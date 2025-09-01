using Microsoft.AspNetCore.Mvc;
using RomaniaEFacturaLibrary.Services.Authentication;
using RomaniaEFacturaLibrary.Models.Authentication;

namespace RomaniaEFacturaLibrary.Examples.Controllers;

/// <summary>
/// Controller demonstrating OAuth2 authentication flow with ANAF EFactura
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthenticationService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Initiates the OAuth2 authentication flow by redirecting to ANAF
    /// </summary>
    /// <param name="returnUrl">URL to redirect to after successful authentication</param>
    /// <returns>Redirect to ANAF OAuth2 authorization endpoint</returns>
    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        try
        {
            // Generate a state parameter to prevent CSRF attacks
            var state = Guid.NewGuid().ToString();
            
            // Store return URL and state in session/cache for verification later
            HttpContext.Session.SetString("oauth_state", state);
            if (!string.IsNullOrEmpty(returnUrl))
            {
                HttpContext.Session.SetString("return_url", returnUrl);
            }

            // Generate authorization URL that will prompt for certificate selection
            var authUrl = _authService.GetAuthorizationUrl("efactura", state);
            
            _logger.LogInformation("Redirecting user to ANAF OAuth2 authorization endpoint");
            
            return Redirect(authUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating OAuth2 authentication");
            return BadRequest(new { error = "Failed to initiate authentication", details = ex.Message });
        }
    }

    /// <summary>
    /// Handles the OAuth2 callback from ANAF with authorization code
    /// </summary>
    /// <param name="code">Authorization code from ANAF</param>
    /// <param name="state">State parameter for CSRF protection</param>
    /// <param name="error">Error parameter if authentication failed</param>
    /// <returns>Redirect to application or error response</returns>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code = null,
        [FromQuery] string? state = null,
        [FromQuery] string? error = null)
    {
        try
        {
            // Check for OAuth2 errors
            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogWarning("OAuth2 authentication failed: {Error}", error);
                return BadRequest(new { error = "Authentication failed", details = error });
            }

            // Validate required parameters
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            {
                _logger.LogWarning("Missing required OAuth2 parameters");
                return BadRequest(new { error = "Missing required parameters" });
            }

            // Verify state parameter to prevent CSRF attacks
            var expectedState = HttpContext.Session.GetString("oauth_state");
            if (state != expectedState)
            {
                _logger.LogWarning("OAuth2 state parameter mismatch");
                return BadRequest(new { error = "Invalid state parameter" });
            }

            // Exchange authorization code for access token
            _logger.LogInformation("Exchanging authorization code for access token");
            var tokenResponse = await _authService.ExchangeCodeForTokenAsync(code);

            if (tokenResponse == null)
            {
                _logger.LogError("Failed to exchange authorization code for token");
                return BadRequest(new { error = "Token exchange failed" });
            }

            _logger.LogInformation("Authentication successful, token expires in {ExpiresIn} seconds", 
                tokenResponse.ExpiresIn);

            // Store token information in session (in production, use secure storage)
            HttpContext.Session.SetString("access_token", tokenResponse.AccessToken);
            if (!string.IsNullOrEmpty(tokenResponse.RefreshToken))
            {
                HttpContext.Session.SetString("refresh_token", tokenResponse.RefreshToken);
            }

            // Redirect to original URL or default dashboard
            var returnUrl = HttpContext.Session.GetString("return_url") ?? "/api/efactura/dashboard";
            HttpContext.Session.Remove("oauth_state");
            HttpContext.Session.Remove("return_url");

            return Redirect(returnUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing OAuth2 callback");
            return BadRequest(new { error = "Authentication processing failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Gets current authentication status
    /// </summary>
    /// <returns>Authentication status information</returns>
    [HttpGet("status")]
    public async Task<IActionResult> GetAuthStatus()
    {
        try
        {
            var accessToken = await _authService.GetValidAccessTokenAsync();
            
            return Ok(new
            {
                isAuthenticated = true,
                hasValidToken = !string.IsNullOrEmpty(accessToken),
                message = "User is authenticated with valid token"
            });
        }
        catch (AuthenticationException)
        {
            return Ok(new
            {
                isAuthenticated = false,
                hasValidToken = false,
                message = "User is not authenticated or token is invalid",
                loginUrl = Url.Action("Login", "Auth")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking authentication status");
            return StatusCode(500, new { error = "Failed to check authentication status" });
        }
    }

    /// <summary>
    /// Manually refreshes the access token using refresh token
    /// </summary>
    /// <returns>New token information or error</returns>
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken()
    {
        try
        {
            // This will automatically refresh the token if needed
            var accessToken = await _authService.GetValidAccessTokenAsync();
            
            return Ok(new
            {
                success = true,
                message = "Token refreshed successfully",
                hasValidToken = !string.IsNullOrEmpty(accessToken)
            });
        }
        catch (AuthenticationException ex)
        {
            _logger.LogWarning("Token refresh failed: {Message}", ex.Message);
            return Unauthorized(new
            {
                success = false,
                error = "Token refresh failed",
                message = "Please re-authenticate",
                loginUrl = Url.Action("Login", "Auth")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing token");
            return StatusCode(500, new { error = "Failed to refresh token" });
        }
    }

    /// <summary>
    /// Logs out the user by clearing session data
    /// </summary>
    /// <returns>Logout confirmation</returns>
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        try
        {
            // Clear session data
            HttpContext.Session.Clear();
            
            _logger.LogInformation("User logged out successfully");
            
            return Ok(new
            {
                success = true,
                message = "Logged out successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return StatusCode(500, new { error = "Failed to logout" });
        }
    }
}