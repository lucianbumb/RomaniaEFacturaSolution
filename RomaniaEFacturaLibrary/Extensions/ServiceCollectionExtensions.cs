using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFacturaLibrary.Configuration;
using RomaniaEFacturaLibrary.Services.Api;
using RomaniaEFacturaLibrary.Services.Authentication;
using RomaniaEFacturaLibrary.Services.Xml;
using RomaniaEFacturaLibrary.Services;
using RomaniaEFacturaLibrary.Services.TokenStorage;

namespace RomaniaEFacturaLibrary.Extensions;

/// <summary>
/// Extension methods for service collection configuration
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds EFactura services to the dependency injection container with MemoryCache token storage
    /// </summary>
    public static IServiceCollection AddEFacturaServices(
        this IServiceCollection services,
        Action<EFacturaConfig> configureOptions)
    {
        return services.AddEFacturaServicesWithMemoryCache(configureOptions);
    }

    /// <summary>
    /// Adds EFactura services with MemoryCache token storage
    /// </summary>
    public static IServiceCollection AddEFacturaServicesWithMemoryCache(
        this IServiceCollection services,
        Action<EFacturaConfig> configureOptions)
    {
        // Configure options
        services.Configure(configureOptions);

        // Add memory cache for token storage
        services.AddMemoryCache();

        // Add HTTP client factory
        services.AddHttpClient();

        // Add token storage service
        services.AddScoped<ITokenStorageService, MemoryCacheTokenStorageService>();

        // Add services
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IXmlService, XmlService>();
        services.AddScoped<IEFacturaApiClient, EFacturaApiClient>();
        services.AddScoped<IEFacturaClient, EFacturaClient>();

        // Add logging if not already configured
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
        });

        return services;
    }

    /// <summary>
    /// Adds EFactura services with Cookie token storage
    /// </summary>
    public static IServiceCollection AddEFacturaServicesWithCookieStorage(
        this IServiceCollection services,
        Action<EFacturaConfig> configureOptions)
    {
        // Configure options
        services.Configure(configureOptions);

        // Add HTTP client factory
        services.AddHttpClient();

        // Add token storage service
        services.AddScoped<ITokenStorageService, CookieTokenStorageService>();

        // Add services
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IXmlService, XmlService>();
        services.AddScoped<IEFacturaApiClient, EFacturaApiClient>();
        services.AddScoped<IEFacturaClient, EFacturaClient>();

        // Add logging if not already configured
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
        });

        return services;
    }

    /// <summary>
    /// Adds EFactura services with custom token storage
    /// </summary>
    public static IServiceCollection AddEFacturaServicesWithCustomStorage<TTokenStorage>(
        this IServiceCollection services,
        Action<EFacturaConfig> configureOptions)
        where TTokenStorage : class, ITokenStorageService
    {
        // Configure options
        services.Configure(configureOptions);

        // Add HTTP client factory
        services.AddHttpClient();

        // Add custom token storage service
        services.AddScoped<ITokenStorageService, TTokenStorage>();

        // Add services
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IXmlService, XmlService>();
        services.AddScoped<IEFacturaApiClient, EFacturaApiClient>();
        services.AddScoped<IEFacturaClient, EFacturaClient>();

        // Add logging if not already configured
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
        });

        return services;
    }

    /// <summary>
    /// Adds EFactura services with configuration from IConfiguration using MemoryCache storage
    /// </summary>
    public static IServiceCollection AddEFacturaServices(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        string sectionName = "EFactura")
    {
        services.Configure<EFacturaConfig>(configuration.GetSection(sectionName));
        return services.AddEFacturaServicesWithMemoryCache(_ => { });
    }

    /// <summary>
    /// Adds EFactura services with configuration from IConfiguration using Cookie storage
    /// </summary>
    public static IServiceCollection AddEFacturaServicesWithCookieStorage(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        string sectionName = "EFactura")
    {
        services.Configure<EFacturaConfig>(configuration.GetSection(sectionName));
        return services.AddEFacturaServicesWithCookieStorage(_ => { });
    }
}
