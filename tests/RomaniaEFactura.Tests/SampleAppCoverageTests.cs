using System.Reflection;

namespace RomaniaEFactura.Tests;

/// <summary>
/// Every method on <see cref="IRomaniaEFacturaService"/> is reachable from the sample app.
/// </summary>
/// <remarks>
/// <para>
/// The sample's stated purpose is to exercise the whole interface and double as the documentation,
/// which is true the day it is written and quietly stops being true the first time the interface
/// grows a method. This turns that from a claim into a check: add something to the interface and
/// this fails until a page calls it.
/// </para>
/// <para>
/// It searches source text rather than rendering the app, which is crude — a method named in a
/// comment would satisfy it. That is an acceptable trade for a test that needs no renderer and no
/// running server, because the failure it is built to catch is a method nobody surfaced at all,
/// not a method surfaced badly.
/// </para>
/// </remarks>
public class SampleAppCoverageTests
{
    [Fact]
    public void EveryServiceMethodIsCalledSomewhereInTheSampleApp()
    {
        var sources = SampleAppSources();
        Assert.NotEmpty(sources);

        var text = string.Join('\n', sources.Select(File.ReadAllText));

        var missing = typeof(IRomaniaEFacturaService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .Where(name => !text.Contains(name, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"""
             The sample app does not reach {missing.Count} interface method(s):
               {string.Join(Environment.NewLine + "  ", missing)}
             The sample is meant to exercise the whole interface; add a page that calls it.
             """);
    }

    [Fact]
    public void TheSampleRegistersTheLibraryTheWayTheDocumentationSays()
    {
        // If this drifts, every reader's first copy-paste is wrong.
        var program = SampleAppSources()
            .Single(path => Path.GetFileName(path) == "Program.cs");

        var text = File.ReadAllText(program);

        Assert.Contains("AddRomaniaEFactura", text, StringComparison.Ordinal);
        Assert.Contains("MapEFacturaAuthorization", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInvoiceFormUsesTheValidatorThatSeesTheLines()
    {
        // Blazor's own DataAnnotationsValidator would validate the invoice and ignore every line,
        // so a form using it enables its send button on documents ANAF rejects. The sample must
        // demonstrate the right one.
        var form = SampleAppSources()
            .Single(path => Path.GetFileName(path) == "NewInvoice.razor");

        var text = File.ReadAllText(form);

        Assert.Contains("<EFacturaValidator", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<DataAnnotationsValidator", text, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> SampleAppSources()
    {
        var root = FindRepositoryRoot();
        var app = Path.Combine(root, "samples", "SampleWebApp");

        Assert.True(Directory.Exists(app), $"The sample app was not found at {app}.");

        return
        [
            .. Directory.EnumerateFiles(app, "*.razor", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories))
                // obj/ holds generated copies of every component, which would make the search
                // pass on text the author never wrote.
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)),
        ];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "samples"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
