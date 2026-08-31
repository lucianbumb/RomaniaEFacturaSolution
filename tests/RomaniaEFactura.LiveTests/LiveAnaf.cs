using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RomaniaEFactura;
using RomaniaEFactura.Authentication;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.Persistence;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura.LiveTests;

/// <summary>
/// Configuration for the run against ANAF's real test environment.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is read from the environment rather than from a file, so no credential is ever
/// committed, and the whole suite is inert unless someone deliberately turns it on. Nothing in CI
/// sets these variables.
/// </para>
/// <para>
/// The authorization itself is not performed here and cannot be: ANAF requires a person presenting
/// a qualified digital certificate in a browser. The sample web app does that, and stores the
/// resulting token in its own database; this suite reuses that database rather than trying to
/// obtain a token of its own. See <c>docs/live-run.md</c>.
/// </para>
/// </remarks>
public static class LiveAnaf
{
    /// <summary>Set to <c>1</c> to allow this suite to run at all.</summary>
    public const string EnabledVariable = "EFACTURA_LIVE";

    /// <summary>Path to the SQLite database holding the authorization.</summary>
    public const string DatabaseVariable = "EFACTURA_LIVE_DB";

    /// <summary>The company being tested.</summary>
    public const string CifVariable = "EFACTURA_LIVE_CIF";

    /// <summary>The registered application's client id.</summary>
    public const string ClientIdVariable = "EFACTURA_LIVE_CLIENT_ID";

    /// <summary>The registered application's client secret.</summary>
    public const string ClientSecretVariable = "EFACTURA_LIVE_CLIENT_SECRET";

    /// <summary>
    /// Whether this looks like an automated environment, where a live run must never happen.
    /// </summary>
    /// <remarks>
    /// A live run spends real daily allowance and files real documents in ANAF's test register. It
    /// is a deliberate act by a person, never a side effect of a build — so it is refused wherever
    /// <c>CI</c> is set, which GitHub Actions and every other runner does. Without this, one
    /// misplaced repository secret would have every push sending invoices.
    /// </remarks>
    public static bool IsAutomatedEnvironment =>
        string.Equals(Get("CI"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether enough is configured to talk to ANAF.</summary>
    public static bool IsConfigured =>
        !IsAutomatedEnvironment
        && Get(EnabledVariable) == "1"
        && Get(DatabaseVariable) is { Length: > 0 } database
        && File.Exists(database)
        && Get(CifVariable) is { Length: > 0 }
        && Get(ClientIdVariable) is { Length: > 0 }
        && Get(ClientSecretVariable) is { Length: > 0 };

    /// <summary>The company under test.</summary>
    public static string Cif => Get(CifVariable) ?? string.Empty;

    /// <summary>Why the suite is not running, phrased so it can be acted on.</summary>
    public static string SkipReason => IsAutomatedEnvironment
        ? "Live ANAF runs are refused in automated environments: they spend real daily allowance "
          + "and file real documents. Run this from a developer machine."
        : $"Not configured for a live run. Set {EnabledVariable}=1 plus {DatabaseVariable}, "
        + $"{CifVariable}, {ClientIdVariable} and {ClientSecretVariable}. See docs/live-run.md — "
        + "authorization needs a person with a qualified certificate and cannot be automated.";

    /// <summary>
    /// Builds a provider wired to the real service, using the stored authorization.
    /// </summary>
    /// <remarks>
    /// Deliberately does not register the reconciler: a hosted service polling in the background
    /// during a live run would spend the daily allowance the tests are trying to measure.
    /// </remarks>
    public static ServiceProvider BuildServices(ILoggerProvider? logging = null)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            if (logging is not null) builder.AddProvider(logging);
        });

        services.AddDataProtection();
        services.AddHttpClient(AnafApiClient.HttpClientName);
        services.AddSingleton(TimeProvider.System);

        services.Configure<EFacturaOptions>(options =>
        {
            // Test rather than Production. Nothing in this suite should ever reach the live
            // register — a document sent there is a real fiscal document.
            options.Environment = EFacturaEnvironment.Test;
            options.Cif = Cif;
            options.ClientId = Get(ClientIdVariable)!;
            options.ClientSecret = Get(ClientSecretVariable)!;
            options.EnableReconciler = false;
        });

        services.AddDbContext<EFacturaDbContext>(options =>
            options.UseSqlite($"Data Source={Get(DatabaseVariable)}"));

        services.AddScoped<IEFacturaTokenStore, EfCoreTokenStore>();
        services.AddSingleton<OAuthStateProtector>();
        services.AddScoped<IAnafOAuthClient, AnafOAuthClient>();
        services.AddScoped<IAnafAccessTokenProvider, StoredTokenAccessTokenProvider>();
        services.AddScoped<IAnafApiClient, AnafApiClient>();
        services.AddScoped<IRomaniaEFacturaService, RomaniaEFacturaService>();

        return services.BuildServiceProvider();
    }

    private static string? Get(string name) => Environment.GetEnvironmentVariable(name);
}

/// <summary>A fact that only runs when a live ANAF run has been configured.</summary>
/// <remarks>
/// Skipping rather than failing, so the suite is harmless in CI and on a machine without
/// credentials. Unlike the validator oracle, this one is <em>not</em> enforced anywhere: a live
/// run costs real daily allowance and must stay a deliberate act.
/// </remarks>
public sealed class LiveAnafFactAttribute : FactAttribute
{
    /// <summary>Marks the test skipped unless a live run is configured.</summary>
    public LiveAnafFactAttribute()
    {
        if (!LiveAnaf.IsConfigured) Skip = LiveAnaf.SkipReason;
    }
}

/// <summary>
/// A fact that only runs when the quota experiment has been explicitly allowed.
/// </summary>
/// <remarks>
/// Separate from <see cref="LiveAnafFactAttribute"/> because the probe deliberately spends a
/// day's status allowance on one document to find out what the allowance is counted against.
/// Nobody should trigger that by configuring a live run.
/// </remarks>
public sealed class LiveAnafQuotaProbeFactAttribute : FactAttribute
{
    /// <summary>Set to <c>1</c> alongside the live variables to allow the quota experiment.</summary>
    public const string EnabledVariable = "EFACTURA_LIVE_QUOTA_PROBE";

    /// <summary>Marks the test skipped unless the probe is explicitly allowed.</summary>
    public LiveAnafQuotaProbeFactAttribute()
    {
        if (!LiveAnaf.IsConfigured)
        {
            Skip = LiveAnaf.SkipReason;
        }
        else if (Environment.GetEnvironmentVariable(EnabledVariable) != "1")
        {
            Skip = $"The quota probe spends a day's status allowance on one document. "
                 + $"Set {EnabledVariable}=1 to run it.";
        }
    }
}
