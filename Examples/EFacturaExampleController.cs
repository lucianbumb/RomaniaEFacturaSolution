using Microsoft.AspNetCore.Mvc;
using RomaniaEFacturaLibrary.Services.Api;
using RomaniaEFacturaLibrary.Services.Authentication;
using RomaniaEFacturaLibrary.Services.TokenStorage;

namespace RomaniaEFacturaLibrary.Examples;

/// <summary>
/// Example controller demonstrating the usage of the updated EFactura library
/// with flexible token storage and CIF parameters
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EFacturaExampleController : ControllerBase
{
    private readonly IEFacturaApiClient _apiClient;
    private readonly IAuthenticationService _authService;
    private readonly ITokenStorageService _tokenStorage;
    private readonly ILogger<EFacturaExampleController> _logger;

    public EFacturaExampleController(
        IEFacturaApiClient apiClient,
        IAuthenticationService authService,
        ITokenStorageService tokenStorage,
        ILogger<EFacturaExampleController> logger)
    {
        _apiClient = apiClient;
        _authService = authService;
        _tokenStorage = tokenStorage;
        _logger = logger;
    }

    /// <summary>
    /// Step 1: Start OAuth2 authentication flow
    /// </summary>
    [HttpGet("auth/login")]
    public IActionResult StartAuthentication([FromQuery] string? state = null)
    {
        try
        {
            var authUrl = _authService.GetAuthorizationUrl(
                clientId: "your-client-id", // In real app, get from config
                redirectUri: "https://your-app.com/api/efactura/auth/callback",
                scope: "efactura",
                state: state ?? Guid.NewGuid().ToString()
            );

            _logger.LogInformation("Redirecting user to ANAF authentication: {AuthUrl}", authUrl);
            return Ok(new { AuthenticationUrl = authUrl });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate authentication URL");
            return BadRequest($"Failed to start authentication: {ex.Message}");
        }
    }

    /// <summary>
    /// Step 2: Handle OAuth2 callback and store token
    /// </summary>
    [HttpGet("auth/callback")]
    public async Task<IActionResult> HandleCallback(
        [FromQuery] string code,
        [FromQuery] string? state = null)
    {
        try
        {
            _logger.LogInformation("Handling OAuth2 callback with code: {Code}", code);

            // Exchange authorization code for access token
            var token = await _authService.GetAccessTokenAsync(
                code: code,
                clientId: "your-client-id", // In real app, get from config
                clientSecret: "your-client-secret", // In real app, get from config
                redirectUri: "https://your-app.com/api/efactura/auth/callback"
            );

            // Token is automatically stored using the configured storage service
            _logger.LogInformation("Authentication successful, token stored");

            return Ok(new 
            { 
                Message = "Authentication successful", 
                TokenExpires = token.ExpiresAt 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed");
            return BadRequest($"Authentication failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Check authentication status
    /// </summary>
    [HttpGet("auth/status")]
    public async Task<IActionResult> GetAuthStatus()
    {
        try
        {
            var hasValidToken = await _tokenStorage.HasValidTokenAsync(HttpContext);
            return Ok(new { IsAuthenticated = hasValidToken });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check auth status");
            return Ok(new { IsAuthenticated = false, Error = ex.Message });
        }
    }

    /// <summary>
    /// Validate an invoice XML for a specific CIF
    /// </summary>
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateInvoice([FromBody] ValidateInvoiceRequest request)
    {
        try
        {
            _logger.LogInformation("Validating invoice for CIF: {Cif}", request.Cif);

            var result = await _apiClient.ValidateInvoiceAsync(
                xmlContent: request.XmlContent,
                cif: request.Cif
            );

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice validation failed for CIF: {Cif}", request.Cif);
            return BadRequest($"Validation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Upload an invoice XML to SPV for a specific CIF
    /// </summary>
    [HttpPost("upload")]
    public async Task<IActionResult> UploadInvoice([FromBody] UploadInvoiceRequest request)
    {
        try
        {
            _logger.LogInformation("Uploading invoice for CIF: {Cif}, Environment: {Environment}", 
                request.Cif, request.Environment);

            var result = await _apiClient.UploadInvoiceXmlAsync(
                xmlContent: request.XmlContent,
                cif: request.Cif,
                environment: request.Environment
            );

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice upload failed for CIF: {Cif}", request.Cif);
            return BadRequest($"Upload failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Get messages/invoices for a specific CIF
    /// </summary>
    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages(
        [FromQuery] string cif,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        try
        {
            _logger.LogInformation("Getting messages for CIF: {Cif}, From: {From}, To: {To}", 
                cif, from, to);

            var result = await _apiClient.GetMessagesAsync(
                cif: cif,
                from: from,
                to: to
            );

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get messages for CIF: {Cif}", cif);
            return BadRequest($"Failed to get messages: {ex.Message}");
        }
    }

    /// <summary>
    /// Get upload status
    /// </summary>
    [HttpGet("upload/{uploadId}/status")]
    public async Task<IActionResult> GetUploadStatus(string uploadId)
    {
        try
        {
            _logger.LogInformation("Getting upload status for ID: {UploadId}", uploadId);

            var result = await _apiClient.GetUploadStatusAsync(uploadId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get upload status for ID: {UploadId}", uploadId);
            return BadRequest($"Failed to get upload status: {ex.Message}");
        }
    }

    /// <summary>
    /// Download an invoice by message ID
    /// </summary>
    [HttpGet("download/{messageId}")]
    public async Task<IActionResult> DownloadInvoice(string messageId)
    {
        try
        {
            _logger.LogInformation("Downloading invoice with ID: {MessageId}", messageId);

            var content = await _apiClient.DownloadInvoiceAsync(messageId);
            
            return File(content, "application/xml", $"invoice_{messageId}.xml");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download invoice with ID: {MessageId}", messageId);
            return BadRequest($"Download failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Convert XML to PDF
    /// </summary>
    [HttpPost("convert-to-pdf")]
    public async Task<IActionResult> ConvertToPdf([FromBody] ConvertToPdfRequest request)
    {
        try
        {
            _logger.LogInformation("Converting XML to PDF, DocumentType: {DocumentType}", request.DocumentType);

            var pdfContent = await _apiClient.ConvertXmlToPdfAsync(
                xmlContent: request.XmlContent,
                documentType: request.DocumentType
            );

            return File(pdfContent, "application/pdf", $"invoice_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert XML to PDF");
            return BadRequest($"PDF conversion failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Logout and clear stored token
    /// </summary>
    [HttpPost("auth/logout")]
    public async Task<IActionResult> Logout()
    {
        try
        {
            await _tokenStorage.RemoveTokenAsync(HttpContext);
            _logger.LogInformation("User logged out successfully");
            
            return Ok(new { Message = "Logged out successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to logout");
            return BadRequest($"Logout failed: {ex.Message}");
        }
    }
}

// Request DTOs
public class ValidateInvoiceRequest
{
    public string XmlContent { get; set; } = string.Empty;
    public string Cif { get; set; } = string.Empty;
}

public class UploadInvoiceRequest
{
    public string XmlContent { get; set; } = string.Empty;
    public string Cif { get; set; } = string.Empty;
    public string Environment { get; set; } = "prod";
}

public class ConvertToPdfRequest
{
    public string XmlContent { get; set; } = string.Empty;
    public string DocumentType { get; set; } = "FACT1";
}
