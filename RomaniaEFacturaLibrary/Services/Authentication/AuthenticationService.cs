using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFacturaLibrary.Configuration;
using RomaniaEFacturaLibrary.Models.Authentication;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

namespace RomaniaEFacturaLibrary.Services.Authentication;

/// <summary>
/// OAuth2 authentication flow state
/// </summary>
public class OAuth2State
{
    public string State { get; set; } = Guid.NewGuid().ToString();
    public string? CodeVerifier { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Service for handling OAuth2 authentication with ANAF using digital certificates
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Gets the authorization URL for redirecting the user to ANAF login
    /// </summary>
    string GetAuthorizationUrl(string redirectUri, string? state = null);
    
    /// <summary>
    /// Exchanges the authorization code for access tokens
    /// </summary>
    Task<TokenResponse> ExchangeCodeForTokenAsync(string code, string redirectUri, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a valid access token, refreshing if necessary
    /// </summary>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Refreshes an existing token
    /// </summary>
    Task<TokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sets the current token (used after code exchange)
    /// </summary>
    void SetToken(TokenResponse token);
}

public class AuthenticationService : IAuthenticationService
{
    private readonly HttpClient _httpClient;
    private readonly EFacturaConfig _config;
    private readonly ILogger<AuthenticationService> _logger;
    private TokenResponse? _currentToken;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);

    public AuthenticationService(
        HttpClient httpClient,
        IOptions<EFacturaConfig> config,
        ILogger<AuthenticationService> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
    }

    public string GetAuthorizationUrl(string redirectUri, string? state = null)
    {
        state ??= Guid.NewGuid().ToString();
        
        var queryParams = new Dictionary<string, string>
        {
            {"response_type", "code"},
            {"client_id", _config.Cif},
            {"redirect_uri", redirectUri},
            {"state", state},
            {"scope", "eFACTURA"}
        };

        var queryString = string.Join("&", queryParams.Select(kvp => 
            $"{HttpUtility.UrlEncode(kvp.Key)}={HttpUtility.UrlEncode(kvp.Value)}"));

        var authUrl = $"{_config.AuthorizeUrl}?{queryString}";
        
        _logger.LogInformation("Generated authorization URL for CIF {Cif}", _config.Cif);
        _logger.LogDebug("Authorization URL: {AuthUrl}", authUrl);
        
        return authUrl;
    }

    public async Task<TokenResponse> ExchangeCodeForTokenAsync(string code, string redirectUri, CancellationToken cancellationToken = default)
    {
        var tokenRequest = new Dictionary<string, string>
        {
            {"grant_type", "authorization_code"},
            {"client_id", _config.Cif},
            {"code", code},
            {"redirect_uri", redirectUri}
        };

        var content = new FormUrlEncodedContent(tokenRequest);
        
        _logger.LogInformation("Exchanging authorization code for access token");
        _logger.LogDebug("Token endpoint: {TokenUrl}", _config.TokenUrl);
        
        var response = await _httpClient.PostAsync(_config.TokenUrl, content, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Token exchange failed with status {StatusCode}: {Response}", 
                response.StatusCode, responseContent);
            
            var error = JsonSerializer.Deserialize<TokenErrorResponse>(responseContent);
            throw new AuthenticationException($"Token exchange failed: {error?.Error} - {error?.ErrorDescription}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent);
        if (tokenResponse == null)
        {
            throw new AuthenticationException("Invalid token response format");
        }

        _currentToken = tokenResponse;
        
        _logger.LogInformation("Token exchange successful, expires in {ExpiresIn} seconds", tokenResponse.ExpiresIn);
        
        return tokenResponse;
    }

    public void SetToken(TokenResponse token)
    {
        _currentToken = token;
        _logger.LogInformation("Token set manually, expires in {ExpiresIn} seconds", token.ExpiresIn);
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _tokenSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Check if we have a valid token
            if (_currentToken?.IsValid == true)
            {
                _logger.LogDebug("Using existing valid access token");
                return _currentToken.AccessToken;
            }

            // Try to refresh if we have a refresh token
            if (_currentToken?.RefreshToken != null)
            {
                try
                {
                    _logger.LogInformation("Refreshing expired access token");
                    _currentToken = await RefreshTokenAsync(_currentToken.RefreshToken, cancellationToken);
                    return _currentToken.AccessToken;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh token");
                    _currentToken = null; // Clear invalid token
                }
            }

            // No valid token available - authentication required
            throw new AuthenticationException(
                "No valid access token available. Please authenticate by redirecting to the authorization URL.");
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    public async Task<TokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var refreshRequest = new Dictionary<string, string>
        {
            {"grant_type", "refresh_token"},
            {"client_id", _config.Cif},
            {"refresh_token", refreshToken}
        };

        var content = new FormUrlEncodedContent(refreshRequest);
        
        _logger.LogInformation("Refreshing access token");
        
        var response = await _httpClient.PostAsync(_config.TokenUrl, content, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Token refresh failed with status {StatusCode}: {Response}", 
                response.StatusCode, responseContent);
            
            var error = JsonSerializer.Deserialize<TokenErrorResponse>(responseContent);
            throw new AuthenticationException($"Token refresh failed: {error?.Error} - {error?.ErrorDescription}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent);
        if (tokenResponse == null)
        {
            throw new AuthenticationException("Invalid refresh response format");
        }

        _logger.LogInformation("Token refresh successful, expires in {ExpiresIn} seconds", tokenResponse.ExpiresIn);
        
        return tokenResponse;
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
