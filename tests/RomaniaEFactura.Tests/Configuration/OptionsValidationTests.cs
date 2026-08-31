using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Configuration;

namespace RomaniaEFactura.Tests.Configuration;

/// <summary>
/// What the library refuses to start with.
/// </summary>
/// <remarks>
/// <para>
/// Startup rather than the first ANAF call, because most of these otherwise surface wearing
/// somebody else's clothes: an empty client secret answers 401, which reads like an expired
/// authorization and sends whoever is debugging it to the certificate holder rather than to
/// appsettings.json.
/// </para>
/// <para>
/// The base-address rules are the ones that matter. Those overrides exist so the library can be
/// pointed at the mock server, and they will just as happily point a production deployment at a
/// plaintext address, taking the bearer token — and on the OAuth address the client secret — with
/// them.
/// </para>
/// </remarks>
public class OptionsValidationTests
{
    [Fact]
    public void AWellConfiguredApplicationStarts()
    {
        using var host = Build(_ => { });

        Assert.NotNull(host.Services.GetRequiredService<IOptions<EFacturaOptions>>().Value);
    }

    [Fact]
    public void AMissingClientSecretIsRefused()
    {
        var message = Refused(o => o.ClientSecret = string.Empty);

        Assert.Contains("ClientSecret", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingClientIdIsRefused()
    {
        Assert.Contains("ClientId", Refused(o => o.ClientId = string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingRedirectUriIsRefused()
    {
        Assert.Contains("RedirectUri", Refused(o => o.RedirectUri = string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public void ARelativeRedirectUriIsRefused()
    {
        // Platform-dependent, which is how CI found the hole this guards. On Unix a leading slash
        // parses as an absolute file:// URI, so this reaches the scheme check rather than the
        // absolute-URI check — hence asserting only that it is refused, and naming the setting.
        Assert.Contains(
            "RedirectUri", Refused(o => o.RedirectUri = "/efactura/callback"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("file:///efactura/callback")]
    [InlineData("ftp://app.example.ro/efactura/callback")]
    public void ARedirectUriThatIsNotHttpIsRefused(string uri)
    {
        // file:// is the one that matters: Uri.IsLoopback is true for it, so a check written as
        // "https or loopback" without a scheme test lets every file:// address through.
        Assert.Contains("RedirectUri", Refused(o => o.RedirectUri = uri), StringComparison.Ordinal);
    }

    [Fact]
    public void ABaseAddressThatIsNotHttpIsRefused()
    {
        Assert.Contains(
            "ApiBaseAddress",
            Refused(o => o.ApiBaseAddress = new Uri("file:///tmp/anaf")),
            StringComparison.Ordinal);
    }

    // ------------------------------------------------------- carrying secrets

    [Fact]
    public void APlaintextApiAddressIsRefused()
    {
        // The finding this rule exists for: the override is meant for the mock server, and the same
        // knob points a production deployment at an address that carries the bearer token in clear.
        var message = Refused(o => o.ApiBaseAddress = new Uri("http://api.example.ro/prod/FCTEL/rest"));

        Assert.Contains("not https", message, StringComparison.Ordinal);
        Assert.Contains("bearer token", message, StringComparison.Ordinal);
    }

    [Fact]
    public void APlaintextOAuthAddressIsRefused()
    {
        Assert.Contains(
            "not https",
            Refused(o => o.OAuthBaseAddress = new Uri("http://logincert.example.ro/anaf-oauth2/v1")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void APlaintextRedirectUriIsRefused()
    {
        Assert.Contains(
            "not https",
            Refused(o => o.RedirectUri = "http://app.example.ro/efactura/callback"),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://localhost:5049/test/FCTEL/rest")]
    [InlineData("http://127.0.0.1:5049/test/FCTEL/rest")]
    [InlineData("http://[::1]:5049/test/FCTEL/rest")]
    public void APlaintextLoopbackAddressIsAccepted(string address)
    {
        // Loopback is the seam rather than a blanket https rule. Pointing the library at the mock
        // server is supported and necessary, and there is no transit to protect.
        using var host = Build(o => o.ApiBaseAddress = new Uri(address));

        Assert.NotNull(host.Services.GetRequiredService<IOptions<EFacturaOptions>>().Value);
    }

    [Fact]
    public void APlaintextLoopbackRedirectUriIsAccepted()
    {
        using var host = Build(o => o.RedirectUri = "http://localhost:5203/efactura/callback");

        Assert.NotNull(host.Services.GetRequiredService<IOptions<EFacturaOptions>>().Value);
    }

    // -------------------------------------------------------------------- CIF

    [Fact]
    public void AMalformedCifIsRefused()
    {
        Assert.Contains("control digit", Refused(o => o.Cif = "12345678"), StringComparison.Ordinal);
    }

    [Fact]
    public void NoCifAtAllIsAccepted()
    {
        // Deliberately optional. A deployment serving several companies passes the CIF per call,
        // which the interface is built for throughout; requiring it here would outlaw that.
        using var host = Build(o => o.Cif = string.Empty);

        Assert.NotNull(host.Services.GetRequiredService<IOptions<EFacturaOptions>>().Value);
    }

    // ------------------------------------------------------------- the numbers

    [Fact]
    public void ANonPositiveReconcileIntervalIsRefused()
    {
        // PeriodicTimer rejects it, so the reconciler would die on its first tick — inside a
        // background service, where nothing reports that it stopped.
        Assert.Contains(
            "ReconcileInterval", Refused(o => o.ReconcileInterval = TimeSpan.Zero), StringComparison.Ordinal);
    }

    [Fact]
    public void AZeroBatchSizeIsRefused()
    {
        Assert.Contains(
            "ReconcileBatchSize", Refused(o => o.ReconcileBatchSize = 0), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFailureIsReportedAtOnce()
    {
        // One restart per mistake would be a poor trade for a check that exists to save time.
        var message = Refused(o =>
        {
            o.ClientId = string.Empty;
            o.ClientSecret = string.Empty;
            o.RedirectUri = string.Empty;
        });

        Assert.Contains("ClientId", message, StringComparison.Ordinal);
        Assert.Contains("ClientSecret", message, StringComparison.Ordinal);
        Assert.Contains("RedirectUri", message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- the harness

    private static string Refused(Action<EFacturaOptions> configure)
    {
        var exception = Assert.Throws<OptionsValidationException>(() => Build(configure).Dispose());
        return string.Join(" ", exception.Failures);
    }

    /// <summary>
    /// Builds and starts a host, which is where <c>ValidateOnStart</c> bites. Resolving the options
    /// would validate too, but only if something resolved them — and a misconfigured deployment
    /// that nobody has clicked "connect" on yet would start clean.
    /// </summary>
    private static IHost Build(Action<EFacturaOptions> configure)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        builder.AddRomaniaEFactura(
            options =>
            {
                options.ClientId = "test-client";
                options.ClientSecret = "test-secret";
                options.RedirectUri = "https://app.example.ro/efactura/callback";
                options.Cif = "12345674";
                options.EnableReconciler = false;
                configure(options);
            },
            db => db.UseSqlite("Data Source=:memory:"));

        var host = builder.Build();
        host.Start();
        return host;
    }
}
