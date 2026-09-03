using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura.Tests.Configuration;

/// <summary>
/// Which company a call is for, when the caller did not say.
/// </summary>
/// <remarks>
/// <para>
/// A single-company application names one in configuration. An application where each of its own
/// registered businesses connects its own authorization has no such company: the CIF belongs to
/// whichever business the request concerns.
/// </para>
/// <para>
/// The order below is the whole feature, and the middle step is the one that matters — a request
/// about one business must never silently fall back to whichever company happens to be configured.
/// </para>
/// </remarks>
public class CompanyResolutionTests
{
    private const string Configured = "12345674";
    private const string Scoped = "19867705";
    private const string Explicit = "80000009";

    [Fact]
    public async Task TheScopesCompanyIsUsedWhenNoneIsPassed()
    {
        var http = new CountingHttpClientFactory();
        var client = CreateClient(http, provider: new FixedCompany(Scoped));

        await client.ListMessagesAsync(days: 7);

        Assert.Equal(Scoped, http.LastCif);
    }

    [Fact]
    public async Task AnExplicitCompanyStillWins()
    {
        // A background job settling one company's submission while the ambient scope says another,
        // or an administrative screen acting across companies, has to be able to be explicit.
        var http = new CountingHttpClientFactory();
        var client = CreateClient(http, provider: new FixedCompany(Scoped));

        await client.ListMessagesAsync(days: 7, cif: Explicit);

        Assert.Equal(Explicit, http.LastCif);
    }

    [Fact]
    public async Task TheConfiguredCompanyIsTheLastResort()
    {
        var http = new CountingHttpClientFactory();
        var client = CreateClient(http, provider: new FixedCompany(null));

        await client.ListMessagesAsync(days: 7);

        Assert.Equal(Configured, http.LastCif);
    }

    [Fact]
    public async Task NoProviderAtAllIsTheSingleCompanyCase()
    {
        // The shape every existing consumer has. It must be untouched by any of this.
        var http = new CountingHttpClientFactory();
        var client = CreateClient(http, provider: null);

        await client.ListMessagesAsync(days: 7);

        Assert.Equal(Configured, http.LastCif);
    }

    [Fact]
    public async Task NothingAnywhereNamesEveryWayOfSupplyingOne()
    {
        // The failure is a wiring mistake, and the reader is looking at a call site that says
        // nothing about companies. It has to name all three routes or it sends them hunting.
        var client = CreateClient(new CountingHttpClientFactory(), provider: null, configuredCif: string.Empty);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.ListMessagesAsync(days: 7));

        Assert.Contains("IEFacturaCompanyProvider", exception.Message, StringComparison.Ordinal);
        Assert.Contains("EFacturaOptions.Cif", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AScopedCompanyIsStillCheckedForValidity()
    {
        // A provider reading a business profile can hand back whatever is stored there, and a
        // fiscal code that fails its control digit should not reach ANAF from here either.
        var client = CreateClient(new CountingHttpClientFactory(), provider: new FixedCompany("12345678"));

        await Assert.ThrowsAsync<ArgumentException>(() => client.ListMessagesAsync(days: 7));
    }

    // ------------------------------------------------------------- the harness

    private static AnafApiClient CreateClient(
        CountingHttpClientFactory http,
        IEFacturaCompanyProvider? provider,
        string configuredCif = Configured) =>
        new(http,
            new StubTokenProvider(),
            Options.Create(new EFacturaOptions
            {
                Cif = configuredCif,
                ApiBaseAddress = new Uri("https://api.invalid/test/FCTEL/rest"),
                MaxRetries = 0,
                MinimumDelayBetweenCalls = TimeSpan.Zero,
            }),
            NullLogger<AnafApiClient>.Instance,
            provider);

    private sealed class FixedCompany(string? cif) : IEFacturaCompanyProvider
    {
        public string? GetCurrentCif() => cif;
    }

    private sealed class StubTokenProvider : IAnafAccessTokenProvider
    {
        public Task<string?> GetAccessTokenAsync(string cif, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("token");
    }

    /// <summary>Records the <c>cif</c> query parameter of the last request.</summary>
    private sealed class CountingHttpClientFactory : IHttpClientFactory
    {
        private readonly RecordingHandler _handler = new();

        public string? LastCif => _handler.LastCif;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);

        private sealed class RecordingHandler : HttpMessageHandler
        {
            public string? LastCif { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
                LastCif = query["cif"];

                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"eroare":"Nu exista mesaje"}"""),
                });
            }
        }
    }
}
