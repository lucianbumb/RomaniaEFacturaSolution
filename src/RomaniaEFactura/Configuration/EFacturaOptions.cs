namespace RomaniaEFactura.Configuration;

/// <summary>Which ANAF environment to talk to.</summary>
public enum EFacturaEnvironment
{
    /// <summary>ANAF's test environment.</summary>
    Test = 0,

    /// <summary>Production.</summary>
    Production,
}

/// <summary>
/// Configuration for the e-Factura client.
/// </summary>
public sealed class EFacturaOptions
{
    /// <summary>The configuration section this binds to by default.</summary>
    public const string SectionName = "EFactura";

    /// <summary>Which ANAF environment to use.</summary>
    public EFacturaEnvironment Environment { get; set; } = EFacturaEnvironment.Test;

    /// <summary>OAuth client identifier issued when the application was registered with ANAF.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth client secret issued alongside <see cref="ClientId"/>.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Where ANAF sends the authorization code. Must match the registered value exactly.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// The company this application submits for. Individual calls may override it, so one
    /// deployment can serve several companies without reconfiguration.
    /// </summary>
    public string Cif { get; set; } = string.Empty;

    /// <summary>
    /// Overrides the API base address. Set this to point at the mock server; leave it unset to use
    /// the environment's real address.
    /// </summary>
    /// <remarks>
    /// The previous version computed its base address from the environment with no way to override
    /// it, which made the library impossible to exercise locally.
    /// </remarks>
    public Uri? ApiBaseAddress { get; set; }

    /// <summary>Overrides the OAuth base address, for the same reason.</summary>
    public Uri? OAuthBaseAddress { get; set; }

    /// <summary>How long to wait for a single ANAF call.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>How many times to retry a call ANAF rate-limited or failed to serve.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>The delay before the first retry. Each subsequent retry doubles it.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The smallest gap between two calls for the same company. ANAF throttles aggressively, and a
    /// burst of downloads is the quickest way to be rate-limited.
    /// </summary>
    public TimeSpan MinimumDelayBetweenCalls { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>The API base address actually in use.</summary>
    public Uri ResolvedApiBaseAddress => ApiBaseAddress ?? new Uri(
        Environment == EFacturaEnvironment.Production
            ? "https://api.anaf.ro/prod/FCTEL/rest"
            : "https://api.anaf.ro/test/FCTEL/rest");

    /// <summary>
    /// The OAuth base address actually in use. ANAF has no separate test identity provider, so
    /// both environments authenticate against the same host.
    /// </summary>
    public Uri ResolvedOAuthBaseAddress => OAuthBaseAddress ?? new Uri("https://logincert.anaf.ro/anaf-oauth2/v1");

    /// <summary>
    /// The address of the unauthenticated validation and rendering endpoints.
    /// </summary>
    /// <remarks>
    /// These live on a different host from the rest of the API. Calling the <c>api.anaf.ro</c>
    /// variants without a bearer token returns 401 — the specific defect that made the previous
    /// version's upload path unreachable, since it validated before every upload.
    /// </remarks>
    public Uri ResolvedPublicToolsBaseAddress => ApiBaseAddress ?? new Uri("https://webservicesp.anaf.ro/prod/FCTEL/rest");
}
