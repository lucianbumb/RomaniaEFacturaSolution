# Romania EFactura Library v2.0.0 - Complete Examples Documentation

This document provides comprehensive examples for all features of the Romania EFactura Library v2.0.0, including the new flexible token storage system and CIF parameter support.

## 📚 Table of Contents

1. [Quick Start Guide](#quick-start-guide)
2. [Authentication Examples](#authentication-examples)
3. [Invoice Operations Examples](#invoice-operations-examples)
4. [Invoice Management Examples](#invoice-management-examples)
5. [Token Storage Examples](#token-storage-examples)
6. [Complete Application Examples](#complete-application-examples)
7. [Migration from v1.x](#migration-from-v1x)

## 🚀 Quick Start Guide

### 1. Installation

```bash
dotnet add package RomaniaEFacturaLibrary --version 2.0.0
```

### 2. Basic Configuration

```json
{
  "EFactura": {
    "Environment": "Test",
    "ClientId": "your-anaf-client-id",
    "ClientSecret": "your-anaf-client-secret",
    "RedirectUri": "https://localhost:7000/api/auth/callback",
    "Scope": "efactura"
  }
}
```

### 3. Service Registration (Choose One)

#### Option A: MemoryCache Storage (Recommended for Development)
```csharp
// Program.cs
builder.Services.AddRomaniaEFacturaLibrary(builder.Configuration);
builder.Services.AddMemoryCacheTokenStorage();
```

#### Option B: Cookie Storage (Recommended for Web Applications)
```csharp
// Program.cs  
builder.Services.AddRomaniaEFacturaLibrary(builder.Configuration);
builder.Services.AddCookieTokenStorage();
```

#### Option C: Custom Storage (Enterprise/Production)
```csharp
// Program.cs
builder.Services.AddRomaniaEFacturaLibrary(builder.Configuration);
builder.Services.AddScoped<ITokenStorageService, YourCustomTokenStorage>();
```

## 🔐 Authentication Examples

### Complete OAuth2 Authentication Controller

```csharp
using Microsoft.AspNetCore.Mvc;
using RomaniaEFacturaLibrary.Services.Authentication;

[ApiController]
[Route("api/[controller]")]
public class AuthenticationController : ControllerBase
{
    private readonly IAuthenticationService _authService;
    private readonly ILogger<AuthenticationController> _logger;
    private readonly IConfiguration _configuration;

    public AuthenticationController(
        IAuthenticationService authService,
        ILogger<AuthenticationController> logger,
        IConfiguration configuration)
    {
        _authService = authService;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Initiates OAuth2 authorization flow with ANAF
    /// </summary>
    [HttpGet("authorize")]
    public async Task<IActionResult> Authorize()
    {
        try
        {
            _logger.LogInformation("Starting OAuth2 authorization flow");
            
            var authUrl = await _authService.GetAuthorizationUrlAsync();
            
            _logger.LogInformation("Redirecting to ANAF authorization URL");
            return Redirect(authUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate authorization");
            return BadRequest(new { Error = "Failed to initiate authorization", Details = ex.Message });
        }
    }

    /// <summary>
    /// Handles OAuth2 callback from ANAF
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string? state = null)
    {
        if (string.IsNullOrEmpty(code))
        {
            _logger.LogWarning("Authorization callback received without code");
            return BadRequest(new { Error = "Authorization code is required" });
        }

        try
        {
            _logger.LogInformation("Processing OAuth2 callback with code: {Code}", code[..8] + "...");
            
            var token = await _authService.GetAccessTokenAsync(code);
            
            _logger.LogInformation("Successfully obtained access token");
            
            // Token is automatically stored by the chosen storage service
            return Ok(new 
            { 
                Message = "Authentication successful",
                TokenType = token.TokenType,
                ExpiresIn = token.ExpiresIn,
                HasRefreshToken = !string.IsNullOrEmpty(token.RefreshToken)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process OAuth2 callback");
            return BadRequest(new { Error = "Authentication failed", Details = ex.Message });
        }
    }

    /// <summary>
    /// Gets current stored access token information
    /// </summary>
    [HttpGet("token")]
    public async Task<IActionResult> GetToken()
    {
        try
        {
            var token = await _authService.GetValidAccessTokenAsync();
            
            if (token == null)
            {
                return Ok(new { HasToken = false, Message = "No valid token found" });
            }

            return Ok(new 
            { 
                HasToken = true,
                TokenType = token.TokenType,
                ExpiresIn = token.ExpiresIn,
                ExpiresAt = token.ExpiresAt,
                IsExpired = token.IsExpired
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve token information");
            return BadRequest(new { Error = "Failed to retrieve token", Details = ex.Message });
        }
    }

    /// <summary>
    /// Manually refreshes the access token
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken()
    {
        try
        {
            _logger.LogInformation("Manually refreshing access token");
            
            var token = await _authService.GetValidAccessTokenAsync();
            
            _logger.LogInformation("Token refresh successful");
            return Ok(new { Message = "Token refreshed successfully", ExpiresIn = token?.ExpiresIn });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh token");
            return BadRequest(new { Error = "Token refresh failed", Details = ex.Message });
        }
    }

    /// <summary>
    /// Logs out and clears stored tokens
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        try
        {
            await _authService.ClearTokenAsync();
            _logger.LogInformation("User logged out successfully");
            
            return Ok(new { Message = "Logged out successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to logout");
            return BadRequest(new { Error = "Logout failed", Details = ex.Message });
        }
    }
}
```

## 📄 Invoice Operations Examples

### Complete Invoice Controller

```csharp
using Microsoft.AspNetCore.Mvc;
using RomaniaEFacturaLibrary.Services;
using System.ComponentModel.DataAnnotations;

[ApiController]
[Route("api/[controller]")]
public class InvoiceController : ControllerBase
{
    private readonly IEFacturaClient _eFacturaClient;
    private readonly ILogger<InvoiceController> _logger;

    public InvoiceController(IEFacturaClient eFacturaClient, ILogger<InvoiceController> logger)
    {
        _eFacturaClient = eFacturaClient;
        _logger = logger;
    }

    /// <summary>
    /// Validates a UBL 2.1 XML invoice before upload
    /// </summary>
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateInvoice([FromBody] ValidateInvoiceRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            _logger.LogInformation("Validating invoice for CIF: {Cif}", request.Cif);
            
            var result = await _eFacturaClient.ValidateInvoiceAsync(request.Cif, request.XmlContent);
            
            if (result.IsValid)
            {
                _logger.LogInformation("Invoice validation successful for CIF: {Cif}", request.Cif);
                return Ok(new 
                { 
                    IsValid = true, 
                    Message = "Invoice is valid",
                    ValidationDetails = result
                });
            }
            else
            {
                _logger.LogWarning("Invoice validation failed for CIF: {Cif}. Errors: {Errors}", 
                    request.Cif, string.Join(", ", result.Errors ?? new List<string>()));
                    
                return BadRequest(new 
                { 
                    IsValid = false,
                    Message = "Invoice validation failed",
                    Errors = result.Errors,
                    ValidationDetails = result
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating invoice for CIF: {Cif}", request.Cif);
            return StatusCode(500, new { Error = "Validation failed", Details = ex.Message });
        }
    }

    /// <summary>
    /// Uploads a validated invoice to ANAF SPV
    /// </summary>
    [HttpPost("upload")]
    public async Task<IActionResult> UploadInvoice([FromBody] UploadInvoiceRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            _logger.LogInformation("Uploading invoice for CIF: {Cif}", request.Cif);
            
            // Optional: Validate before upload
            if (request.ValidateBeforeUpload)
            {
                var validationResult = await _eFacturaClient.ValidateInvoiceAsync(request.Cif, request.XmlContent);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("Invoice validation failed before upload for CIF: {Cif}", request.Cif);
                    return BadRequest(new 
                    { 
                        Error = "Invoice validation failed", 
                        ValidationErrors = validationResult.Errors 
                    });
                }
            }
            
            var result = await _eFacturaClient.UploadInvoiceAsync(request.Cif, request.XmlContent);
            
            if (result.IsSuccess)
            {
                _logger.LogInformation("Invoice uploaded successfully for CIF: {Cif}. Upload ID: {UploadId}", 
                    request.Cif, result.UploadIndex);
                    
                return Ok(new 
                { 
                    Success = true,
                    Message = "Invoice uploaded successfully",
                    UploadId = result.UploadIndex,
                    UploadDetails = result
                });
            }
            else
            {
                _logger.LogWarning("Invoice upload failed for CIF: {Cif}. Errors: {Errors}", 
                    request.Cif, string.Join(", ", result.Errors ?? new List<string>()));
                    
                return BadRequest(new 
                { 
                    Success = false,
                    Message = "Invoice upload failed",
                    Errors = result.Errors,
                    UploadDetails = result
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading invoice for CIF: {Cif}", request.Cif);
            return StatusCode(500, new { Error = "Upload failed", Details = ex.Message });
        }
    }

    /// <summary>
    /// Gets the upload status of a previously uploaded invoice
    /// </summary>
    [HttpGet("status/{uploadId}")]
    public async Task<IActionResult> GetUploadStatus(string uploadId, [FromQuery] string cif)
    {
        if (string.IsNullOrEmpty(cif))
            return BadRequest(new { Error = "CIF is required" });

        try
        {
            _logger.LogInformation("Checking upload status for Upload ID: {UploadId}, CIF: {Cif}", uploadId, cif);
            
            // This would typically involve checking the messages or invoice list
            var recentInvoices = await _eFacturaClient.GetInvoicesAsync(cif, days: 1);
            
            // Find the invoice by upload ID (this is a simplified example)
            var uploadedInvoice = recentInvoices?.FirstOrDefault(inv => inv.Id == uploadId);
            
            if (uploadedInvoice != null)
            {
                return Ok(new 
                { 
                    UploadId = uploadId,
                    Status = "Found",
                    InvoiceDetails = uploadedInvoice
                });
            }
            else
            {
                return Ok(new 
                { 
                    UploadId = uploadId,
                    Status = "Not Found or Still Processing",
                    Message = "Invoice may still be processing or upload ID is invalid"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking upload status for Upload ID: {UploadId}", uploadId);
            return StatusCode(500, new { Error = "Status check failed", Details = ex.Message });
        }
    }
}

// Request Models
public class ValidateInvoiceRequest
{
    [Required]
    [StringLength(10, MinimumLength = 8)]
    public string Cif { get; set; } = string.Empty;
    
    [Required]
    [MinLength(100)]
    public string XmlContent { get; set; } = string.Empty;
}

public class UploadInvoiceRequest
{
    [Required]
    [StringLength(10, MinimumLength = 8)]
    public string Cif { get; set; } = string.Empty;
    
    [Required]
    [MinLength(100)]
    public string XmlContent { get; set; } = string.Empty;
    
    public bool ValidateBeforeUpload { get; set; } = true;
}
```

## 📋 Invoice Management Examples

### Complete Invoice Management Controller

```csharp
using Microsoft.AspNetCore.Mvc;
using RomaniaEFacturaLibrary.Services;
using System.ComponentModel.DataAnnotations;

[ApiController]
[Route("api/[controller]")]
public class InvoiceManagementController : ControllerBase
{
    private readonly IEFacturaClient _eFacturaClient;
    private readonly ILogger<InvoiceManagementController> _logger;

    public InvoiceManagementController(IEFacturaClient eFacturaClient, ILogger<InvoiceManagementController> logger)
    {
        _eFacturaClient = eFacturaClient;
        _logger = logger;
    }

    /// <summary>
    /// Lists invoices for a specific CIF with filtering options
    /// </summary>
    [HttpGet("list")]
    public async Task<IActionResult> GetInvoices([FromQuery] GetInvoicesRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            _logger.LogInformation("Retrieving invoices for CIF: {Cif}, Days: {Days}", request.Cif, request.Days);
            
            var invoices = await _eFacturaClient.GetInvoicesAsync(request.Cif, request.Days);
            
            if (invoices != null && invoices.Any())
            {
                _logger.LogInformation("Found {Count} invoices for CIF: {Cif}", invoices.Count(), request.Cif);
                
                return Ok(new 
                { 
                    Cif = request.Cif,
                    Days = request.Days,
                    TotalCount = invoices.Count(),
                    Invoices = invoices.Select(inv => new 
                    {
                        inv.Id,
                        inv.CreatedDate,
                        inv.Type,
                        inv.Details
                    })
                });
            }
            else
            {
                _logger.LogInformation("No invoices found for CIF: {Cif}", request.Cif);
                return Ok(new 
                { 
                    Cif = request.Cif,
                    Days = request.Days,
                    TotalCount = 0,
                    Message = "No invoices found for the specified period",
                    Invoices = new object[0]
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving invoices for CIF: {Cif}", request.Cif);
            return StatusCode(500, new { Error = "Failed to retrieve invoices", Details = ex.Message });
        }
    }

    /// <summary>
    /// Downloads a specific invoice by message ID in XML format
    /// </summary>
    [HttpGet("{messageId}/download")]
    public async Task<IActionResult> DownloadInvoiceXml(string messageId)
    {
        if (string.IsNullOrEmpty(messageId))
            return BadRequest(new { Error = "Message ID is required" });

        try
        {
            _logger.LogInformation("Downloading invoice XML for Message ID: {MessageId}", messageId);
            
            var invoice = await _eFacturaClient.DownloadInvoiceAsync(messageId);
            
            if (invoice != null)
            {
                _logger.LogInformation("Successfully downloaded invoice for Message ID: {MessageId}", messageId);
                
                return File(
                    System.Text.Encoding.UTF8.GetBytes(invoice),
                    "application/xml",
                    $"invoice_{messageId}.xml"
                );
            }
            else
            {
                _logger.LogWarning("Invoice not found for Message ID: {MessageId}", messageId);
                return NotFound(new { Error = "Invoice not found", MessageId = messageId });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading invoice for Message ID: {MessageId}", messageId);
            return StatusCode(500, new { Error = "Download failed", Details = ex.Message });
        }
    }

    /// <summary>
    /// Downloads a specific invoice by message ID in PDF format
    /// </summary>
    [HttpGet("{messageId}/pdf")]
    public async Task<IActionResult> DownloadInvoicePdf(string messageId)
    {
        if (string.IsNullOrEmpty(messageId))
            return BadRequest(new { Error = "Message ID is required" });

        try
        {
            _logger.LogInformation("Downloading invoice PDF for Message ID: {MessageId}", messageId);
            
            var pdfBytes = await _eFacturaClient.DownloadInvoicePdfAsync(messageId);
            
            if (pdfBytes != null && pdfBytes.Length > 0)
            {
                _logger.LogInformation("Successfully downloaded PDF for Message ID: {MessageId}, Size: {Size} bytes", 
                    messageId, pdfBytes.Length);
                
                return File(
                    pdfBytes,
                    "application/pdf",
                    $"invoice_{messageId}.pdf"
                );
            }
            else
            {
                _logger.LogWarning("PDF not found or empty for Message ID: {MessageId}", messageId);
                return NotFound(new { Error = "PDF not found or not available", MessageId = messageId });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading PDF for Message ID: {MessageId}", messageId);
            return StatusCode(500, new { Error = "PDF download failed", Details = ex.Message });
        }
    }

    /// <summary>
    /// Retrieves ANAF messages for a specific CIF
    /// </summary>
    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages([FromQuery] GetMessagesRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            _logger.LogInformation("Retrieving messages for CIF: {Cif}, Days: {Days}", request.Cif, request.Days);
            
            var messages = await _eFacturaClient.GetMessagesAsync(request.Cif, request.Days);
            
            if (messages != null && messages.Any())
            {
                _logger.LogInformation("Found {Count} messages for CIF: {Cif}", messages.Count(), request.Cif);
                
                return Ok(new 
                { 
                    Cif = request.Cif,
                    Days = request.Days,
                    TotalCount = messages.Count(),
                    Messages = messages.Select(msg => new 
                    {
                        msg.Id,
                        msg.CreatedDate,
                        msg.Type,
                        msg.Details
                    })
                });
            }
            else
            {
                _logger.LogInformation("No messages found for CIF: {Cif}", request.Cif);
                return Ok(new 
                { 
                    Cif = request.Cif,
                    Days = request.Days,
                    TotalCount = 0,
                    Message = "No messages found for the specified period",
                    Messages = new object[0]
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving messages for CIF: {Cif}", request.Cif);
            return StatusCode(500, new { Error = "Failed to retrieve messages", Details = ex.Message });
        }
    }

    /// <summary>
    /// Advanced search for invoices with multiple criteria
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> SearchInvoices([FromQuery] SearchInvoicesRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            _logger.LogInformation("Searching invoices for CIF: {Cif} with criteria", request.Cif);
            
            // Get all invoices for the period
            var allInvoices = await _eFacturaClient.GetInvoicesAsync(request.Cif, request.Days);
            
            if (allInvoices == null || !allInvoices.Any())
            {
                return Ok(new 
                { 
                    Cif = request.Cif,
                    TotalCount = 0,
                    FilteredCount = 0,
                    SearchCriteria = request,
                    Invoices = new object[0]
                });
            }

            // Apply filters (this is a simplified example - you'd implement actual filtering)
            var filteredInvoices = allInvoices.AsEnumerable();
            
            if (!string.IsNullOrEmpty(request.InvoiceType))
            {
                filteredInvoices = filteredInvoices.Where(inv => 
                    inv.Type?.Contains(request.InvoiceType, StringComparison.OrdinalIgnoreCase) == true);
            }
            
            var results = filteredInvoices.ToList();
            
            _logger.LogInformation("Search returned {Count} invoices out of {Total} for CIF: {Cif}", 
                results.Count, allInvoices.Count(), request.Cif);
            
            return Ok(new 
            { 
                Cif = request.Cif,
                TotalCount = allInvoices.Count(),
                FilteredCount = results.Count,
                SearchCriteria = request,
                Invoices = results.Select(inv => new 
                {
                    inv.Id,
                    inv.CreatedDate,
                    inv.Type,
                    inv.Details
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching invoices for CIF: {Cif}", request.Cif);
            return StatusCode(500, new { Error = "Search failed", Details = ex.Message });
        }
    }
}

// Request Models for Invoice Management
public class GetInvoicesRequest
{
    [Required]
    [StringLength(10, MinimumLength = 8)]
    public string Cif { get; set; } = string.Empty;
    
    [Range(1, 365)]
    public int Days { get; set; } = 30;
}

public class GetMessagesRequest
{
    [Required]
    [StringLength(10, MinimumLength = 8)]
    public string Cif { get; set; } = string.Empty;
    
    [Range(1, 365)]
    public int Days { get; set; } = 7;
}

public class SearchInvoicesRequest
{
    [Required]
    [StringLength(10, MinimumLength = 8)]
    public string Cif { get; set; } = string.Empty;
    
    [Range(1, 365)]
    public int Days { get; set; } = 30;
    
    public string? InvoiceType { get; set; }
    
    public DateTime? FromDate { get; set; }
    
    public DateTime? ToDate { get; set; }
}
```

## 🔄 Token Storage Examples

### Custom Token Storage Implementation

```csharp
using Microsoft.EntityFrameworkCore;
using RomaniaEFacturaLibrary.Services.TokenStorage;
using RomaniaEFacturaLibrary.Models.Authentication;

// Database Entity
public class StoredToken
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public string TokenType { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// Database Context
public class EFacturaDbContext : DbContext
{
    public DbSet<StoredToken> Tokens { get; set; }
    
    public EFacturaDbContext(DbContextOptions<EFacturaDbContext> options) : base(options) { }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.Property(e => e.AccessToken).IsRequired();
            entity.Property(e => e.TokenType).IsRequired();
        });
    }
}

// Custom Database Token Storage Service
public class DatabaseTokenStorageService : ITokenStorageService
{
    private readonly EFacturaDbContext _context;
    private readonly ILogger<DatabaseTokenStorageService> _logger;

    public DatabaseTokenStorageService(EFacturaDbContext context, ILogger<DatabaseTokenStorageService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<TokenDto?> GetTokenAsync(HttpContext httpContext)
    {
        var userId = GetUserId(httpContext);
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Cannot get token: User ID not found in HttpContext");
            return null;
        }

        try
        {
            var storedToken = await _context.Tokens
                .FirstOrDefaultAsync(t => t.UserId == userId);

            if (storedToken == null)
            {
                _logger.LogInformation("No token found for user: {UserId}", userId);
                return null;
            }

            var tokenDto = new TokenDto
            {
                AccessToken = storedToken.AccessToken,
                RefreshToken = storedToken.RefreshToken,
                TokenType = storedToken.TokenType,
                ExpiresAt = storedToken.ExpiresAt
            };

            _logger.LogInformation("Retrieved token for user: {UserId}, Expires: {ExpiresAt}", 
                userId, tokenDto.ExpiresAt);
                
            return tokenDto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving token for user: {UserId}", userId);
            throw;
        }
    }

    public async Task SetTokenAsync(HttpContext httpContext, TokenDto token)
    {
        var userId = GetUserId(httpContext);
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogError("Cannot set token: User ID not found in HttpContext");
            throw new InvalidOperationException("User ID not found in HttpContext");
        }

        try
        {
            var existingToken = await _context.Tokens
                .FirstOrDefaultAsync(t => t.UserId == userId);

            if (existingToken != null)
            {
                // Update existing token
                existingToken.AccessToken = token.AccessToken;
                existingToken.RefreshToken = token.RefreshToken;
                existingToken.TokenType = token.TokenType;
                existingToken.ExpiresAt = token.ExpiresAt;
                existingToken.UpdatedAt = DateTime.UtcNow;
                
                _context.Tokens.Update(existingToken);
                _logger.LogInformation("Updated existing token for user: {UserId}", userId);
            }
            else
            {
                // Create new token
                var newToken = new StoredToken
                {
                    UserId = userId,
                    AccessToken = token.AccessToken,
                    RefreshToken = token.RefreshToken,
                    TokenType = token.TokenType,
                    ExpiresAt = token.ExpiresAt,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                
                _context.Tokens.Add(newToken);
                _logger.LogInformation("Created new token for user: {UserId}", userId);
            }

            await _context.SaveChangesAsync();
            _logger.LogInformation("Token saved successfully for user: {UserId}, Expires: {ExpiresAt}", 
                userId, token.ExpiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving token for user: {UserId}", userId);
            throw;
        }
    }

    public async Task RemoveTokenAsync(HttpContext httpContext)
    {
        var userId = GetUserId(httpContext);
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Cannot remove token: User ID not found in HttpContext");
            return;
        }

        try
        {
            var existingToken = await _context.Tokens
                .FirstOrDefaultAsync(t => t.UserId == userId);

            if (existingToken != null)
            {
                _context.Tokens.Remove(existingToken);
                await _context.SaveChangesAsync();
                
                _logger.LogInformation("Token removed successfully for user: {UserId}", userId);
            }
            else
            {
                _logger.LogInformation("No token found to remove for user: {UserId}", userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing token for user: {UserId}", userId);
            throw;
        }
    }

    public async Task<bool> HasValidTokenAsync(HttpContext httpContext)
    {
        var token = await GetTokenAsync(httpContext);
        return token != null && !token.IsExpired;
    }

    private string? GetUserId(HttpContext httpContext)
    {
        // Try multiple claim types for user identification
        var claimTypes = new[]
        {
            "sub", "id", "user_id", "userId", "name", "preferred_username", "email"
        };

        foreach (var claimType in claimTypes)
        {
            var claim = httpContext.User?.FindFirst(claimType);
            if (!string.IsNullOrEmpty(claim?.Value))
            {
                return claim.Value;
            }
        }

        // Fallback to session ID if no user claims found
        return httpContext.Session?.Id;
    }
}

// Registration in Program.cs
builder.Services.AddDbContext<EFacturaDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddRomaniaEFacturaLibrary(builder.Configuration);
builder.Services.AddScoped<ITokenStorageService, DatabaseTokenStorageService>();
```

## 🔄 Migration from v1.x

### Key Changes Summary

| Aspect | v1.x | v2.0.0 |
|--------|------|--------|
| CIF Usage | Configuration-based | Parameter-based |
| Token Storage | Manual management | Automatic with flexible options |
| API Access | `EFacturaApiClient` public | `EFacturaApiClient` internal, use `IEFacturaClient` |
| Service Registration | Basic registration | Choose storage method |

### Migration Steps

#### 1. Update Package Reference
```xml
<!-- Before -->
<PackageReference Include="RomaniaEFacturaLibrary" Version="1.0.1" />

<!-- After -->
<PackageReference Include="RomaniaEFacturaLibrary" Version="2.0.0" />
```

#### 2. Update Service Registration
```csharp
// Before (v1.x)
builder.Services.AddEFacturaServices(builder.Configuration);

// After (v2.0.0) - Choose storage method
builder.Services.AddRomaniaEFacturaLibrary(builder.Configuration);
builder.Services.AddMemoryCacheTokenStorage(); // or AddCookieTokenStorage()
```

#### 3. Update Method Calls
```csharp
// Before (v1.x)
public async Task<IActionResult> ValidateInvoice(string xmlContent)
{
    var result = await _client.ValidateInvoiceAsync(xmlContent);
    return Ok(result);
}

// After (v2.0.0)
public async Task<IActionResult> ValidateInvoice(ValidateInvoiceRequest request)
{
    var result = await _client.ValidateInvoiceAsync(request.Cif, request.XmlContent);
    return Ok(result);
}
```

#### 4. Update Dependency Injection
```csharp
// Before (v1.x)
public class InvoiceService
{
    private readonly EFacturaApiClient _apiClient;
    
    public InvoiceService(EFacturaApiClient apiClient) // Direct dependency
    {
        _apiClient = apiClient;
    }
}

// After (v2.0.0)  
public class InvoiceService
{
    private readonly IEFacturaClient _eFacturaClient;
    
    public InvoiceService(IEFacturaClient eFacturaClient) // Interface dependency
    {
        _eFacturaClient = eFacturaClient;
    }
}
```

#### 5. Remove Manual Token Management
```csharp
// Before (v1.x) - Manual token handling
public async Task<IActionResult> CallApi()
{
    var token = await _authService.GetTokenAsync();
    _apiClient.SetToken(token); // Manual token setting
    var result = await _apiClient.SomeOperation();
    return Ok(result);
}

// After (v2.0.0) - Automatic token handling
public async Task<IActionResult> CallApi(string cif)
{
    // No manual token management needed - library handles it automatically
    var result = await _eFacturaClient.SomeOperation(cif);
    return Ok(result);
}
```

## 🎯 Complete Application Example

### Program.cs - Complete Setup
```csharp
using RomaniaEFacturaLibrary.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Romania EFactura Library v2.0.0
builder.Services.AddRomaniaEFacturaLibrary(builder.Configuration);

// Choose token storage method (pick one):

// Option 1: MemoryCache (good for development/single instance)
builder.Services.AddMemoryCacheTokenStorage();

// Option 2: Cookie (good for web apps/load balanced)
// builder.Services.AddCookieTokenStorage();

// Option 3: Custom storage (good for enterprise/production)
// builder.Services.AddDbContext<EFacturaDbContext>(...);
// builder.Services.AddScoped<ITokenStorageService, DatabaseTokenStorageService>();

// Add logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
});

// Add session support (required for some token storage options)
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseSession(); // Enable session support
app.UseAuthentication(); // If using authentication
app.UseAuthorization();
app.MapControllers();

app.Run();
```

This comprehensive documentation provides all the examples you need to successfully implement and use the Romania EFactura Library v2.0.0 in your applications. Each example includes proper error handling, logging, and follows best practices for production use.
