using System.Reflection;

namespace RomaniaEFactura.Tests.Transport;

/// <summary>
/// Enforces the rule the transport layer is built around.
/// </summary>
/// <remarks>
/// ANAF signals failure inside HTTP 200 on every endpoint, so any status-code branch outside the
/// envelope reader is a latent bug that reads a failure as a success. This guard fails if one is
/// reintroduced. It cannot go red through a behavioural change alone — it exists to catch a
/// plausible future edit, which is a case a normal test would never cover.
/// </remarks>
public class EnvelopeRuleGuardTests
{
    [Fact]
    public void NoSourceFileOutsideTheEnvelopeReaderChecksTheStatusCode()
    {
        var sourceRoot = FindSourceRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name is "AnafEnvelope.cs") continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;

            foreach (var (line, number) in File.ReadLines(file).Select((l, i) => (l, i + 1)))
            {
                var code = line.TrimStart();
                // Ignore documentation, which names the rule deliberately.
                if (code.StartsWith("//", StringComparison.Ordinal)) continue;

                if (code.Contains("IsSuccessStatusCode", StringComparison.Ordinal)
                    || code.Contains("EnsureSuccessStatusCode", StringComparison.Ordinal))
                {
                    offenders.Add($"{name}:{number}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "ANAF reports failures inside HTTP 200, so only AnafEnvelope may judge a response. "
            + $"Found status-code checks in: {string.Join(", ", offenders)}");
    }

    private static string FindSourceRoot()
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(
            Assembly.GetExecutingAssembly().Location)!);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "RomaniaEFactura");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate src/RomaniaEFactura from the test output directory.");
    }
}
