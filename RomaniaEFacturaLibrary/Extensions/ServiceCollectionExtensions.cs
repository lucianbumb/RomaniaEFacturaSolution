using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFacturaLibrary.Configuration;
using RomaniaEFacturaLibrary.Services.Api;
using RomaniaEFacturaLibrary.Services.Authentication;
using RomaniaEFacturaLibrary.Services.Xml;
using RomaniaEFacturaLibrary.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;

namespace RomaniaEFacturaLibrary.Extensions;

/// <summary>
/// Extension methods for service collection configuration
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds EFactura services to the dependency injection container with MemoryCache token storage (default)
    /// </summary>
    public static IServiceCollection AddEFacturaServices(
        this IServiceCollection services,
        Action<EFacturaConfig> configureOptions)
    {
        // Configure options
        services.Configure(configureOptions);

        // Add required dependencies
        services.AddMemoryCache();
        services.AddDistributedMemoryCache(); // Add for session support
        services.AddHttpClient();
        services.AddHttpContextAccessor();

        // Add token storage (default: MemoryCache)
        services.AddScoped<ITokenStorageService, MemoryCacheTokenStorageService>();

        // Add services in correct order (dependencies first)
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IXmlService, XmlService>();
        services.AddScoped<IEFacturaApiClient, EFacturaApiClient>();
        services.AddScoped<IEFacturaClient, EFacturaClient>();

        return services;
    }

    /// <summary>
    /// Adds EFactura services with configuration from IConfiguration using MemoryCache token storage
    /// </summary>
    public static IServiceCollection AddEFacturaServices(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        string sectionName = "EFactura")
    {
        services.Configure<EFacturaConfig>(configuration.GetSection(sectionName));

        // Add required dependencies
        services.AddMemoryCache();
        services.AddDistributedMemoryCache(); // Add for session support
        services.AddHttpClient();
        services.AddHttpContextAccessor();

        // Add token storage (default: MemoryCache)
        services.AddScoped<ITokenStorageService, MemoryCacheTokenStorageService>();

        // Add services in correct order (dependencies first)
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IXmlService, XmlService>();
        services.AddScoped<IEFacturaApiClient, EFacturaApiClient>();
        services.AddScoped<IEFacturaClient, EFacturaClient>();

        return services;
    }

    /// <summary>
    /// Adds EFactura services with sessions configured for OAuth2 state management
    /// </summary>
    public static IServiceCollection AddEFacturaServicesWithSessions(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        string sectionName = "EFactura")
    {
        // Add EFactura services first
        services.AddEFacturaServices(configuration, sectionName);

        // Add session support for OAuth2 state management
        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        return services;
    }

    /// <summary>
    /// Adds EFactura services with Cookie-based token storage
    /// </summary>
    public static IServiceCollection AddEFacturaServicesWithCookieStorage(
        this IServiceCollection services,
        Action<EFacturaConfig> configureOptions)
    {
        // Configure options
        services.Configure(configureOptions);

        // Add required dependencies
        services.AddDistributedMemoryCache(); // Add for session support
        services.AddHttpClient();
        services.AddHttpContextAccessor();

        // Add cookie-based token storage
        services.AddScoped<ITokenStorageService, CookieTokenStorageService>();

        // Add services in correct order (dependencies first)
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IXmlService, XmlService>();
        services.AddScoped<IEFacturaApiClient, EFacturaApiClient>();
        services.AddScoped<IEFacturaClient, EFacturaClient>();

        return services;
    }

    /// <summary>
    /// Adds EFactura services with Cookie-based token storage using IConfiguration
    /// </summary>
    public static IServiceCollection AddEFacturaServicesWithCookieStorage(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        string sectionName = "EFactura")
    {
        services.Configure<EFacturaConfig>(configuration.GetSection(sectionName));

        // Add required dependencies
        services.AddDistributedMemoryCache(); // Add for session support
        services.AddHttpClient();
        services.AddHttpContextAccessor();

        // Add cookie-based token storage
        services.AddScoped<ITokenStorageService, CookieTokenStorageService>();

        // Add services in correct order (dependencies first)
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IXmlService, XmlService>();
        services.AddScoped<IEFacturaApiClient, EFacturaApiClient>();
        services.AddScoped<IEFacturaClient, EFacturaClient>();

        return services;
    }

    /// <summary>
    /// Adds EFactura services with a custom token storage implementation
    /// </summary>
    public static IServiceCollection AddEFacturaServicesWithCustomStorage<TTokenStorage>(
        this IServiceCollection services,
        Action<EFacturaConfig> configureOptions)
        where TTokenStorage : class, ITokenStorageService
    {
        // Configure options
        services.Configure(configureOptions);

        // Add required dependencies
        services.AddDistributedMemoryCache(); // Add for session support
        services.AddHttpClient();
        services.AddHttpContextAccessor();

        // Add custom token storage
        services.AddScoped<ITokenStorageService, TTokenStorage>();

        // Add services in correct order (dependencies first)
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IXmlService, XmlService>();
        services.AddScoped<IEFacturaApiClient, EFacturaApiClient>();
        services.AddScoped<IEFacturaClient, EFacturaClient>();

        return services;
    }

    /// <summary>
    /// Adds EFactura services with a custom token storage implementation using IConfiguration
    /// </summary>
    public static IServiceCollection AddEFacturaServicesWithCustomStorage<TTokenStorage>(
        this IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        string sectionName = "EFactura")
        where TTokenStorage : class, ITokenStorageService
    {
        services.Configure<EFacturaConfig>(configuration.GetSection(sectionName));

        // Add required dependencies
        services.AddDistributedMemoryCache(); // Add for session support
        services.AddHttpClient();
        services.AddHttpContextAccessor();

        // Add custom token storage
        services.AddScoped<ITokenStorageService, TTokenStorage>();

        // Add services in correct order (dependencies first)
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IXmlService, XmlService>();
        services.AddScoped<IEFacturaApiClient, EFacturaApiClient>();
        services.AddScoped<IEFacturaClient, EFacturaClient>();

        return services;
    }
}
