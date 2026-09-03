using System.Reflection;

namespace RomaniaEFactura.Tests;

/// <summary>
/// That the contracts package takes on no infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// The split exists so a layered application can hold the invoice rules in a layer forbidden from
/// depending on HTTP or persistence — a common rule, and one an architecture test can enforce. If
/// <c>RomaniaEFactura.Abstractions</c> quietly acquires ASP.NET Core or Entity Framework Core, that
/// consumer's architecture test fails and the cause is in this repository, not theirs.
/// </para>
/// <para>
/// A split like this rots the first time somebody adds a using and nothing complains. This is the
/// thing that complains.
/// </para>
/// </remarks>
public class AbstractionsDependencyTests
{
    /// <summary>Assembly name prefixes the contracts package must not reach for.</summary>
    private static readonly string[] Forbidden =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Logging",
        "Npgsql",
    ];

    [Fact]
    public void TheProjectFileDeclaresNoInfrastructureDependency()
    {
        // The check that actually protects a consumer, and the reason the runtime one below is not
        // enough on its own: Roslyn emits an assembly reference only for an assembly whose types
        // are used, so a PackageReference nobody has used yet is invisible there — while still
        // landing in the dependency graph of everyone who installs the package, and still failing
        // their architecture test.
        var project = File.ReadAllText(ProjectFile("RomaniaEFactura.Abstractions"));

        foreach (var forbidden in Forbidden)
        {
            Assert.DoesNotContain(
                $"Include=\"{forbidden}",
                project,
                StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Microsoft.AspNetCore.App", project, StringComparison.Ordinal);
    }

    [Fact]
    public void AbstractionsHaveNoInfrastructureDependency()
    {
        var abstractions = typeof(global::RomaniaEFactura.IRomaniaEFacturaService).Assembly;

        Assert.Equal("RomaniaEFactura.Abstractions", abstractions.GetName().Name);

        var offenders = abstractions.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => Forbidden.Any(f => name.StartsWith(f, StringComparison.Ordinal)))
            .Order()
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "RomaniaEFactura.Abstractions must not reference infrastructure, and now references: "
            + string.Join(", ", offenders)
            + ". The package exists so an application layer can depend on the contracts without "
            + "taking on HTTP or persistence; a reference here breaks that for every consumer.");
    }

    [Fact]
    public void TheContractsAreWhereAConsumerWouldExpect()
    {
        // The types an application layer actually touches. Named individually rather than counted,
        // so moving one back into the implementation package fails here with its name.
        var abstractions = typeof(global::RomaniaEFactura.IRomaniaEFacturaService).Assembly;

        foreach (var type in (Type[])
        [
            typeof(global::RomaniaEFactura.IRomaniaEFacturaService),
            typeof(global::RomaniaEFactura.EditModels.InvoiceEditModel),
            typeof(global::RomaniaEFactura.EditModels.CreditNoteEditModel),
            typeof(global::RomaniaEFactura.EditModels.BuyerMessageEditModel),
            typeof(global::RomaniaEFactura.EditModels.EditModelValidator),
            typeof(global::RomaniaEFactura.Validation.ValidationReport),
            typeof(global::RomaniaEFactura.Transport.AnafError),
            typeof(global::RomaniaEFactura.Ubl.UblInvoice),
            typeof(global::RomaniaEFactura.Lookup.CompanyLookup),
            typeof(global::RomaniaEFactura.Configuration.IEFacturaCompanyProvider),
        ])
        {
            Assert.Same(abstractions, type.Assembly);
        }
    }

    [Fact]
    public void TheImplementationIsNotInTheContractsPackage()
    {
        // The other direction. Everything that talks to ANAF, a database or a browser belongs in
        // the package a composition root references, not the one domain rules do.
        var implementation = typeof(global::RomaniaEFactura.ServiceCollectionExtensions).Assembly;

        Assert.Equal("RomaniaEFactura", implementation.GetName().Name);

        foreach (var type in (Type[])
        [
            typeof(global::RomaniaEFactura.RomaniaEFacturaService),
            typeof(global::RomaniaEFactura.Transport.AnafApiClient),
            typeof(global::RomaniaEFactura.Persistence.EFacturaDbContext),
            typeof(global::RomaniaEFactura.Authentication.EFacturaAuthorizationEndpoints),
            typeof(global::RomaniaEFactura.EditModels.EFacturaValidator),
            typeof(global::RomaniaEFactura.Reconciliation.InboxSweeper),
        ])
        {
            Assert.Same(implementation, type.Assembly);
        }
    }

    [Fact]
    public void BothPackagesCarryTheSameVersion()
    {
        // They are published together and one references the other, so a consumer taking a
        // mismatched pair would get a binding failure rather than a useful error.
        var abstractions = typeof(global::RomaniaEFactura.IRomaniaEFacturaService).Assembly;
        var implementation = typeof(global::RomaniaEFactura.ServiceCollectionExtensions).Assembly;

        Assert.Equal(Informational(abstractions), Informational(implementation));
    }

    private static string? Informational(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    /// <summary>
    /// Finds a project file by walking up to the solution.
    /// </summary>
    /// <remarks>
    /// Reading the project file is the only way to see a dependency that is declared but not yet
    /// used, and that is exactly the dependency this guard exists to catch.
    /// </remarks>
    private static string ProjectFile(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RomaniaEFacturaSolution.sln")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "Could not find the solution root from " + AppContext.BaseDirectory);

        var path = Path.Combine(directory!.FullName, "src", name, name + ".csproj");
        Assert.True(File.Exists(path), $"Expected the project file at {path}.");

        return path;
    }
}
