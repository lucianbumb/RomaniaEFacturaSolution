using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RomaniaEFactura.Authentication;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.Persistence;
using RomaniaEFactura.Reconciliation;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura;

/// <summary>Registers e-Factura with an application.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds everything needed to send and receive e-Factura documents.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="configure">Sets the ANAF credentials and the company.</param>
    /// <param name="configureDatabase">
    /// Configures where the library keeps its own data — authorizations, tracked submissions and
    /// the inbox record. Omit it to use an in-memory token store with no persistence, which is
    /// only appropriate for a spike: an ANAF authorization costs a person with a qualified
    /// certificate to obtain, so losing it on restart is not a production behaviour.
    /// </param>
    public static IHostApplicationBuilder AddRomaniaEFactura(
        this IHostApplicationBuilder builder,
        Action<EFacturaOptions> configure,
        Action<DbContextOptionsBuilder>? configureDatabase = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.Configure(configure);
        AddCore(builder.Services, configureDatabase);
        return builder;
    }

    /// <summary>
    /// Adds e-Factura, binding its options from configuration.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="sectionName">The configuration section to bind.</param>
    /// <param name="configureDatabase">Configures the library's own storage.</param>
    public static IHostApplicationBuilder AddRomaniaEFactura(
        this IHostApplicationBuilder builder,
        string sectionName = EFacturaOptions.SectionName,
        Action<DbContextOptionsBuilder>? configureDatabase = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Configure<EFacturaOptions>(builder.Configuration.GetSection(sectionName));
        AddCore(builder.Services, configureDatabase);
        return builder;
    }

    private static void AddCore(IServiceCollection services, Action<DbContextOptionsBuilder>? configureDatabase)
    {
        // Registered rather than taken statically so a host - or a test - can substitute a clock.
        services.TryAddSingleton(TimeProvider.System);

        services.AddDataProtection();
        services.AddHttpClient(AnafApiClient.HttpClientName);

        if (configureDatabase is not null)
        {
            services.AddDbContext<EFacturaDbContext>(configureDatabase);
            services.AddScoped<IEFacturaTokenStore, EfCoreTokenStore>();
        }
        else
        {
            // No database configured. The token store keeps authorizations in memory, which is
            // enough to try the library out but loses them on restart; everything that needs the
            // context - submissions, the inbox, the reconciler - is unavailable.
            services.AddSingleton<IEFacturaTokenStore, InMemoryTokenStore>();
        }

        services.AddSingleton<OAuthStateProtector>();
        services.AddScoped<IAnafOAuthClient, AnafOAuthClient>();
        services.AddScoped<IAnafAccessTokenProvider, StoredTokenAccessTokenProvider>();
        services.AddScoped<IAnafApiClient, AnafApiClient>();

        if (configureDatabase is not null)
        {
            services.AddScoped<IRomaniaEFacturaService, RomaniaEFacturaService>();
            services.AddSingleton<EFacturaReconciler>();
            services.AddHostedService<ReconcilerHostedService>();
        }
    }
}
