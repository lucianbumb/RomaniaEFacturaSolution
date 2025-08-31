using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using RomaniaEFacturaLibrary.Models.Authentication;
using System.Security.Claims;
using System.Text.Json;

namespace RomaniaEFacturaLibrary.Services.Authentication;

/// <summary>
/// Token storage service using HTTP cookies
/// </summary>
public class CookieTokenStorageService : ITokenStorageService
{
    private readonly ILogger<CookieTokenStorageService> _logger;
    private const string TokenCookiePrefix = "efactura_token_";

    public CookieTokenStorageService(ILogger<CookieTokenStorageService> logger)
    {
        _logger = logger;
    }

    public Task SetTokenAsync(string userName, TokenDto token, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Cookie storage requires HttpContext. Use SetTokenAsync(HttpContext, TokenDto) instead.");
    }

    public Task SetTokenAsync(HttpContext httpContext, TokenDto token, CancellationToken cancellationToken = default)
    {
        if (httpContext == null)
            throw new ArgumentNullException(nameof(httpContext));
        
        if (token == null)
            throw new ArgumentNullException(nameof(token));

        var userName = GetUserNameFromHttpContext(httpContext);
        token.UserName = userName;
        
        var cookieName = GetCookieName(userName);
        var tokenJson = JsonSerializer.Serialize(token);
        
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true, // Prevent JavaScript access
            Secure = httpContext.Request.IsHttps, // Only send over HTTPS in production
            SameSite = SameSiteMode.Strict, // CSRF protection
            Expires = token.ExpiresAt,
            Path = "/",
            IsEssential = true // Not subject to consent policies
        };
        
        httpContext.Response.Cookies.Append(cookieName, tokenJson, cookieOptions);
        
        _logger.LogDebug("Token stored in cookie for user: {UserName}, expires at: {ExpiresAt}", 
            userName, token.ExpiresAt);
        
        return Task.CompletedTask;
    }

    public Task<TokenDto?> GetTokenAsync(string userName, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Cookie storage requires HttpContext. Use GetTokenAsync(HttpContext) instead.");
    }

    public Task<TokenDto?> GetTokenAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        if (httpContext == null)
            return Task.FromResult<TokenDto?>(null);

        try
        {
            var userName = GetUserNameFromHttpContext(httpContext);
            var cookieName = GetCookieName(userName);
            
            if (httpContext.Request.Cookies.TryGetValue(cookieName, out string? tokenJson) && 
                !string.IsNullOrWhiteSpace(tokenJson))
            {
                var token = JsonSerializer.Deserialize<TokenDto>(tokenJson);
                
                if (token != null && token.IsValid)
                {
                    _logger.LogDebug("Valid token retrieved from cookie for user: {UserName}", userName);
                    return Task.FromResult<TokenDto?>(token);
                }
                else
                {
                    _logger.LogDebug("Expired token found in cookie for user: {UserName}, removing", userName);
                    httpContext.Response.Cookies.Delete(cookieName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving token from cookie");
        }
        
        return Task.FromResult<TokenDto?>(null);
    }

    public Task RemoveTokenAsync(string userName, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Cookie storage requires HttpContext. Use RemoveTokenAsync(HttpContext) instead.");
    }

    public Task RemoveTokenAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        if (httpContext == null)
            return Task.CompletedTask;

        try
        {
            var userName = GetUserNameFromHttpContext(httpContext);
            var cookieName = GetCookieName(userName);
            
            httpContext.Response.Cookies.Delete(cookieName);
            
            _logger.LogDebug("Token cookie removed for user: {UserName}", userName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing token cookie");
        }
        
        return Task.CompletedTask;
    }

    public async Task<bool> HasValidTokenAsync(string userName, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Cookie storage requires HttpContext. Use HasValidTokenAsync(HttpContext) instead.");
    }

    public async Task<bool> HasValidTokenAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(httpContext, cancellationToken);
        return token != null && token.IsValid;
    }

    private static string GetCookieName(string userName)
    {
        return $"{TokenCookiePrefix}{userName.ToLowerInvariant()}_efactura";
    }

    private string GetUserNameFromHttpContext(HttpContext httpContext)
    {
        if (httpContext?.User?.Identity?.IsAuthenticated == true)
        {
            // Try different claim types for username
            var userName = httpContext.User.FindFirst(ClaimTypes.Name)?.Value ??
                          httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                          httpContext.User.FindFirst("preferred_username")?.Value ??
                          httpContext.User.FindFirst("email")?.Value ??
                          httpContext.User.Identity.Name;

            if (!string.IsNullOrWhiteSpace(userName))
            {
                return userName;
            }
        }
        
        _logger.LogWarning("Could not extract username from HttpContext. User authenticated: {IsAuthenticated}", 
            httpContext?.User?.Identity?.IsAuthenticated ?? false);
        
        throw new InvalidOperationException("Cannot determine current user from HttpContext. Ensure user is authenticated.");
    }
}
