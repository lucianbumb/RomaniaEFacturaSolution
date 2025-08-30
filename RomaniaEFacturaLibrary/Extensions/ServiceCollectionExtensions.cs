using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFacturaLibrary.Configuration;
using RomaniaEFacturaLibrary.Services.Api;
using RomaniaEFacturaLibrary.Services.Authentication;
using RomaniaEFacturaLibrary.Services.Xml;
using RomaniaEFacturaLibrary.Services;

namespace RomaniaEFacturaLibrary.Extensions;

/// <summary>
/// Extension methods for service collection configuration
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds EFactura services to the dependency injection container
    /// </summary>
    public static IServiceCollection AddEFacturaServices(
        this IServiceCollection services, 
        Action<EFacturaConfig> configureOptions)
    {
        // Configure options
        services.Configure(configureOptions);
        
        // Add HTTP client
        services.AddHttpClient<IEFacturaApiClient, EFacturaApiClient>();
        services.AddHttpClient<IAuthenticationService, AuthenticationService>();
        
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
    /// Adds EFactura services with configuration from IConfiguration
    /// </summary>
    public static IServiceCollection AddEFacturaServices(
        this IServiceCollection services, 
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        string sectionName = "EFactura")
    {
        services.Configure<EFacturaConfig>(configuration.GetSection(sectionName));
        
        return services.AddEFacturaServices(_ => { });
    }
}
