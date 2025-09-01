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
using Microsoft.AspNetCore.Http;

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
    /// Constructs the Authorization URL for redirecting the user to ANAF's Identity Provider (IdP) for authentication.
    /// This URL is used in a web application context where the user's browser is redirected to ANAF for consent.
    /// This method does not make an HTTP request itself.
    /// </summary>
    /// <param name="clientId">The Client ID obtained from ANAF application registration.</param>
    /// <param name="redirectUri">The Redirect URI registered with ANAF, where the authorization code will be sent.</param>
    /// <param name="scope">Optional: The scope of access being requested (e.g., "efactura").</param>
    /// <param name="state">Optional: A unique value to maintain state between the request and callback to prevent CSRF attacks.</param>
    /// <returns>The constructed authorization URL string.</returns>
    string GetAuthorizationUrl(string clientId, string redirectUri, string? scope = null, string? state = null);
    
    /// <summary>
    /// Gets the authorization URL using configuration values
    /// </summary>
    string GetAuthorizationUrl(string? scope = null, string? state = null);
    
    /// <summary>
    /// Gets an access token and refresh token from ANAF's token endpoint using the authorization code.
    /// This is the second step in the OAuth 2.0 Authorization Code Flow.
    /// </summary>
    /// <param name="code">The authorization code received from the IdP callback.</param>
    /// <param name="redirectUri">The Redirect URI used in the initial authorization request. Must match exactly.</param>
    /// <param name="clientId">The Client ID for your ANAF application.</param>
    /// <param name="clientSecret">The Client Secret for your ANAF application.</param>
    /// <returns>An TokenResponse object containing the tokens, or null on failure.</returns>
    Task<TokenResponse?> GetAccessTokenAsync(string code, string redirectUri, string clientId, string clientSecret);
    
    /// <summary>
    /// Gets access token using configuration values
    /// </summary>
    Task<TokenResponse?> ExchangeCodeForTokenAsync(string code, string? userName = null);
    
    /// <summary>
    /// Refreshes an expired access token using the refresh token obtained previously.
    /// This avoids requiring the user to re-authenticate with ANAF.
    /// </summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="clientId">The Client ID for your ANAF application.</param>
    /// <param name="clientSecret">The Client Secret for your ANAF application.</param>
    /// <returns>An TokenResponse object with new tokens, or null on failure.</returns>
    Task<TokenResponse?> RefreshAccessTokenAsync(string refreshToken, string clientId, string clientSecret);
    
    /// <summary>
    /// Gets a valid access token, refreshing if necessary
    /// </summary>
    Task<string> GetValidAccessTokenAsync(string? userName = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sets the current token (used after code exchange)
    /// </summary>
    void SetToken(TokenResponse token, string? userName = null);
    
    /// <summary>
    /// Removes stored token for a user
    /// </summary>
    Task RemoveTokenAsync(string? userName = null, CancellationToken cancellationToken = default);
}

public class AuthenticationService : IAuthenticationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EFacturaConfig _config;
    private readonly ILogger<AuthenticationService> _logger;
    private readonly ITokenStorageService _tokenStorage;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);

    public AuthenticationService(
        IHttpClientFactory httpClientFactory,
        IOptions<EFacturaConfig> config,
        ILogger<AuthenticationService> logger,
        ITokenStorageService tokenStorage,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _httpClientFactory = httpClientFactory;
        _config = config.Value;
        _logger = logger;
        _tokenStorage = tokenStorage;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Constructs the Authorization URL for redirecting the user to ANAF's Identity Provider (IdP) for authentication.
    /// This URL is used in a web application context where the user's browser is redirected to ANAF for consent.
    /// This method does not make an HTTP request itself.
    /// </summary>
    /// <param name="clientId">The Client ID obtained from ANAF application registration.</param>
    /// <param name="redirectUri">The Redirect URI registered with ANAF, where the authorization code will be sent.</param>
    /// <param name="scope">Optional: The scope of access being requested (e.g., "efactura").</param>
    /// <param name="state">Optional: A unique value to maintain state between the request and callback to prevent CSRF attacks.</param>
    /// <returns>The constructed authorization URL string.</returns>
    public string GetAuthorizationUrl(string clientId, string redirectUri, string? scope = null, string? state = null)
    {
        _logger.LogInformation("Constructing ANAF OAuth Authorization URL.");
        var uriBuilder = new UriBuilder(_config.AuthorizeUrl);
        var query = HttpUtility.ParseQueryString(uriBuilder.Query);

        query["response_type"] = "code";
        query["client_id"] = clientId;
        query["redirect_uri"] = redirectUri;
        query["token_content_type"] = "jwt"; // As specified in the documentation for JWT tokens

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

    /// <summary>
    /// Gets the authorization URL using configuration values
    /// </summary>
    public string GetAuthorizationUrl(string? scope = null, string? state = null)
    {
        return GetAuthorizationUrl(_config.ClientId, _config.RedirectUri, scope, state);
    }

    /// <summary>
    /// Gets an access token and refresh token from ANAF's token endpoint using the authorization code.
    /// This is the second step in the OAuth 2.0 Authorization Code Flow.
    /// </summary>
    /// <param name="code">The authorization code received from the IdP callback.</param>
    /// <param name="redirectUri">The Redirect URI used in the initial authorization request. Must match exactly.</param>
    /// <param name="clientId">The Client ID for your ANAF application.</param>
    /// <param name="clientSecret">The Client Secret for your ANAF application.</param>
    /// <returns>An TokenResponse object containing the tokens, or null on failure.</returns>
    public async Task<TokenResponse?> GetAccessTokenAsync(string code, string redirectUri, string clientId, string clientSecret)
    {
        using var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_config.OAuthBaseUrl);

        // Set Basic Authentication header using client_id and client_secret as required by the documentation
        var authenticationString = $"{clientId}:{clientSecret}";
        var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.ASCII.GetBytes(authenticationString));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);

        var content = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", redirectUri),
            new KeyValuePair<string, string>("token_content_type", "jwt") // Required parameter as per doc
        ]);

        _logger.LogInformation("Sending access token request to ANAF OAuth endpoint...");

        try
        {
            var response = await client.PostAsync("token", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("Received access token response from ANAF (Status: {StatusCode}): {Response}", response.StatusCode, responseBody);

            response.EnsureSuccessStatusCode(); // Throws an exception for non-success status codes (4xx, 5xx)

            // Deserialize the JSON response using System.Text.Json (modern .NET serializer)
            var tokenResponse = System.Text.Json.JsonSerializer.Deserialize<TokenResponse>(responseBody);
            
            return tokenResponse;
        }
        catch (HttpRequestException e)
        {
            _logger.LogError(e, "HttpRequestException when getting access token: {Message}", e.Message);
            return null;
        }
        catch (System.Text.Json.JsonException e)
        {
            _logger.LogError(e, "JSON deserialization error when getting access token: {Message}", e.Message);
            return null;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "An unexpected error occurred when getting access token: {Message}", e.Message);
            return null;
        }
    }

    /// <summary>
    /// Gets access token using configuration values and stores it in token storage
    /// </summary>
    public async Task<TokenResponse?> ExchangeCodeForTokenAsync(string code, string? userName = null)
    {
        var tokenResponse = await GetAccessTokenAsync(code, _config.RedirectUri, _config.ClientId, _config.ClientSecret);
        
        if (tokenResponse != null)
        {
            // Store token in token storage
            userName ??= GetCurrentUserName();
            var tokenDto = TokenDto.FromTokenResponse(tokenResponse, userName);
            
            try
            {
                if (_httpContextAccessor?.HttpContext != null)
                {
                    await _tokenStorage.SetTokenAsync(_httpContextAccessor.HttpContext, tokenDto);
                }
                else
                {
                    await _tokenStorage.SetTokenAsync(userName, tokenDto);
                }
                
                _logger.LogInformation("Token stored successfully for user: {UserName}", userName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to store token for user: {UserName}", userName);
                // Continue anyway - token was successfully obtained
            }
        }
        
        return tokenResponse;
    }

    /// <summary>
    /// Refreshes an expired access token using the refresh token obtained previously.
    /// This avoids requiring the user to re-authenticate with ANAF.
    /// </summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="clientId">The Client ID for your ANAF application.</param>
    /// <param name="clientSecret">The Client Secret for your ANAF application.</param>
    /// <returns>An TokenResponse object with new tokens, or null on failure.</returns>
    public async Task<TokenResponse?> RefreshAccessTokenAsync(string refreshToken, string clientId, string clientSecret)
    {
        using var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(_config.OAuthBaseUrl);

        // Set Basic Authentication header
        var authenticationString = $"{clientId}:{clientSecret}";
        var base64EncodedAuthenticationString = Convert.ToBase64String(Encoding.ASCII.GetBytes(authenticationString));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64EncodedAuthenticationString);

        var content = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken),
            new KeyValuePair<string, string>("token_content_type", "jwt") // Required parameter
        ]);

        _logger.LogInformation("Sending refresh token request to ANAF OAuth endpoint...");

        try
        {
            var response = await client.PostAsync("token", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("Received refresh token response from ANAF (Status: {StatusCode}): {Response}", response.StatusCode, responseBody);

            response.EnsureSuccessStatusCode(); // Throws an exception for non-success status codes

            var tokenResponse = System.Text.Json.JsonSerializer.Deserialize<TokenResponse>(responseBody);
            
            return tokenResponse;
        }
        catch (HttpRequestException e)
        {
            _logger.LogError(e, "HttpRequestException when refreshing access token: {Message}", e.Message);
            return null;
        }
        catch (System.Text.Json.JsonException e)
        {
            _logger.LogError(e, "JSON deserialization error when refreshing access token: {Message}", e.Message);
            return null;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "An unexpected error occurred when refreshing access token: {Message}", e.Message);
            return null;
        }
    }

    public void SetToken(TokenResponse token, string? userName = null)
    {
        userName ??= GetCurrentUserName();
        var tokenDto = TokenDto.FromTokenResponse(token, userName);
        
        try
        {
            if (_httpContextAccessor?.HttpContext != null)
            {
                _tokenStorage.SetTokenAsync(_httpContextAccessor.HttpContext, tokenDto).Wait();
            }
            else
            {
                _tokenStorage.SetTokenAsync(userName, tokenDto).Wait();
            }
            
            _logger.LogInformation("Token set manually for user {UserName}, expires in {ExpiresIn} seconds", userName, token.ExpiresIn);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store token for user: {UserName}", userName);
        }
    }

    public async Task<string> GetValidAccessTokenAsync(string? userName = null, CancellationToken cancellationToken = default)
    {
        await _tokenSemaphore.WaitAsync(cancellationToken);
        try
        {
            userName ??= GetCurrentUserName();
            
            // Get token from storage
            TokenDto? currentToken = null;
            
            try
            {
                if (_httpContextAccessor?.HttpContext != null)
                {
                    currentToken = await _tokenStorage.GetTokenAsync(_httpContextAccessor.HttpContext, cancellationToken);
                }
                else
                {
                    currentToken = await _tokenStorage.GetTokenAsync(userName, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to retrieve token from storage for user: {UserName}", userName);
            }

            // Check if we have a valid token
            if (currentToken?.IsValid == true)
            {
                _logger.LogDebug("Using existing valid access token for user: {UserName}", userName);
                return currentToken.AccessToken;
            }

            // Try to refresh if we have a refresh token
            if (currentToken?.RefreshToken != null)
            {
                try
                {
                    _logger.LogInformation("Refreshing expired access token for user: {UserName}", userName);
                    var refreshedToken = await RefreshAccessTokenAsync(currentToken.RefreshToken, _config.ClientId, _config.ClientSecret);
                    if (refreshedToken != null)
                    {
                        // Store the refreshed token
                        var refreshedTokenDto = TokenDto.FromTokenResponse(refreshedToken, userName);
                        
                        if (_httpContextAccessor?.HttpContext != null)
                        {
                            await _tokenStorage.SetTokenAsync(_httpContextAccessor.HttpContext, refreshedTokenDto, cancellationToken);
                        }
                        else
                        {
                            await _tokenStorage.SetTokenAsync(userName, refreshedTokenDto, cancellationToken);
                        }
                        
                        return refreshedToken.AccessToken;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh token for user: {UserName}", userName);
                    
                    // Remove invalid token
                    try
                    {
                        if (_httpContextAccessor?.HttpContext != null)
                        {
                            await _tokenStorage.RemoveTokenAsync(_httpContextAccessor.HttpContext, cancellationToken);
                        }
                        else
                        {
                            await _tokenStorage.RemoveTokenAsync(userName, cancellationToken);
                        }
                    }
                    catch (Exception removeEx)
                    {
                        _logger.LogWarning(removeEx, "Failed to remove invalid token for user: {UserName}", userName);
                    }
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

    public async Task RemoveTokenAsync(string? userName = null, CancellationToken cancellationToken = default)
    {
        userName ??= GetCurrentUserName();
        
        try
        {
            if (_httpContextAccessor?.HttpContext != null)
            {
                await _tokenStorage.RemoveTokenAsync(_httpContextAccessor.HttpContext, cancellationToken);
            }
            else
            {
                await _tokenStorage.RemoveTokenAsync(userName, cancellationToken);
            }
            
            _logger.LogInformation("Token removed for user: {UserName}", userName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove token for user: {UserName}", userName);
        }
    }

    private string GetCurrentUserName()
    {
        // Try to get from HttpContext first
        if (_httpContextAccessor?.HttpContext?.User?.Identity?.IsAuthenticated == true)
        {
            var user = _httpContextAccessor.HttpContext.User;
            var userName = user.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ??
                          user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ??
                          user.FindFirst("preferred_username")?.Value ??
                          user.FindFirst("email")?.Value ??
                          user.Identity.Name;

            if (!string.IsNullOrWhiteSpace(userName))
            {
                return userName;
            }
        }
        
        // Fallback to default user for non-web scenarios
        return "default_user";
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
