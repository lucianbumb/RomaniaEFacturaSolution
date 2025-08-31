using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RomaniaEFacturaLibrary.Services.Authentication;
using RomaniaEFacturaLibrary.Services.TokenStorage;
using System.Security.Claims;

namespace RomaniaEFacturaLibrary.Examples.Controllers;

/// <summary>
/// Controller for handling OAuth2 authentication with ANAF
/// </summary>
[ApiController]
[Route("api/efactura/[controller]")]
public class AuthenticationController : ControllerBase
{
    private readonly IAuthenticationService _authService;
    private readonly ITokenStorageService _tokenStorage;
    private readonly ILogger<AuthenticationController> _logger;

    public AuthenticationController(
        IAuthenticationService authService,
        ITokenStorageService tokenStorage,
        ILogger<AuthenticationController> logger)
    {
        _authService = authService;
        _tokenStorage = tokenStorage;
        _logger = logger;
    }

    /// <summary>
    /// Initiates the OAuth2 authentication flow by redirecting to ANAF
    /// </summary>
    /// <param name="clientId">OAuth2 client ID</param>
    /// <param name="redirectUri">Callback URI after authentication</param>
    /// <param name="scope">OAuth2 scope (default: efactura)</param>
    /// <param name="state">Optional state parameter for security</param>
    /// <returns>Authentication URL to redirect user to</returns>
    [HttpGet("login")]
    [ProducesResponseType(typeof(AuthenticationUrlResponse), 200)]
    [ProducesResponseType(400)]
    public IActionResult InitiateLogin(
        [FromQuery] string clientId,
        [FromQuery] string redirectUri,
        [FromQuery] string scope = "efactura",
        [FromQuery] string? state = null)
    {
        try
        {
            _logger.LogInformation("Initiating OAuth2 login for client: {ClientId}", clientId);

            var authState = state ?? Guid.NewGuid().ToString();
            var authUrl = _authService.GetAuthorizationUrl(clientId, redirectUri, scope, authState);

            _logger.LogInformation("Generated authentication URL for client: {ClientId}", clientId);

            return Ok(new AuthenticationUrlResponse
            {
                AuthenticationUrl = authUrl,
                State = authState,
                ExpiresIn = 300 // 5 minutes
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate authentication URL for client: {ClientId}", clientId);
            return BadRequest(new ErrorResponse
            {
                Error = "authentication_error",
                ErrorDescription = $"Failed to generate authentication URL: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Handles the OAuth2 callback from ANAF and exchanges code for token
    /// </summary>
    /// <param name="request">OAuth2 callback parameters</param>
    /// <returns>Authentication result with token information</returns>
    [HttpPost("callback")]
    [ProducesResponseType(typeof(AuthenticationResultResponse), 200)]
    [ProducesResponseType(400)]
    public async Task<IActionResult> HandleCallback([FromBody] AuthenticationCallbackRequest request)
    {
        try
        {
            _logger.LogInformation("Handling OAuth2 callback for code: {Code}", request.Code[..8] + "...");

            // Validate state parameter if provided
            if (!string.IsNullOrEmpty(request.ExpectedState) && request.State != request.ExpectedState)
            {
                _logger.LogWarning("State parameter mismatch. Expected: {Expected}, Received: {Received}", 
                    request.ExpectedState, request.State);
                
                return BadRequest(new ErrorResponse
                {
                    Error = "invalid_state",
                    ErrorDescription = "State parameter validation failed"
                });
            }

            // Exchange authorization code for access token
            var token = await _authService.GetAccessTokenAsync(
                request.Code,
                request.ClientId,
                request.ClientSecret,
                request.RedirectUri);

            _logger.LogInformation("Successfully obtained access token, expires at: {ExpiresAt}", token.ExpiresAt);

            // Token is automatically stored by the authentication service
            // Check if user is authenticated for storage context
            if (HttpContext.User.Identity?.IsAuthenticated == true)
            {
                var hasValidToken = await _tokenStorage.HasValidTokenAsync(HttpContext);
                _logger.LogInformation("Token stored successfully. Valid token available: {HasToken}", hasValidToken);
            }

            return Ok(new AuthenticationResultResponse
            {
                Success = true,
                TokenType = "Bearer",
                ExpiresAt = token.ExpiresAt,
                Scope = token.Scope,
                Message = "Authentication successful"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OAuth2 callback failed for code: {Code}", request.Code[..8] + "...");
            return BadRequest(new ErrorResponse
            {
                Error = "authentication_failed",
                ErrorDescription = $"Token exchange failed: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Gets the current authentication status
    /// </summary>
    /// <returns>Authentication status information</returns>
    [HttpGet("status")]
    [Authorize]
    [ProducesResponseType(typeof(AuthenticationStatusResponse), 200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetAuthenticationStatus()
    {
        try
        {
            var userName = HttpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? 
                          HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? 
                          "unknown";

            _logger.LogInformation("Checking authentication status for user: {UserName}", userName);

            var hasValidToken = await _tokenStorage.HasValidTokenAsync(HttpContext);
            
            if (hasValidToken)
            {
                var token = await _tokenStorage.GetTokenAsync(HttpContext);
                return Ok(new AuthenticationStatusResponse
                {
                    IsAuthenticated = true,
                    UserName = userName,
                    TokenExpiresAt = token?.ExpiresAt,
                    Scope = token?.Scope
                });
            }
            else
            {
                return Ok(new AuthenticationStatusResponse
                {
                    IsAuthenticated = false,
                    UserName = userName,
                    Message = "No valid EFactura token available"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check authentication status");
            return Ok(new AuthenticationStatusResponse
            {
                IsAuthenticated = false,
                Message = $"Error checking authentication: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Refreshes the current access token
    /// </summary>
    /// <returns>Token refresh result</returns>
    [HttpPost("refresh")]
    [Authorize]
    [ProducesResponseType(typeof(TokenRefreshResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> RefreshToken()
    {
        try
        {
            var userName = HttpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
            _logger.LogInformation("Refreshing token for user: {UserName}", userName);

            // This will automatically refresh the token if needed and store it
            var accessToken = await _authService.GetValidAccessTokenAsync();
            
            var token = await _tokenStorage.GetTokenAsync(HttpContext);

            return Ok(new TokenRefreshResponse
            {
                Success = true,
                ExpiresAt = token?.ExpiresAt ?? DateTime.UtcNow.AddHours(1),
                Message = "Token refreshed successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh token");
            return BadRequest(new ErrorResponse
            {
                Error = "token_refresh_failed",
                ErrorDescription = $"Token refresh failed: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Logs out the user and clears stored tokens
    /// </summary>
    /// <returns>Logout result</returns>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(typeof(LogoutResponse), 200)]
    public async Task<IActionResult> Logout()
    {
        try
        {
            var userName = HttpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
            _logger.LogInformation("Logging out user: {UserName}", userName);

            await _tokenStorage.RemoveTokenAsync(HttpContext);

            _logger.LogInformation("User logged out successfully: {UserName}", userName);

            return Ok(new LogoutResponse
            {
                Success = true,
                Message = "Logged out successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to logout user");
            return Ok(new LogoutResponse
            {
                Success = false,
                Message = $"Logout failed: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Gets detailed token information (for debugging)
    /// </summary>
    /// <returns>Token details</returns>
    [HttpGet("token-info")]
    [Authorize]
    [ProducesResponseType(typeof(TokenInfoResponse), 200)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetTokenInfo()
    {
        try
        {
            var userName = HttpContext.User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
            var token = await _tokenStorage.GetTokenAsync(HttpContext);

            if (token == null)
            {
                return Ok(new TokenInfoResponse
                {
                    HasToken = false,
                    Message = "No token available"
                });
            }

            return Ok(new TokenInfoResponse
            {
                HasToken = true,
                IsValid = token.IsValid,
                ExpiresAt = token.ExpiresAt,
                Scope = token.Scope,
                UserName = token.UserName,
                TimeUntilExpiry = token.ExpiresAt - DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get token info");
            return BadRequest(new ErrorResponse
            {
                Error = "token_info_failed",
                ErrorDescription = $"Failed to get token info: {ex.Message}"
            });
        }
    }
}

// DTOs for request/response
public class AuthenticationCallbackRequest
{
    public string Code { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string? State { get; set; }
    public string? ExpectedState { get; set; }
}

public class AuthenticationUrlResponse
{
    public string AuthenticationUrl { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
}

public class AuthenticationResultResponse
{
    public bool Success { get; set; }
    public string TokenType { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string? Scope { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class AuthenticationStatusResponse
{
    public bool IsAuthenticated { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime? TokenExpiresAt { get; set; }
    public string? Scope { get; set; }
    public string? Message { get; set; }
}

public class TokenRefreshResponse
{
    public bool Success { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class LogoutResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class TokenInfoResponse
{
    public bool HasToken { get; set; }
    public bool IsValid { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Scope { get; set; }
    public string? UserName { get; set; }
    public TimeSpan? TimeUntilExpiry { get; set; }
    public string? Message { get; set; }
}

public class ErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string ErrorDescription { get; set; } = string.Empty;
}
