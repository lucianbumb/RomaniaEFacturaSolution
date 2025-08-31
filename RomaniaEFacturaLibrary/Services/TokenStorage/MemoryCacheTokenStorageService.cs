using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using RomaniaEFacturaLibrary.Models.Authentication;
using System.Security.Claims;

namespace RomaniaEFacturaLibrary.Services.TokenStorage;

/// <summary>
/// Token storage service using MemoryCache
/// </summary>
public class MemoryCacheTokenStorageService : ITokenStorageService
{
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<MemoryCacheTokenStorageService> _logger;
    private const string TokenCacheKeyPrefix = "efactura_token_";

    public MemoryCacheTokenStorageService(
        IMemoryCache memoryCache,
        ILogger<MemoryCacheTokenStorageService> logger)
    {
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public Task SetTokenAsync(string userName, TokenDto token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("Username cannot be null or empty", nameof(userName));
        
        if (token == null)
            throw new ArgumentNullException(nameof(token));

        var cacheKey = GetCacheKey(userName);
        token.UserName = userName;
        
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = token.ExpiresAt,
            SlidingExpiration = TimeSpan.FromMinutes(30), // Extend cache if actively used
            Priority = CacheItemPriority.High
        };
        
        _memoryCache.Set(cacheKey, token, cacheOptions);
        
        _logger.LogDebug("Token stored in cache for user: {UserName}, expires at: {ExpiresAt}", 
            userName, token.ExpiresAt);
        
        return Task.CompletedTask;
    }

    public Task SetTokenAsync(HttpContext httpContext, TokenDto token, CancellationToken cancellationToken = default)
    {
        var userName = GetUserNameFromHttpContext(httpContext);
        return SetTokenAsync(userName, token, cancellationToken);
    }

    public Task<TokenDto?> GetTokenAsync(string userName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return Task.FromResult<TokenDto?>(null);

        var cacheKey = GetCacheKey(userName);
        
        if (_memoryCache.TryGetValue(cacheKey, out TokenDto? token))
        {
            if (token != null && token.IsValid)
            {
                _logger.LogDebug("Valid token retrieved from cache for user: {UserName}", userName);
                return Task.FromResult<TokenDto?>(token);
            }
            else
            {
                _logger.LogDebug("Expired token found in cache for user: {UserName}, removing", userName);
                _memoryCache.Remove(cacheKey);
            }
        }
        
        return Task.FromResult<TokenDto?>(null);
    }

    public Task<TokenDto?> GetTokenAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        var userName = GetUserNameFromHttpContext(httpContext);
        return GetTokenAsync(userName, cancellationToken);
    }

    public Task RemoveTokenAsync(string userName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName))
            return Task.CompletedTask;

        var cacheKey = GetCacheKey(userName);
        _memoryCache.Remove(cacheKey);
        
        _logger.LogDebug("Token removed from cache for user: {UserName}", userName);
        
        return Task.CompletedTask;
    }

    public Task RemoveTokenAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        var userName = GetUserNameFromHttpContext(httpContext);
        return RemoveTokenAsync(userName, cancellationToken);
    }

    public async Task<bool> HasValidTokenAsync(string userName, CancellationToken cancellationToken = default)
    {
        var token = await GetTokenAsync(userName, cancellationToken);
        return token != null && token.IsValid;
    }

    public async Task<bool> HasValidTokenAsync(HttpContext httpContext, CancellationToken cancellationToken = default)
    {
        var userName = GetUserNameFromHttpContext(httpContext);
        return await HasValidTokenAsync(userName, cancellationToken);
    }

    private static string GetCacheKey(string userName)
    {
        return $"{TokenCacheKeyPrefix}{userName.ToLowerInvariant()}_efactura";
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
