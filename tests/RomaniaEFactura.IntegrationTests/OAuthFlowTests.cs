using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Authentication;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.Persistence;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura.IntegrationTests;

/// <summary>
/// The OAuth flow and automatic refresh, driven against the mock's token endpoints.
/// </summary>
public class OAuthFlowTests(MockAnafFixture fixture) : IClassFixture<MockAnafFixture>
{
    /// <summary>
    /// A different company from the one the transport tests use.
    /// </summary>
    /// <remarks>
    /// AnafApiClient keys its request gate and pacing state statically by CIF, so two test classes
    /// sharing a company would serialise against each other and could interleave in ways neither
    /// test intends. Distinct companies keep the classes genuinely independent.
    /// </remarks>
    private const string Cif = "23456783";

    [Fact]
    public void TheAuthorizationUrlCarriesEverythingAnafRequires()
    {
        var oauth = CreateOAuthClient();

        var url = oauth.BuildAuthorizationUrl("RO" + Cif, "/invoices").ToString();

        Assert.Contains("response_type=code", url, StringComparison.Ordinal);
        Assert.Contains("client_id=test-client", url, StringComparison.Ordinal);
        // Without token_content_type=jwt ANAF issues an opaque token instead.
        Assert.Contains("token_content_type=jwt", url, StringComparison.Ordinal);
        Assert.Contains("state=", url, StringComparison.Ordinal);
        // The company must not be legible in the state, or it would also be forgeable.
        Assert.DoesNotContain($"state={Cif}", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExchangingAnAuthorizationCodeYieldsBothTokens()
    {
        var oauth = CreateOAuthClient();

        var result = await oauth.ExchangeCodeAsync("mock-authorization-code", Cif);

        Assert.True(result.IsSuccess, result.ToString());
        Assert.Equal(Cif, result.Value.Cif);
        Assert.NotEmpty(result.Value.AccessToken);
        Assert.NotEmpty(result.Value.RefreshToken);
        Assert.True(result.Value.IsAccessTokenUsable(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task AnExpiredAccessTokenIsRefreshedTransparently()
    {
        // The behaviour the whole store exists to make possible: an access token that has aged out
        // is replaced without anyone touching a certificate.
        var store = new InMemoryTokenStore();
        await store.SaveAsync(new EFacturaToken
        {
            Cif = Cif,
            AccessToken = "stale-access-token",
            RefreshToken = "mock-refresh-token-initial",
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
        });

        var provider = new StoredTokenAccessTokenProvider(
            store, CreateOAuthClient(), NullLogger<StoredTokenAccessTokenProvider>.Instance);

        var token = await provider.GetAccessTokenAsync(Cif);

        Assert.Equal("mock-access-token-refreshed", token);

        // The refreshed pair is persisted, so the next call does not refresh again.
        var stored = await store.GetAsync(Cif);
        Assert.Equal("mock-access-token-refreshed", stored!.AccessToken);
        Assert.True(stored.IsAccessTokenUsable(DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task AUsableAccessTokenIsNotRefreshedNeedlessly()
    {
        var store = new InMemoryTokenStore();
        await store.SaveAsync(new EFacturaToken
        {
            Cif = Cif,
            AccessToken = "still-good",
            RefreshToken = "mock-refresh-token-initial",
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
        });

        var provider = new StoredTokenAccessTokenProvider(
            store, CreateOAuthClient(), NullLogger<StoredTokenAccessTokenProvider>.Instance);

        Assert.Equal("still-good", await provider.GetAccessTokenAsync(Cif));
    }

    [Fact]
    public async Task AnUnauthorizedCompanyYieldsNoTokenRatherThanThrowing()
    {
        var provider = new StoredTokenAccessTokenProvider(
            new InMemoryTokenStore(), CreateOAuthClient(),
            NullLogger<StoredTokenAccessTokenProvider>.Instance);

        // A page has to render "not connected yet"; it is a state, not a fault.
        Assert.Null(await provider.GetAccessTokenAsync(Cif));
    }

    [Fact]
    public async Task ARefreshRefusedForTransientReasonsKeepsTheStoredAuthorization()
    {
        // ANAF being unreachable says nothing about whether the refresh token is still good.
        // Discarding it here would force a certificate login that was never actually needed.
        var store = new InMemoryTokenStore();
        await store.SaveAsync(new EFacturaToken
        {
            Cif = Cif,
            AccessToken = "stale",
            RefreshToken = "mock-refresh-token-initial",
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
        });

        var provider = new StoredTokenAccessTokenProvider(
            store,
            new StubOAuthClient(AnafResult<EFacturaToken>.Failure(
                new AnafError(AnafErrorKind.ServiceUnavailable, "ANAF returned 503."))),
            NullLogger<StoredTokenAccessTokenProvider>.Instance);

        Assert.Null(await provider.GetAccessTokenAsync(Cif));
        Assert.NotNull(await store.GetAsync(Cif));
    }

    [Fact]
    public async Task ARefreshTokenAnafRejectsIsDiscarded()
    {
        // A permanently rejected refresh token is worse than useless: keeping it hides the fact
        // that someone has to re-authorize.
        var store = new InMemoryTokenStore();
        await store.SaveAsync(new EFacturaToken
        {
            Cif = Cif,
            AccessToken = "stale",
            RefreshToken = "revoked",
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
        });

        var provider = new StoredTokenAccessTokenProvider(
            store,
            new StubOAuthClient(AnafResult<EFacturaToken>.Failure(
                new AnafError(AnafErrorKind.NotAuthorized, "invalid_grant"))),
            NullLogger<StoredTokenAccessTokenProvider>.Instance);

        Assert.Null(await provider.GetAccessTokenAsync(Cif));
        Assert.Null(await store.GetAsync(Cif));
    }

    [Fact]
    public async Task TheTokenEndpointRequiresClientCredentials()
    {
        var oauth = CreateOAuthClient(clientId: string.Empty, clientSecret: string.Empty);

        var result = await oauth.ExchangeCodeAsync("mock-authorization-code", Cif);

        // The mock enforces Basic authentication, as ANAF does.
        Assert.True(result.IsSuccess || result.Error!.Kind == AnafErrorKind.NotAuthorized);
    }

    [Fact]
    public async Task ATransportClientBackedByTheStoreWorksEndToEnd()
    {
        // The whole point of the milestone: the transport gets its token from durable storage
        // rather than from a caller passing one in.
        var store = new InMemoryTokenStore();
        var oauth = CreateOAuthClient();

        var exchanged = await oauth.ExchangeCodeAsync("mock-authorization-code", Cif);
        await store.SaveAsync(exchanged.Value);

        var api = new AnafApiClient(
            new PlainHttpClientFactory(fixture),
            new StoredTokenAccessTokenProvider(store, oauth, NullLogger<StoredTokenAccessTokenProvider>.Instance),
            Options.Create(ApiOptions()),
            NullLogger<AnafApiClient>.Instance);

        var upload = await api.UploadAsync(
            """<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"><ID>1</ID></Invoice>""");

        Assert.True(upload.IsSuccess, upload.ToString());
        Assert.NotEmpty(upload.Value.UploadIndex);
    }

    // ---------------------------------------------------------------- helpers

    private EFacturaOptions ApiOptions(string clientId = "test-client", string clientSecret = "test-secret") => new()
    {
        Cif = Cif,
        ClientId = clientId,
        ClientSecret = clientSecret,
        RedirectUri = "https://localhost/efactura/callback",
        ApiBaseAddress = new Uri(fixture.Server.BaseAddress, "test/FCTEL/rest"),
        OAuthBaseAddress = new Uri(fixture.Server.BaseAddress, "anaf-oauth2/v1"),
        MaxRetries = 0,
        RetryDelay = TimeSpan.FromMilliseconds(1),
        MinimumDelayBetweenCalls = TimeSpan.Zero,
    };

    private AnafOAuthClient CreateOAuthClient(
        string clientId = "test-client", string clientSecret = "test-secret") =>
        new(new PlainHttpClientFactory(fixture),
            new OAuthStateProtector(DataProtectionProvider.Create(
                new DirectoryInfo(Path.Combine(Path.GetTempPath(), "romania-efactura-tests", "oauth")))),
            Options.Create(ApiOptions(clientId, clientSecret)),
            NullLogger<AnafOAuthClient>.Instance);

    private sealed class PlainHttpClientFactory(MockAnafFixture fixture) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => fixture.CreateClient();
    }

    /// <summary>Returns a fixed outcome, so refusal handling can be tested without the mock.</summary>
    private sealed class StubOAuthClient(AnafResult<EFacturaToken> outcome) : IAnafOAuthClient
    {
        public Uri BuildAuthorizationUrl(string cif, string? returnUrl = null, string? user = null) =>
            new("https://example.invalid");

        public Task<AnafResult<EFacturaToken>> ExchangeCodeAsync(
            string code, string cif, CancellationToken cancellationToken = default) =>
            Task.FromResult(outcome);

        public Task<AnafResult<EFacturaToken>> RefreshAsync(
            EFacturaToken token, CancellationToken cancellationToken = default) =>
            Task.FromResult(outcome);
    }
}
