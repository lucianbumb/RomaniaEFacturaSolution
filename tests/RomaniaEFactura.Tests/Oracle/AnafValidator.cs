using System.Diagnostics;
using System.Text.RegularExpressions;

namespace RomaniaEFactura.Tests.Oracle;

/// <summary>
/// Drives ANAF's own offline validator (<c>ROeFacturaValidator.jar</c>) so our C# rule engine can
/// be checked against the authority rather than against our reading of the specification.
/// </summary>
/// <remarks>
/// This is a test-only dependency. It is never shipped, and nothing in <c>src/</c> may reference
/// it. Point <c>ROEFACTURA_VALIDATOR_HOME</c> at an unpacked
/// <c>roefacturavalidator_12122024.zip</c>; when the variable is absent the oracle tests skip.
/// </remarks>
public static partial class AnafValidator
{
    /// <summary>Environment variable naming the unpacked validator directory.</summary>
    public const string HomeVariable = "ROEFACTURA_VALIDATOR_HOME";

    /// <summary>The validator directory, or <see langword="null"/> when it is not configured.</summary>
    public static string? Home
    {
        get
        {
            var home = Environment.GetEnvironmentVariable(HomeVariable);
            return string.IsNullOrWhiteSpace(home) || !File.Exists(Path.Combine(home, "ROeFacturaValidator.jar"))
                ? null
                : home;
        }
    }

    /// <summary>Whether the validator can be run on this machine.</summary>
    public static bool IsAvailable => Home is not null && JavaExecutable is not null;

    /// <summary>
    /// The Java runtime to use — the JRE bundled with the validator when present, otherwise
    /// whatever <c>java</c> is on the PATH (which is how CI runs it, via actions/setup-java).
    /// </summary>
    private static string? JavaExecutable
    {
        get
        {
            if (Home is not { } home) return null;

            var bundled = Path.Combine(home, "jre11", "bin",
                OperatingSystem.IsWindows() ? "java.exe" : "java");
            if (File.Exists(bundled)) return bundled;

            return OperatingSystem.IsWindows() ? "java.exe" : "java";
        }
    }

    /// <summary>
    /// Validates a UBL document and returns what ANAF's validator concluded.
    /// </summary>
    /// <param name="xml">The document to validate.</param>
    /// <param name="standard"><c>FACT1</c> for an invoice, <c>FCN</c> for a credit note.</param>
    public static AnafValidatorResult Validate(string xml, string standard)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        ArgumentException.ThrowIfNullOrWhiteSpace(standard);

        if (Home is not { } home || JavaExecutable is not { } java)
        {
            throw new InvalidOperationException(
                $"ANAF validator is not available. Set {HomeVariable} to an unpacked validator directory.");
        }

        // The validator writes its report next to the input file, so give each run its own
        // directory. Parallel test execution would otherwise have runs overwrite each other.
        var workDir = Path.Combine(Path.GetTempPath(), "roefactura-oracle", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            var inputPath = Path.Combine(workDir, "document.xml");
            File.WriteAllText(inputPath, xml);

            var psi = new ProcessStartInfo(java)
            {
                WorkingDirectory = home,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-jar");
            psi.ArgumentList.Add(Path.Combine(home, "ROeFacturaValidator.jar"));
            psi.ArgumentList.Add("-t");
            psi.ArgumentList.Add(standard);
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add(inputPath);

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start the ANAF validator process.");

            if (!process.WaitForExit(milliseconds: 120_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("The ANAF validator did not finish within 120 seconds.");
            }

            // The report is written as RASP_<inputFileName>.txt beside the input.
            var reportPath = Path.Combine(workDir, "RASP_document.xml.txt");
            if (!File.Exists(reportPath))
            {
                var stderr = process.StandardError.ReadToEnd();
                var stdout = process.StandardOutput.ReadToEnd();
                throw new InvalidOperationException(
                    $"The ANAF validator produced no report.{Environment.NewLine}stdout: {stdout}{Environment.NewLine}stderr: {stderr}");
            }

            return Parse(File.ReadAllText(reportPath));
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    /// <summary>
    /// Parses the validator's plain-text report.
    /// </summary>
    /// <remarks>
    /// A clean document reports "<c>document.xml este valid.</c>"; otherwise each finding appears
    /// on its own line as "<c>- textEroare=[BR-CODE]-Romanian text #English text</c>". Some
    /// findings — the CIF control-digit check and the XSD failures among them — carry no rule
    /// code, so <see cref="AnafValidatorFinding.Code"/> is null for those.
    /// </remarks>
    internal static AnafValidatorResult Parse(string report)
    {
        var findings = new List<AnafValidatorFinding>();

        foreach (var raw in report.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (!line.StartsWith("- ", StringComparison.Ordinal)) continue;

            var text = line[2..].Trim();
            if (text.StartsWith("textEroare=", StringComparison.Ordinal))
            {
                text = text["textEroare=".Length..].Trim();
            }

            var match = RuleCodePattern().Match(text);
            findings.Add(match.Success
                ? new AnafValidatorFinding(match.Groups["code"].Value, match.Groups["message"].Value.Trim())
                : new AnafValidatorFinding(null, text));
        }

        var isValid = findings.Count == 0
            && report.Contains("este valid", StringComparison.OrdinalIgnoreCase);

        return new AnafValidatorResult(isValid, findings, report);
    }

    [GeneratedRegex(@"^\[(?<code>[A-Za-z0-9\-]+)\]-(?<message>.*)$", RegexOptions.Singleline)]
    private static partial Regex RuleCodePattern();
}

/// <summary>What ANAF's validator concluded about a document.</summary>
/// <param name="IsValid">Whether the validator reported the document as valid.</param>
/// <param name="Findings">Everything the validator objected to.</param>
/// <param name="RawReport">The unparsed report, for diagnosing a surprising result.</param>
public sealed record AnafValidatorResult(
    bool IsValid,
    IReadOnlyList<AnafValidatorFinding> Findings,
    string RawReport)
{
    /// <summary>The distinct rule codes reported, ignoring findings that carry no code.</summary>
    public IReadOnlyList<string> Codes { get; } =
        [.. Findings.Where(f => f.Code is not null).Select(f => f.Code!).Distinct()];

    /// <summary>A compact description suitable for an assertion failure message.</summary>
    public override string ToString() =>
        IsValid
            ? "valid"
            : $"invalid: {string.Join("; ", Findings.Select(f => f.Code ?? f.Message))}";
}

/// <summary>A single objection raised by ANAF's validator.</summary>
/// <param name="Code">The rule code, such as <c>BR-RO-001</c>, or null when the finding carries none.</param>
/// <param name="Message">The message text.</param>
public sealed record AnafValidatorFinding(string? Code, string Message);
