using RomaniaEFacturaLibrary.Services.Authentication;

namespace RomaniaEFacturaLibrary.Configuration;

/// <summary>
/// EFactura environment configuration
/// </summary>
public enum EFacturaEnvironment
{
    /// <summary>
    /// Test environment for development
    /// </summary>
    Test,
    
    /// <summary>
    /// Production environment
    /// </summary>
    Production
}

/// <summary>
/// Configuration for EFactura client
/// </summary>
public class EFacturaConfig
{
    /// <summary>
    /// The environment to use (Test or Production)
    /// </summary>
    public EFacturaEnvironment Environment { get; set; } = EFacturaEnvironment.Test;
    
    /// <summary>
    /// Company fiscal identification code (CIF) - used as client_id in OAuth2
    /// </summary>
    public string Cif { get; set; } = string.Empty;
    
    /// <summary>
    /// Base URL for ANAF API (will be set based on Environment)
    /// </summary>
    public string BaseUrl => Environment == EFacturaEnvironment.Test 
        ? "https://api.anaf.ro/test/FCTEL/rest" 
        : "https://api.anaf.ro/prod/FCTEL/rest";
    
    /// <summary>
    /// OAuth Authorization URL for redirecting users to ANAF login
    /// </summary>
    public string AuthorizeUrl => "https://logincert.anaf.ro/anaf-oauth2/v1/authorize";
    
    /// <summary>
    /// Token URL for exchanging authorization codes and refreshing tokens
    /// </summary>
    public string TokenUrl => "https://logincert.anaf.ro/anaf-oauth2/v1/token";
        
    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// Enable detailed logging
    /// </summary>
    public bool EnableLogging { get; set; } = true;
}
