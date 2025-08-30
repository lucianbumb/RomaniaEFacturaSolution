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
    /// Path to the digital certificate file (.pfx)
    /// </summary>
    public string CertificatePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Password for the digital certificate
    /// </summary>
    public string CertificatePassword { get; set; } = string.Empty;
    
    /// <summary>
    /// Company fiscal identification code (CIF)
    /// </summary>
    public string Cif { get; set; } = string.Empty;
    
    /// <summary>
    /// Base URL for ANAF API (will be set based on Environment)
    /// </summary>
    public string BaseUrl => Environment == EFacturaEnvironment.Test 
        ? "https://api.anaf.ro/test/FCTEL/rest" 
        : "https://api.anaf.ro/prod/FCTEL/rest";
    
    /// <summary>
    /// OAuth URL for authentication
    /// </summary>
    public string OAuthUrl => Environment == EFacturaEnvironment.Test 
        ? "https://logincert.anaf.ro/anaf-oauth2/v1/authorize" 
        : "https://logincert.anaf.ro/anaf-oauth2/v1/authorize";
    
    /// <summary>
    /// Token URL for getting access tokens
    /// </summary>
    public string TokenUrl => Environment == EFacturaEnvironment.Test 
        ? "https://logincert.anaf.ro/anaf-oauth2/v1/token" 
        : "https://logincert.anaf.ro/anaf-oauth2/v1/token";
        
    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// Enable detailed logging
    /// </summary>
    public bool EnableLogging { get; set; } = true;
}
