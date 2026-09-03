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

    /// <summary>
    /// Overrides the address of ANAF's public taxpayer register.
    /// </summary>
    /// <remarks>
    /// A different service on a different host from the e-Factura API, and unauthenticated, so it
    /// has its own address rather than sharing one.
    /// </remarks>
    public Uri? CompanyLookupBaseAddress { get; set; }

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

    /// <summary>
    /// Origins the post-callback redirect may return to, besides paths within this application.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty by default, and a local path is always allowed, so an application whose user interface
    /// is served by the same host needs none of this.
    /// </para>
    /// <para>
    /// Name an origin here when the interface is somewhere else — a separate SPA or PWA. Each entry
    /// is an absolute origin, scheme and host and port; a path on an entry is ignored, and matching
    /// is on the parsed components rather than on the text, so
    /// <c>https://app.example.ro.evil.test</c> does not match <c>https://app.example.ro</c>.
    /// </para>
    /// </remarks>
    public IList<string> AllowedReturnOrigins { get; set; } = [];

    /// <summary>Whether the background reconciler runs.</summary>
    /// <remarks>
    /// Turning it off leaves submissions permanently unresolved unless something else calls the
    /// reconciler, so it is only appropriate when another process owns reconciliation.
    /// </remarks>
    public bool EnableReconciler { get; set; } = true;

    /// <summary>
    /// How often the reconciler looks for work.
    /// </summary>
    /// <remarks>
    /// This is not how often ANAF is called. Each submission has its own widening schedule, so a
    /// short interval here only means a document that has become due is picked up sooner.
    /// </remarks>
    public TimeSpan ReconcileInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Whether the background sweep reads every authorized company's inbox.
    /// </summary>
    /// <remarks>
    /// <b>Off by default</b>, unlike the reconciler. The reconciler only calls ANAF about documents
    /// the application itself submitted; this polls on its own initiative, against an allowance
    /// that belongs to each company, so it is something to turn on deliberately rather than
    /// something an upgrade should start doing.
    /// </remarks>
    public bool EnableInboxSync { get; set; }

    /// <summary>
    /// How long between reads of one company's inbox.
    /// </summary>
    /// <remarks>
    /// Per company, not for the sweep as a whole: with a hundred companies a shared interval would
    /// mean a hundred calls on every tick. After a failure the interval widens, up to a day.
    /// </remarks>
    public TimeSpan InboxSyncInterval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How many submissions one reconciliation pass will handle.</summary>
    public int ReconcileBatchSize { get; set; } = 25;

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

    /// <summary>
    /// The address of ANAF's public taxpayer register actually in use.
    /// </summary>
    /// <remarks>
    /// There is one register, not a test copy of it, so this does not vary by environment. It
    /// falls back to <see cref="ApiBaseAddress"/> when that is overridden, which is what points
    /// the lookup at the mock server alongside everything else.
    /// </remarks>
    public Uri ResolvedCompanyLookupBaseAddress =>
        CompanyLookupBaseAddress ?? ApiBaseAddress ?? new Uri("https://webservicesp.anaf.ro/api/PlatitorTvaRest/v9");
}
