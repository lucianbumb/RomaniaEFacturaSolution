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
    Task<TokenResponse?> ExchangeCodeForTokenAsync(string code);
    
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
    Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sets the current token (used after code exchange)
    /// </summary>
    void SetToken(TokenResponse token);
}

public class AuthenticationService : IAuthenticationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EFacturaConfig _config;
    private readonly ILogger<AuthenticationService> _logger;
    private TokenResponse? _currentToken;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);

    public AuthenticationService(
        IHttpClientFactory httpClientFactory,
        IOptions<EFacturaConfig> config,
        ILogger<AuthenticationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config.Value;
        _logger = logger;
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
            
            if (tokenResponse != null)
            {
                _currentToken = tokenResponse;
            }
            
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
    /// Gets access token using configuration values
    /// </summary>
    public async Task<TokenResponse?> ExchangeCodeForTokenAsync(string code)
    {
        return await GetAccessTokenAsync(code, _config.RedirectUri, _config.ClientId, _config.ClientSecret);
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
            
            if (tokenResponse != null)
            {
                _currentToken = tokenResponse;
            }
            
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

    public void SetToken(TokenResponse token)
    {
        _currentToken = token;
        _logger.LogInformation("Token set manually, expires in {ExpiresIn} seconds", token.ExpiresIn);
    }

    public async Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken = default)
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
                    var refreshedToken = await RefreshAccessTokenAsync(_currentToken.RefreshToken, _config.ClientId, _config.ClientSecret);
                    if (refreshedToken != null)
                    {
                        return refreshedToken.AccessToken;
                    }
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
}

/// <summary>
/// Exception thrown when authentication fails
/// </summary>
public class AuthenticationException : Exception
{
    public AuthenticationException(string message) : base(message) { }
    public AuthenticationException(string message, Exception innerException) : base(message, innerException) { }
}
