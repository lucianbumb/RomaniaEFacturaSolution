using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFacturaLibrary.Configuration;
using RomaniaEFacturaLibrary.Models.Authentication;
using RomaniaEFacturaLibrary.Services.TokenStorage;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Web;

namespace RomaniaEFacturaLibrary.Services.Authentication;

/// <summary>
/// Service for handling OAuth2 authentication with ANAF
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Constructs the Authorization URL for redirecting the user to ANAF's Identity Provider (IdP) for authentication.
    /// </summary>
    string GetAuthorizationUrl(string clientId, string redirectUri, string? scope = null, string? state = null);
    
    /// <summary>
    /// Gets the authorization URL using configuration values
    /// </summary>
    string GetAuthorizationUrl(string? scope = null, string? state = null);
    
    /// <summary>
    /// Exchanges authorization code for access token
    /// </summary>
    Task<TokenDto> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Refreshes an expired access token
    /// </summary>
    Task<TokenDto> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a valid access token (backward compatibility)
    /// </summary>
    Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a valid access token using HttpContext for user identification
    /// </summary>
    Task<string> GetValidAccessTokenAsync(HttpContext httpContext, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sets the current token (in-memory only)
    /// </summary>
    void SetToken(TokenDto token);
    
    /// <summary>
    /// Sets token for a specific user
    /// </summary>
    Task SetTokenAsync(string userName, TokenDto token, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sets token using HttpContext to determine user
    /// </summary>
    Task SetTokenAsync(HttpContext httpContext, TokenDto token, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Removes token for a specific user
    /// </summary>
    Task RemoveTokenAsync(string userName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Removes token using HttpContext
    /// </summary>
    Task RemoveTokenAsync(HttpContext httpContext, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the current token (in-memory)
    /// </summary>
    TokenDto? GetCurrentToken();
    
    /// <summary>
    /// Gets the current token using HttpContext
    /// </summary>
    Task<TokenDto?> GetCurrentTokenAsync(HttpContext httpContext, CancellationToken cancellationToken = default);
}

public class AuthenticationService : IAuthenticationService
{
    private readonly HttpClient _httpClient;
    private readonly EFacturaConfig _config;
    private readonly ITokenStorageService? _tokenStorage;
    private readonly ILogger<AuthenticationService> _logger;
    private TokenDto? _currentToken;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);

    public AuthenticationService(
        HttpClient httpClient,
        IOptions<EFacturaConfig> config,
        ILogger<AuthenticationService> logger,
        ITokenStorageService? tokenStorage = null)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
        _tokenStorage = tokenStorage;
    }

    public string GetAuthorizationUrl(string clientId, string redirectUri, string? scope = null, string? state = null)
    {
        _logger.LogInformation("Constructing ANAF OAuth Authorization URL.");
        var uriBuilder = new UriBuilder(_config.AuthorizeUrl);
        var query = HttpUtility.ParseQueryString(uriBuilder.Query);

        query["response_type"] = "code";
        query["client_id"] = clientId;
        query["redirect_uri"] = redirectUri;
        query["token_content_type"] = "jwt";

        if (!string.IsNullOrEmpty(scope))
        {
            query["scope"] = scope;
        }
        if (!string.IsNullOrEmpty(state))
        {
            query["state"] = state;
        }

        uriBuilder.Query = query.ToString();
        _logger.LogDebug("Generated Authorization URL: {Url}", uriBuilder.ToString());
        return uriBuilder.ToString();
    }

    public string GetAuthorizationUrl(string? scope = null, string? state = null)
    {
        return GetAuthorizationUrl(_config.ClientId, _config.RedirectUri, scope, state);
    }

    public async Task<TokenDto> ExchangeCodeForTokenAsync(string code, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Exchanging authorization code for access token");

        var authenticationString = $"{_config.ClientId}:{_config.ClientSecret}";
        var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.ASCII.GetBytes(authenticationString));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);

        var content = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", _config.RedirectUri),
            new KeyValuePair<string, string>("token_content_type", "jwt")
        ]);

        var response = await _httpClient.PostAsync(_config.TokenUrl, content, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Token exchange failed with status {StatusCode}: {Content}", response.StatusCode, responseContent);
            throw new AuthenticationException($"Token exchange failed: {response.StatusCode}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenDto>(responseContent);
        if (tokenResponse == null)
        {
            throw new AuthenticationException("Invalid token response format");
        }

        _currentToken = tokenResponse;
        _logger.LogInformation("Token exchange successful. Token expires in {ExpiresIn} seconds", tokenResponse.ExpiresIn);

        return tokenResponse;
    }

    public async Task<TokenDto> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Refreshing access token");

        var authenticationString = $"{_config.ClientId}:{_config.ClientSecret}";
        var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.ASCII.GetBytes(authenticationString));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);

        var content = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken),
            new KeyValuePair<string, string>("token_content_type", "jwt")
        ]);

        var response = await _httpClient.PostAsync(_config.TokenUrl, content, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Token refresh failed with status {StatusCode}: {Content}", response.StatusCode, responseContent);
            throw new AuthenticationException($"Token refresh failed: {response.StatusCode}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenDto>(responseContent);
        if (tokenResponse == null)
        {
            throw new AuthenticationException("Invalid token response format");
        }

        _currentToken = tokenResponse;
        _logger.LogInformation("Token refresh successful. New token expires in {ExpiresIn} seconds", tokenResponse.ExpiresIn);

        return tokenResponse;
    }

    public async Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _tokenSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_currentToken?.IsValid == true)
            {
                return _currentToken.AccessToken;
            }

            if (_currentToken?.RefreshToken != null)
            {
                try
                {
                    var refreshedToken = await RefreshTokenAsync(_currentToken.RefreshToken, cancellationToken);
                    return refreshedToken.AccessToken;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh token");
                }
            }

            throw new AuthenticationException("No valid access token available. Please re-authenticate.");
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    public async Task<string> GetValidAccessTokenAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        await _tokenSemaphore.WaitAsync(cancellationToken);
        try
        {
            // Try to get token from storage first
            if (_tokenStorage != null)
            {
                var storedToken = await _tokenStorage.GetTokenAsync(httpContext, cancellationToken);
                if (storedToken?.IsValid == true)
                {
                    _currentToken = storedToken;
                    return storedToken.AccessToken;
                }

                // Try to refresh if we have a refresh token
                if (storedToken?.RefreshToken != null)
                {
                    try
                    {
                        var refreshedToken = await RefreshTokenAsync(storedToken.RefreshToken, cancellationToken);
                        await _tokenStorage.SetTokenAsync(httpContext, refreshedToken, cancellationToken);
                        return refreshedToken.AccessToken;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to refresh stored token");
                        await _tokenStorage.RemoveTokenAsync(httpContext, cancellationToken);
                    }
                }
            }

            // Fall back to in-memory token
            if (_currentToken?.IsValid == true)
            {
                return _currentToken.AccessToken;
            }

            if (_currentToken?.RefreshToken != null)
            {
                try
                {
                    var refreshedToken = await RefreshTokenAsync(_currentToken.RefreshToken, cancellationToken);
                    if (_tokenStorage != null)
                    {
                        await _tokenStorage.SetTokenAsync(httpContext, refreshedToken, cancellationToken);
                    }
                    return refreshedToken.AccessToken;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh token");
                }
            }

            throw new AuthenticationException("No valid access token available. Please re-authenticate.");
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    public void SetToken(TokenDto token)
    {
        _currentToken = token;
        _logger.LogDebug("Token set manually. Expires at: {ExpiresAt}", token.ExpiresAt);
    }

    public async Task SetTokenAsync(string userName, TokenDto token, CancellationToken cancellationToken = default)
    {
        _currentToken = token;
        
        if (_tokenStorage != null)
        {
            await _tokenStorage.SetTokenAsync(userName, token, cancellationToken);
        }
        
        _logger.LogDebug("Token set for user: {UserName}. Expires at: {ExpiresAt}", userName, token.ExpiresAt);
    }

    public async Task SetTokenAsync(HttpContext httpContext, TokenDto token, CancellationToken cancellationToken = default)
    {
        _currentToken = token;
        
        if (_tokenStorage != null)
        {
            await _tokenStorage.SetTokenAsync(httpContext, token, cancellationToken);
        }
        
        _logger.LogDebug("Token set via HttpContext. Expires at: {ExpiresAt}", token.ExpiresAt);
    }

    public async Task RemoveTokenAsync(string userName, CancellationToken cancellationToken = default)
    {
        _currentToken = null;
        
        if (_tokenStorage != null)
        {
            await _tokenStorage.RemoveTokenAsync(userName, cancellationToken);
        }
        
        _logger.LogDebug("Token removed for user: {UserName}", userName);
    }

    public async Task RemoveTokenAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        _currentToken = null;
        
        if (_tokenStorage != null)
        {
            await _tokenStorage.RemoveTokenAsync(httpContext, cancellationToken);
        }
        
        _logger.LogDebug("Token removed via HttpContext");
    }

    public TokenDto? GetCurrentToken()
    {
        return _currentToken;
    }

    public async Task<TokenDto?> GetCurrentTokenAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        if (_tokenStorage != null)
        {
            var storedToken = await _tokenStorage.GetTokenAsync(httpContext, cancellationToken);
            if (storedToken != null)
            {
                _currentToken = storedToken;
                return storedToken;
            }
        }
        
        return _currentToken;
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
