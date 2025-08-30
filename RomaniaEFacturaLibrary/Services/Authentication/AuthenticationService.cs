using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFacturaLibrary.Configuration;
using RomaniaEFacturaLibrary.Models.Authentication;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RomaniaEFacturaLibrary.Services.Authentication;

/// <summary>
/// Certificate selection mode
/// </summary>
public enum CertificateSelectionMode
{
    /// <summary>
    /// Load from file path (original behavior)
    /// </summary>
    FilePath,
    
    /// <summary>
    /// Select from certificate store (allows USB certificates)
    /// </summary>
    CertificateStore,
    
    /// <summary>
    /// Automatic detection - prefer USB, fallback to file
    /// </summary>
    AutoDetect
}

/// <summary>
/// Service for handling OAuth2 authentication with ANAF using digital certificates
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Gets a valid access token, refreshing if necessary
    /// </summary>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Authenticates using the digital certificate and gets an initial token
    /// </summary>
    Task<TokenResponse> AuthenticateAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Refreshes an existing token
    /// </summary>
    Task<TokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets available certificates for the specified CIF
    /// </summary>
    List<X509Certificate2> GetAvailableCertificates(string? cif = null);
    
    /// <summary>
    /// Sets the certificate to use for authentication
    /// </summary>
    void SetCertificate(X509Certificate2 certificate);
}

public class AuthenticationService : IAuthenticationService
{
    private readonly HttpClient _httpClient;
    private readonly EFacturaConfig _config;
    private readonly ILogger<AuthenticationService> _logger;
    private TokenResponse? _currentToken;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private X509Certificate2? _selectedCertificate;

    public AuthenticationService(
        HttpClient httpClient,
        IOptions<EFacturaConfig> config,
        ILogger<AuthenticationService> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
        
        // Note: Certificate loading is deferred until actually needed in authentication methods
    }

    private void ConfigureHttpClient()
    {
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        
        // Load and configure the digital certificate
        if (!string.IsNullOrEmpty(_config.CertificatePath))
        {
            var cert = LoadCertificateSecure(_config.CertificatePath, _config.CertificatePassword);
            var handler = new HttpClientHandler();
            handler.ClientCertificates.Add(cert);
            
            _logger.LogInformation("Certificate loaded: {Subject}", cert.Subject);
        }
    }

    public List<X509Certificate2> GetAvailableCertificates(string? cif = null)
    {
        var certificates = new List<X509Certificate2>();
        
        try
        {
            // Search in Current User Personal store (includes USB certificates)
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            
            foreach (var cert in store.Certificates)
            {
                // Check if certificate has private key and is valid
                if (cert.HasPrivateKey && 
                    cert.NotBefore <= DateTime.Now && 
                    cert.NotAfter >= DateTime.Now)
                {
                    // If CIF is provided, filter by certificate subject or issuer
                    if (string.IsNullOrEmpty(cif) || 
                        cert.Subject.Contains(cif) || 
                        cert.GetNameInfo(X509NameType.SimpleName, false).Contains(cif))
                    {
                        certificates.Add(cert);
                    }
                }
            }
            
            _logger.LogInformation("Found {Count} valid certificates", certificates.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accessing certificate store");
        }
        
        return certificates;
    }
    
    public void SetCertificate(X509Certificate2 certificate)
    {
        _selectedCertificate = certificate;
        _currentToken = null; // Reset token when certificate changes
        _logger.LogInformation("Certificate set: {Subject}", certificate.Subject);
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _tokenSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Check if we have a valid token
            if (_currentToken?.IsValid == true)
            {
                _logger.LogDebug("Using existing valid token");
                return _currentToken.AccessToken;
            }

            // Try to refresh if we have a refresh token
            if (_currentToken?.RefreshToken != null)
            {
                try
                {
                    _logger.LogInformation("Refreshing expired token");
                    _currentToken = await RefreshTokenAsync(_currentToken.RefreshToken, cancellationToken);
                    return _currentToken.AccessToken;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh token, will authenticate from scratch");
                }
            }

            // Authenticate from scratch
            _logger.LogInformation("Authenticating with certificate");
            _currentToken = await AuthenticateAsync(cancellationToken);
            return _currentToken.AccessToken;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    public async Task<TokenResponse> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        var certificate = LoadCertificate();
        
        // Create the authentication request
        var authRequest = CreateAuthenticationRequest(certificate);
        
        var content = new FormUrlEncodedContent(authRequest);
        
        _logger.LogInformation("Sending authentication request to {Url}", _config.TokenUrl);
        
        var response = await _httpClient.PostAsync(_config.TokenUrl, content, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = JsonSerializer.Deserialize<TokenErrorResponse>(responseContent);
            throw new AuthenticationException($"Authentication failed: {error?.Error} - {error?.ErrorDescription}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent);
        if (tokenResponse == null)
        {
            throw new AuthenticationException("Invalid token response format");
        }

        _logger.LogInformation("Authentication successful, token expires in {ExpiresIn} seconds", tokenResponse.ExpiresIn);
        
        return tokenResponse;
    }

    public async Task<TokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var refreshRequest = new Dictionary<string, string>
        {
            {"grant_type", "refresh_token"},
            {"refresh_token", refreshToken}
        };

        var content = new FormUrlEncodedContent(refreshRequest);
        
        _logger.LogInformation("Sending token refresh request");
        
        var response = await _httpClient.PostAsync(_config.TokenUrl, content, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = JsonSerializer.Deserialize<TokenErrorResponse>(responseContent);
            throw new AuthenticationException($"Token refresh failed: {error?.Error} - {error?.ErrorDescription}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent);
        if (tokenResponse == null)
        {
            throw new AuthenticationException("Invalid refresh response format");
        }

        _logger.LogInformation("Token refresh successful");
        
        return tokenResponse;
    }

    private X509Certificate2 LoadCertificate()
    {
        // Priority 1: Use explicitly set certificate
        if (_selectedCertificate != null)
        {
            _logger.LogInformation("Using explicitly set certificate");
            return _selectedCertificate;
        }
        
        // Priority 2: Auto-detect USB certificates
        var availableCertificates = GetAvailableCertificates(_config.Cif);
        if (availableCertificates.Count == 1)
        {
            _logger.LogInformation("Auto-selected single available certificate");
            return availableCertificates[0];
        }
        else if (availableCertificates.Count > 1)
        {
            // Multiple certificates found - in a real scenario, you'd want to prompt user
            _logger.LogWarning("Multiple certificates found. Using first one. Consider implementing certificate selection UI.");
            return availableCertificates[0];
        }
        
        // Priority 3: Fallback to file path if configured
        if (!string.IsNullOrEmpty(_config.CertificatePath))
        {
            if (!File.Exists(_config.CertificatePath))
            {
                throw new FileNotFoundException($"Certificate file not found: {_config.CertificatePath}");
            }
            
            _logger.LogInformation("Using certificate from file path");
            return LoadCertificateSecure(_config.CertificatePath, _config.CertificatePassword);
        }
        
        throw new InvalidOperationException("No certificate available. Please insert USB certificate or configure certificate path.");
    }

    private static X509Certificate2 LoadCertificateSecure(string certificatePath, string? certificatePassword)
    {
        // Suppress the obsolete warning for X509Certificate2 constructor
        // The recommended X509CertificateLoader is not available in all target frameworks
#pragma warning disable SYSLIB0057
        return new X509Certificate2(certificatePath, certificatePassword);
#pragma warning restore SYSLIB0057
    }

    private Dictionary<string, string> CreateAuthenticationRequest(X509Certificate2 certificate)
    {
        // Create the authentication payload specific to ANAF requirements
        var nonce = Guid.NewGuid().ToString();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        
        // Sign the request with the certificate
        var dataToSign = $"{_config.Cif}:{nonce}:{timestamp}";
        var signature = SignData(dataToSign, certificate);
        
        return new Dictionary<string, string>
        {
            {"grant_type", "client_credentials"},
            {"client_id", _config.Cif},
            {"scope", "eFACTURA"},
            {"nonce", nonce},
            {"timestamp", timestamp},
            {"signature", signature}
        };
    }

    private string SignData(string data, X509Certificate2 certificate)
    {
        using var rsa = certificate.GetRSAPrivateKey();
        if (rsa == null)
        {
            throw new InvalidOperationException("Certificate does not contain an RSA private key");
        }

        var dataBytes = Encoding.UTF8.GetBytes(data);
        var signatureBytes = rsa.SignData(dataBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        
        return Convert.ToBase64String(signatureBytes);
    }
}

/// <summary>
/// Exception thrown when authentication fails
/// </summary>
public class AuthenticationException : Exception
{
    public AuthenticationException(string message) : base(message) { }
    public AuthenticationException(string message, Exception innerException) : base(message, innerException) { }
}
