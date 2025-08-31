using Microsoft.AspNetCore.Http;
using RomaniaEFacturaLibrary.Models.Authentication;

namespace RomaniaEFacturaLibrary.Services.TokenStorage;

/// <summary>
/// Interface for token storage services
/// </summary>
public interface ITokenStorageService
{
    /// <summary>
    /// Stores token for a specific user
    /// </summary>
    Task SetTokenAsync(string userName, TokenDto token, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Stores token using HttpContext to get current user
    /// </summary>
    Task SetTokenAsync(HttpContext httpContext, TokenDto token, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Retrieves token for a specific user
    /// </summary>
    Task<TokenDto?> GetTokenAsync(string userName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Retrieves token using HttpContext to get current user
    /// </summary>
    Task<TokenDto?> GetTokenAsync(HttpContext httpContext, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Removes token for a specific user
    /// </summary>
    Task RemoveTokenAsync(string userName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Removes token using HttpContext to get current user
    /// </summary>
    Task RemoveTokenAsync(HttpContext httpContext, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if a valid token exists for a user
    /// </summary>
    Task<bool> HasValidTokenAsync(string userName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if a valid token exists using HttpContext
    /// </summary>
    Task<bool> HasValidTokenAsync(HttpContext httpContext, CancellationToken cancellationToken = default);
}
