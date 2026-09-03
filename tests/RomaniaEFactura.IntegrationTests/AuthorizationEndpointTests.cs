using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Authentication;
using RomaniaEFactura.Persistence;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura.IntegrationTests;

/// <summary>
/// Who is allowed to drive the two endpoints that connect a company to ANAF.
/// </summary>
/// <remarks>
/// <para>
/// The callback writes an ANAF authorization into the token store, and
/// <c>EfCoreTokenStore.SaveAsync</c> overwrites the row for a CIF unconditionally. An open
/// callback therefore lets anyone holding their own qualified certificate walk the ordinary flow
/// and replace a company's stored authorization with one of their own — after which every ANAF
/// call is made under an identity with no rights for that company, and undoing it needs the real
/// certificate holder.
/// </para>
/// <para>
/// The protected state does not cover this. It stops a callback being forged, which is a different
/// attack from walking the real flow against an endpoint that asks nothing of the caller.
/// </para>
/// </remarks>
public class AuthorizationEndpointTests
{
    private const string Cif = "12345674";

    [Fact]
    public async Task AnAnonymousRequestCannotStartAnAuthorization()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var response = await client.GetAsync($"/efactura/connect/{Cif}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnAnonymousRequestCannotCompleteACallback()
    {
        // The one that matters: this is the request that writes to the token store.
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/efactura/callback?code=whatever&state=whatever");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await StoredCifsAsync(host));
    }

    [Fact]
    public async Task AnAuthenticatedRequestIsLetThrough()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var response = await Get(client, $"/efactura/connect/{Cif}", user: "alice");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://oauth.invalid/", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APolicyIsAppliedWhenOneIsNamed()
    {
        using var host = await CreateHostAsync(configure: o => o.Policy = "administrators");
        using var client = host.GetTestClient();

        // Authenticated, but not an administrator.
        var response = await Get(client, $"/efactura/connect/{Cif}", user: "alice");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MappingWithoutAnAuthorizationServiceIsRefusedAtStartup()
    {
        // Louder than the alternative: without this the same mistake surfaces as an exception from
        // the routing middleware the first time somebody clicks "connect", long after deployment.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateHostAsync(registerAuthorization: false));

        Assert.Contains("AllowAnonymousAccess", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllowAnonymousAccessMountsThemOpen()
    {
        // The escape hatch for an application with no user accounts at all. It has to work, and it
        // has to be the thing somebody typed on purpose.
        using var host = await CreateHostAsync(
            registerAuthorization: false,
            configure: o => o.AllowAnonymousAccess = true);
        using var client = host.GetTestClient();

        var response = await client.GetAsync($"/efactura/connect/{Cif}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task TheGroupIsReturnedSoFurtherConventionsCanBeApplied()
    {
        // Guard against a regression that cannot fail at runtime: returning IEndpointRouteBuilder
        // instead of the group made RequireAuthorization a compile error for the consumer, which is
        // how these endpoints came to be unprotectable. The convention added below is the proof —
        // it does not compile unless the return type is still a convention builder.
        using var host = await CreateHostAsync(extraConvention: group => group.WithDisplayName("efactura authorization"));
        using var client = host.GetTestClient();

        var response = await client.GetAsync($"/efactura/connect/{Cif}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AMalformedCifIsABadRequestRatherThanAFault()
    {
        // The CIF comes from the path. Without the check it reaches BuildAuthorizationUrl as an
        // argument exception, and whoever mistyped it gets a 500.
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var response = await Get(client, "/efactura/connect/12345678", user: "alice");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ------------------------------------------ who may connect which company

    [Fact]
    public async Task AUserWhoMayNotConnectACompanyIsRefusedBeforeAnafIsInvolved()
    {
        // The multi-tenant half of the authentication requirement. Being signed in says nothing
        // about which businesses a person may act for, and the CIF arrives in the path.
        using var host = await CreateHostAsync(authorizer: new AllowNobody());
        using var client = host.GetTestClient();

        var response = await Get(client, $"/efactura/connect/{Cif}", user: "alice");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ACallbackForACompanyTheUserMayNotConnectStoresNothing()
    {
        // Checked again at the callback, not only at connect: entitlement can be withdrawn during
        // the round trip, and a state minted while it held would otherwise still write a token.
        using var host = await CreateHostAsync(authorizer: new AllowNobody());
        using var client = host.GetTestClient();

        var state = StateFor(host, user: "alice");
        var response = await Get(client, $"/efactura/callback?code=good&state={Uri.EscapeDataString(state)}", user: "alice");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await StoredCifsAsync(host));
    }

    [Fact]
    public async Task AUserEntitledToTheCompanyIsLetThrough()
    {
        using var host = await CreateHostAsync(authorizer: new AllowEverybody());
        using var client = host.GetTestClient();

        var response = await Get(client, $"/efactura/connect/{Cif}", user: "alice");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task WithNoAuthorizerOnlyTheConfiguredCompanyMayBeConnected()
    {
        // The default. Correct for a deployment that names one company, and refusing every other
        // is the safe direction for one that has not thought about it yet.
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var mine = await Get(client, $"/efactura/connect/{Cif}", user: "alice");
        var theirs = await Get(client, "/efactura/connect/19867705", user: "alice");

        Assert.Equal(HttpStatusCode.Redirect, mine.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, theirs.StatusCode);
    }

    // -------------------------------------------------- returning to the app

    [Fact]
    public async Task ALocalReturnUrlIsStillHonoured()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var state = StateFor(host, user: "alice", returnUrl: "/invoices");
        var response = await Get(client, $"/efactura/callback?code=good&state={Uri.EscapeDataString(state)}", user: "alice");

        Assert.StartsWith("/invoices?", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOffOriginReturnUrlIsRefusedByDefault()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var state = StateFor(host, user: "alice", returnUrl: "https://app.example.ro/done");
        var response = await Get(client, $"/efactura/callback?code=good&state={Uri.EscapeDataString(state)}", user: "alice");

        Assert.StartsWith("/?", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAllowedOriginIsHonoured()
    {
        // The case a separate SPA or PWA needs: the interface is not served by this host.
        using var host = await CreateHostAsync(allowedOrigins: ["https://app.example.ro"]);
        using var client = host.GetTestClient();

        var state = StateFor(host, user: "alice", returnUrl: "https://app.example.ro/done");
        var response = await Get(client, $"/efactura/callback?code=good&state={Uri.EscapeDataString(state)}", user: "alice");

        Assert.StartsWith("https://app.example.ro/done?", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://app.example.ro.evil.test/done")]
    [InlineData("http://app.example.ro/done")]
    [InlineData("https://app.example.ro:8443/done")]
    [InlineData("https://evil.test/done")]
    public async Task AnOriginThatOnlyLooksAllowedIsRefused(string returnUrl)
    {
        // The first is the one an allow-list gets wrong: it has the allowed origin as a text
        // prefix. The others differ by scheme and by port, which are part of an origin.
        using var host = await CreateHostAsync(allowedOrigins: ["https://app.example.ro"]);
        using var client = host.GetTestClient();

        var state = StateFor(host, user: "alice", returnUrl: returnUrl);
        var response = await Get(client, $"/efactura/callback?code=good&state={Uri.EscapeDataString(state)}", user: "alice");

        Assert.StartsWith("/?", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    // ------------------------------------------------- binding the round trip

    [Fact]
    public async Task ACallbackCompletedByADifferentUserIsRefused()
    {
        // Login CSRF between two authenticated users: without the binding, anyone who can sign in
        // can start a flow, capture the state, and hand an administrator a link that finishes the
        // attacker's ANAF authorization under the application's own company.
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var state = StateFor(host, user: "mallory");
        var response = await Get(client, $"/efactura/callback?code=good&state={Uri.EscapeDataString(state)}", user: "alice");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await StoredCifsAsync(host));
    }

    [Fact]
    public async Task ACallbackCompletedByTheUserWhoStartedItIsAccepted()
    {
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var state = StateFor(host, user: "alice");
        var response = await Get(client, $"/efactura/callback?code=good&state={Uri.EscapeDataString(state)}", user: "alice");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal([Cif], await StoredCifsAsync(host));
    }

    [Fact]
    public async Task AStateCarryingNoUserIsStillAccepted()
    {
        // An application that builds the URL itself through IRomaniaEFacturaService may not supply
        // one. That state binds nothing, and refusing it would break a legitimate caller; the
        // authorization requirement is what protects this case.
        using var host = await CreateHostAsync();
        using var client = host.GetTestClient();

        var state = StateFor(host, user: null);
        var response = await Get(client, $"/efactura/callback?code=good&state={Uri.EscapeDataString(state)}", user: "alice");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    // ------------------------------------------------------------- the harness

    private static string StateFor(IHost host, string? user, string? returnUrl = "/invoices") =>
        host.Services.GetRequiredService<OAuthStateProtector>().Protect(Cif, returnUrl, user);

    private static async Task<IReadOnlyList<string>> StoredCifsAsync(IHost host) =>
        await host.Services.GetRequiredService<IEFacturaTokenStore>().ListAuthorizedCifsAsync();

    private static Task<HttpResponseMessage> Get(HttpClient client, string path, string user)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(TestUserHandler.UserHeader, user);
        return client.SendAsync(request);
    }

    private static async Task<IHost> CreateHostAsync(
        bool registerAuthorization = true,
        Action<EFacturaAuthorizationEndpointOptions>? configure = null,
        Action<Microsoft.AspNetCore.Routing.RouteGroupBuilder>? extraConvention = null,
        IEFacturaConnectAuthorizer? authorizer = null,
        string[]? allowedOrigins = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
                    services.AddDataProtection().SetApplicationName("efactura-endpoint-tests");
                    services.AddSingleton<IEFacturaTokenStore, InMemoryTokenStore>();
                    services.AddSingleton<Microsoft.Extensions.Options.IOptions<Configuration.EFacturaOptions>>(
                        Microsoft.Extensions.Options.Options.Create(new Configuration.EFacturaOptions
                        {
                            Cif = Cif,
                            AllowedReturnOrigins = allowedOrigins ?? [],
                        }));
                    services.AddSingleton<IEFacturaConnectAuthorizer>(
                        authorizer ?? new AllowConfiguredOnly(Cif));
                    services.AddSingleton<OAuthStateProtector>();
                    services.AddSingleton<IAnafOAuthClient, FakeOAuthClient>();

                    if (registerAuthorization)
                    {
                        services.AddAuthentication(TestUserHandler.SchemeName)
                            .AddScheme<AuthenticationSchemeOptions, TestUserHandler>(TestUserHandler.SchemeName, null);
                        services.AddAuthorizationBuilder()
                            .AddPolicy("administrators", policy => policy.RequireRole("administrator"));
                    }
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    if (registerAuthorization)
                    {
                        app.UseAuthentication();
                        app.UseAuthorization();
                    }

                    app.UseEndpoints(endpoints =>
                    {
                        var group = endpoints.MapEFacturaAuthorization(configure);
                        extraConvention?.Invoke(group);
                    });
                });
            })
            .StartAsync();

        return host;
    }

    private sealed class AllowNobody : IEFacturaConnectAuthorizer
    {
        public ValueTask<bool> CanConnectAsync(
            System.Security.Claims.ClaimsPrincipal user, string cif, CancellationToken ct = default) =>
            ValueTask.FromResult(false);
    }

    private sealed class AllowEverybody : IEFacturaConnectAuthorizer
    {
        public ValueTask<bool> CanConnectAsync(
            System.Security.Claims.ClaimsPrincipal user, string cif, CancellationToken ct = default) =>
            ValueTask.FromResult(true);
    }

    /// <summary>Stands in for the library default, which allows only the configured company.</summary>
    private sealed class AllowConfiguredOnly(string configured) : IEFacturaConnectAuthorizer
    {
        public ValueTask<bool> CanConnectAsync(
            System.Security.Claims.ClaimsPrincipal user, string cif, CancellationToken ct = default) =>
            ValueTask.FromResult(string.Equals(configured, cif, StringComparison.Ordinal));
    }

    /// <summary>Authenticates whoever the request names, so a test can be two different people.</summary>
    private sealed class TestUserHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";
        public const string UserHeader = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeader, out var user) || user.Count == 0)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, user.ToString())], SchemeName);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    /// <summary>Exchanges any code successfully, so the test is about who may reach the exchange.</summary>
    private sealed class FakeOAuthClient : IAnafOAuthClient
    {
        public Uri BuildAuthorizationUrl(string cif, string? returnUrl = null, string? user = null) =>
            new($"https://oauth.invalid/authorize?cif={cif}");

        public Task<AnafResult<EFacturaToken>> ExchangeCodeAsync(
            string code, string cif, CancellationToken cancellationToken = default) =>
            Task.FromResult(AnafResult<EFacturaToken>.Success(new EFacturaToken
            {
                Cif = cif,
                AccessToken = "access",
                RefreshToken = "refresh",
                AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            }));

        public Task<AnafResult<EFacturaToken>> RefreshAsync(
            EFacturaToken token, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
