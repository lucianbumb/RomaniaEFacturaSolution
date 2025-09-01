using Microsoft.AspNetCore.Mvc;
using RomaniaEFacturaLibrary.Services;
using RomaniaEFacturaLibrary.Services.Authentication;
using RomaniaEFacturaLibrary.Models.Authentication;

namespace RomaniaEFacturaWebApi.Controllers;

/// <summary>
/// Controller for EFactura authentication and basic operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EFacturaController : ControllerBase
{
    private readonly IEFacturaClient _eFacturaClient;
    private readonly IAuthenticationService _authService;
    private readonly ILogger<EFacturaController> _logger;
    private const string DefaultCif = "123456789";

    public EFacturaController(
        IEFacturaClient eFacturaClient,
        IAuthenticationService authService,
        ILogger<EFacturaController> logger)
    {
        _eFacturaClient = eFacturaClient;
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Initiates OAuth2 login flow with ANAF
    /// </summary>
    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        try
        {
            // Generate state parameter for CSRF protection
            var state = Guid.NewGuid().ToString();
            
            // Store state and return URL in session
            HttpContext.Session.SetString("oauth_state", state);
            if (!string.IsNullOrEmpty(returnUrl))
            {
                HttpContext.Session.SetString("return_url", returnUrl);
            }

            // Get authorization URL
            var authUrl = _authService.GetAuthorizationUrl("efactura", state);
            
            _logger.LogInformation("Redirecting to ANAF OAuth2 login");
            
            return Ok(new
            {
                success = true,
                authUrl,
                message = "Redirect to authUrl to complete authentication",
                state
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating OAuth2 login");
            return BadRequest(new { error = "Failed to initiate login", details = ex.Message });
        }
    }

    /// <summary>
    /// Handles OAuth2 callback from ANAF
    /// </summary>
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
                return BadRequest(new { error = "Missing required parameters (code or state)" });
            }

            // Verify state parameter
            var expectedState = HttpContext.Session.GetString("oauth_state");
            if (state != expectedState)
            {
                _logger.LogWarning("OAuth2 state parameter mismatch");
                return BadRequest(new { error = "Invalid state parameter - possible CSRF attack" });
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

            // Get return URL or default
            var returnUrl = HttpContext.Session.GetString("return_url") ?? "/api/efactura/status";
            
            // Clean up session
            HttpContext.Session.Remove("oauth_state");
            HttpContext.Session.Remove("return_url");

            return Ok(new
            {
                success = true,
                message = "Authentication successful",
                expiresIn = tokenResponse.ExpiresIn,
                returnUrl,
                nextSteps = new[]
                {
                    "You can now use /api/efactura/download to get invoices",
                    "Use /api/invoice/upload to upload new invoices",
                    "Check /api/efactura/status for authentication status"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing OAuth2 callback");
            return BadRequest(new { error = "Authentication processing failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Downloads invoices for the default CIF (123456789)
    /// </summary>
    [HttpGet("download")]
    public async Task<IActionResult> Download(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? cif = null)
    {
        try
        {
            // Use provided CIF or default
            var targetCif = cif ?? DefaultCif;
            
            // Default date range if not provided
            from ??= DateTime.UtcNow.AddDays(-30);
            to ??= DateTime.UtcNow;

            // Validate date range
            if (from > to)
            {
                return BadRequest(new { error = "Start date cannot be later than end date" });
            }

            if ((to.Value - from.Value).TotalDays > 365)
            {
                return BadRequest(new { error = "Date range cannot exceed 365 days" });
            }

            _logger.LogInformation("Downloading invoices for CIF {Cif} from {From} to {To}", targetCif, from, to);

            var invoices = await _eFacturaClient.GetInvoicesAsync(targetCif, from, to);

            return Ok(new
            {
                success = true,
                cif = targetCif,
                dateRange = new { from, to },
                count = invoices.Count,
                invoices = invoices.Select(inv => new
                {
                    id = inv.Id,
                    issueDate = inv.IssueDate,
                    dueDate = inv.DueDate,
                    supplier = inv.AccountingSupplierParty?.PartyLegalEntity?.RegistrationName,
                    customer = inv.AccountingCustomerParty?.PartyLegalEntity?.RegistrationName,
                    totalAmount = inv.LegalMonetaryTotal?.TaxInclusiveAmount?.Value,
                    currency = inv.DocumentCurrencyCode,
                    lines = inv.InvoiceLines?.Count ?? 0
                }),
                retrievedAt = DateTime.UtcNow
            });
        }
        catch (AuthenticationException)
        {
            return Unauthorized(new
            {
                error = "Authentication required",
                message = "Please authenticate first",
                loginUrl = Url.Action("Login", "EFactura")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading invoices for CIF {Cif}", cif ?? DefaultCif);
            return StatusCode(500, new { error = "Failed to download invoices", details = ex.Message });
        }
    }

    /// <summary>
    /// Gets current authentication status
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        try
        {
            var accessToken = await _authService.GetValidAccessTokenAsync();
            
            return Ok(new
            {
                isAuthenticated = true,
                hasValidToken = !string.IsNullOrEmpty(accessToken),
                message = "User is authenticated with valid token",
                defaultCif = DefaultCif,
                availableEndpoints = new
                {
                    login = Url.Action("Login", "EFactura"),
                    download = Url.Action("Download", "EFactura"),
                    upload = "/api/invoice/upload"
                }
            });
        }
        catch (AuthenticationException)
        {
            return Ok(new
            {
                isAuthenticated = false,
                hasValidToken = false,
                message = "User is not authenticated or token is invalid",
                loginUrl = Url.Action("Login", "EFactura"),
                instructions = new[]
                {
                    "1. Call GET /api/efactura/login to get authorization URL",
                    "2. Navigate to the authUrl in a browser with digital certificate",
                    "3. After authentication, you'll be redirected to /api/efactura/callback",
                    "4. Then you can use /api/efactura/download and /api/invoice/upload"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking authentication status");
            return StatusCode(500, new { error = "Failed to check authentication status" });
        }
    }

    /// <summary>
    /// Logs out the current user
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        try
        {
            // Remove stored tokens
            await _authService.RemoveTokenAsync();
            
            // Clear session
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