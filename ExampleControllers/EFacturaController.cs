using Microsoft.AspNetCore.Mvc;
using RomaniaEFacturaLibrary.Services;
using RomaniaEFacturaLibrary.Services.Authentication;
using RomaniaEFacturaLibrary.Models.Ubl;
using RomaniaEFacturaLibrary.Models.Api;
using System.ComponentModel.DataAnnotations;

namespace RomaniaEFacturaLibrary.Examples.Controllers;

/// <summary>
/// Controller demonstrating EFactura invoice operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class EFacturaController : ControllerBase
{
    private readonly IEFacturaClient _eFacturaClient;
    private readonly IAuthenticationService _authService;
    private readonly ILogger<EFacturaController> _logger;

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
    /// Dashboard showing authentication status and quick stats
    /// </summary>
    /// <returns>Dashboard information</returns>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard()
    {
        try
        {
            // Check authentication status
            bool isAuthenticated;
            try
            {
                await _authService.GetValidAccessTokenAsync();
                isAuthenticated = true;
            }
            catch (AuthenticationException)
            {
                isAuthenticated = false;
            }

            if (!isAuthenticated)
            {
                return Ok(new
                {
                    isAuthenticated = false,
                    message = "Please authenticate first",
                    loginUrl = Url.Action("Login", "Auth")
                });
            }

            // Get recent invoices count
            var recentInvoices = await _eFacturaClient.GetInvoicesAsync(
                DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);

            return Ok(new
            {
                isAuthenticated = true,
                recentInvoicesCount = recentInvoices.Count,
                lastChecked = DateTime.UtcNow,
                availableOperations = new[]
                {
                    "Upload Invoice",
                    "Download Invoices",
                    "Validate Invoice",
                    "Check Upload Status",
                    "Convert to PDF"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading dashboard");
            return StatusCode(500, new { error = "Failed to load dashboard" });
        }
    }

    /// <summary>
    /// Validates an invoice without uploading it
    /// </summary>
    /// <param name="invoice">UBL invoice to validate</param>
    /// <returns>Validation results</returns>
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateInvoice([FromBody] UblInvoice invoice)
    {
        try
        {
            if (invoice == null)
            {
                return BadRequest(new { error = "Invoice data is required" });
            }

            // Ensure user is authenticated
            await _authService.GetValidAccessTokenAsync();

            _logger.LogInformation("Validating invoice {InvoiceId}", invoice.Id);

            var validation = await _eFacturaClient.ValidateInvoiceAsync(invoice);

            var response = new
            {
                invoiceId = invoice.Id,
                isValid = validation.Success,
                errors = validation.Errors?.Select(e => new
                {
                    message = e.Message,
                    severity = "Error"
                }) ?? Array.Empty<object>(),
                validatedAt = DateTime.UtcNow
            };

            if (validation.Success)
            {
                _logger.LogInformation("Invoice {InvoiceId} validation successful", invoice.Id);
            }
            else
            {
                _logger.LogWarning("Invoice {InvoiceId} validation failed with {ErrorCount} errors", 
                    invoice.Id, validation.Errors?.Count ?? 0);
            }

            return Ok(response);
        }
        catch (AuthenticationException)
        {
            return Unauthorized(new
            {
                error = "Authentication required",
                loginUrl = Url.Action("Login", "Auth")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating invoice");
            return StatusCode(500, new { error = "Validation failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Uploads an invoice to ANAF SPV
    /// </summary>
    /// <param name="invoice">UBL invoice to upload</param>
    /// <returns>Upload response with tracking ID</returns>
    [HttpPost("upload")]
    public async Task<IActionResult> UploadInvoice([FromBody] UblInvoice invoice)
    {
        try
        {
            if (invoice == null)
            {
                return BadRequest(new { error = "Invoice data is required" });
            }

            // Ensure user is authenticated
            await _authService.GetValidAccessTokenAsync();

            _logger.LogInformation("Uploading invoice {InvoiceId}", invoice.Id);

            var uploadResponse = await _eFacturaClient.UploadInvoiceAsync(invoice);

            _logger.LogInformation("Invoice {InvoiceId} uploaded successfully with ID {UploadId}", 
                invoice.Id, uploadResponse.UploadId);

            return Ok(new
            {
                success = true,
                uploadId = uploadResponse.UploadId,
                invoiceId = invoice.Id,
                message = "Invoice uploaded successfully",
                uploadedAt = DateTime.UtcNow,
                statusCheckUrl = Url.Action("GetUploadStatus", new { uploadId = uploadResponse.UploadId })
            });
        }
        catch (AuthenticationException)
        {
            return Unauthorized(new
            {
                error = "Authentication required",
                loginUrl = Url.Action("Login", "Auth")
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("validation"))
        {
            return BadRequest(new
            {
                error = "Invoice validation failed",
                details = ex.Message,
                suggestion = "Please validate the invoice first using the /validate endpoint"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading invoice {InvoiceId}", invoice?.Id);
            return StatusCode(500, new { error = "Upload failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Gets the status of an uploaded invoice
    /// </summary>
    /// <param name="uploadId">Upload tracking ID</param>
    /// <returns>Current upload status</returns>
    [HttpGet("upload/{uploadId}/status")]
    public async Task<IActionResult> GetUploadStatus([Required] string uploadId)
    {
        try
        {
            if (string.IsNullOrEmpty(uploadId))
            {
                return BadRequest(new { error = "Upload ID is required" });
            }

            // Ensure user is authenticated
            await _authService.GetValidAccessTokenAsync();

            _logger.LogInformation("Checking status for upload {UploadId}", uploadId);

            var status = await _eFacturaClient.GetUploadStatusAsync(uploadId);

            return Ok(new
            {
                uploadId,
                status = status.Status,
                message = status.Message,
                isComplete = IsUploadComplete(status.Status),
                checkedAt = DateTime.UtcNow,
                details = status
            });
        }
        catch (AuthenticationException)
        {
            return Unauthorized(new
            {
                error = "Authentication required",
                loginUrl = Url.Action("Login", "Auth")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking upload status for {UploadId}", uploadId);
            return StatusCode(500, new { error = "Failed to check status", details = ex.Message });
        }
    }

    /// <summary>
    /// Waits for an upload to complete and returns final status
    /// </summary>
    /// <param name="uploadId">Upload tracking ID</param>
    /// <param name="timeoutMinutes">Maximum time to wait in minutes (default: 10)</param>
    /// <returns>Final upload status</returns>
    [HttpPost("upload/{uploadId}/wait")]
    public async Task<IActionResult> WaitForUploadCompletion(
        [Required] string uploadId,
        [FromQuery] int timeoutMinutes = 10)
    {
        try
        {
            if (string.IsNullOrEmpty(uploadId))
            {
                return BadRequest(new { error = "Upload ID is required" });
            }

            if (timeoutMinutes < 1 || timeoutMinutes > 30)
            {
                return BadRequest(new { error = "Timeout must be between 1 and 30 minutes" });
            }

            // Ensure user is authenticated
            await _authService.GetValidAccessTokenAsync();

            _logger.LogInformation("Waiting for upload {UploadId} to complete (timeout: {Timeout} minutes)", 
                uploadId, timeoutMinutes);

            var timeout = TimeSpan.FromMinutes(timeoutMinutes);
            var finalStatus = await _eFacturaClient.WaitForUploadCompletionAsync(uploadId, timeout);

            return Ok(new
            {
                uploadId,
                finalStatus = finalStatus.Status,
                message = finalStatus.Message,
                isComplete = IsUploadComplete(finalStatus.Status),
                completedAt = DateTime.UtcNow,
                details = finalStatus
            });
        }
        catch (AuthenticationException)
        {
            return Unauthorized(new
            {
                error = "Authentication required",
                loginUrl = Url.Action("Login", "Auth")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error waiting for upload completion {UploadId}", uploadId);
            return StatusCode(500, new { error = "Failed to wait for completion", details = ex.Message });
        }
    }

    /// <summary>
    /// Gets list of invoices for a date range
    /// </summary>
    /// <param name="from">Start date (optional, defaults to 30 days ago)</param>
    /// <param name="to">End date (optional, defaults to today)</param>
    /// <returns>List of invoices</returns>
    [HttpGet("invoices")]
    public async Task<IActionResult> GetInvoices(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        try
        {
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

            // Ensure user is authenticated
            await _authService.GetValidAccessTokenAsync();

            _logger.LogInformation("Getting invoices from {From} to {To}", from, to);

            var invoices = await _eFacturaClient.GetInvoicesAsync(from, to);

            return Ok(new
            {
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
                    downloadUrl = Url.Action("DownloadInvoice", new { messageId = inv.Id })
                }),
                retrievedAt = DateTime.UtcNow
            });
        }
        catch (AuthenticationException)
        {
            return Unauthorized(new
            {
                error = "Authentication required",
                loginUrl = Url.Action("Login", "Auth")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting invoices for date range {From} to {To}", from, to);
            return StatusCode(500, new { error = "Failed to get invoices", details = ex.Message });
        }
    }

    /// <summary>
    /// Downloads a specific invoice by message ID
    /// </summary>
    /// <param name="messageId">Invoice message ID</param>
    /// <returns>Invoice data or file download</returns>
    [HttpGet("invoices/{messageId}")]
    public async Task<IActionResult> DownloadInvoice([Required] string messageId)
    {
        try
        {
            if (string.IsNullOrEmpty(messageId))
            {
                return BadRequest(new { error = "Message ID is required" });
            }

            // Ensure user is authenticated
            await _authService.GetValidAccessTokenAsync();

            _logger.LogInformation("Downloading invoice {MessageId}", messageId);

            var invoice = await _eFacturaClient.DownloadInvoiceAsync(messageId);

            return Ok(new
            {
                messageId,
                invoice = new
                {
                    id = invoice.Id,
                    issueDate = invoice.IssueDate,
                    supplier = invoice.AccountingSupplierParty?.PartyLegalEntity?.RegistrationName,
                    customer = invoice.AccountingCustomerParty?.PartyLegalEntity?.RegistrationName,
                    totalAmount = invoice.LegalMonetaryTotal?.TaxInclusiveAmount?.Value,
                    currency = invoice.DocumentCurrencyCode,
                    lines = invoice.InvoiceLines?.Count ?? 0
                },
                downloadedAt = DateTime.UtcNow,
                pdfUrl = Url.Action("ConvertToPdf", new { messageId })
            });
        }
        catch (AuthenticationException)
        {
            return Unauthorized(new
            {
                error = "Authentication required",
                loginUrl = Url.Action("Login", "Auth")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading invoice {MessageId}", messageId);
            return StatusCode(500, new { error = "Failed to download invoice", details = ex.Message });
        }
    }

    /// <summary>
    /// Downloads raw invoice data (ZIP file)
    /// </summary>
    /// <param name="messageId">Invoice message ID</param>
    /// <returns>ZIP file download</returns>
    [HttpGet("invoices/{messageId}/raw")]
    public async Task<IActionResult> DownloadRawInvoice([Required] string messageId)
    {
        try
        {
            if (string.IsNullOrEmpty(messageId))
            {
                return BadRequest(new { error = "Message ID is required" });
            }

            // Ensure user is authenticated
            await _authService.GetValidAccessTokenAsync();

            _logger.LogInformation("Downloading raw invoice data {MessageId}", messageId);

            var rawData = await _eFacturaClient.DownloadRawInvoiceAsync(messageId);

            return File(rawData, "application/zip", $"invoice_{messageId}.zip");
        }
        catch (AuthenticationException)
        {
            return Unauthorized(new
            {
                error = "Authentication required",
                loginUrl = Url.Action("Login", "Auth")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading raw invoice {MessageId}", messageId);
            return StatusCode(500, new { error = "Failed to download raw invoice", details = ex.Message });
        }
    }

    /// <summary>
    /// Converts an invoice to PDF
    /// </summary>
    /// <param name="messageId">Invoice message ID</param>
    /// <returns>PDF file download</returns>
    [HttpGet("invoices/{messageId}/pdf")]
    public async Task<IActionResult> ConvertToPdf([Required] string messageId)
    {
        try
        {
            if (string.IsNullOrEmpty(messageId))
            {
                return BadRequest(new { error = "Message ID is required" });
            }

            // Ensure user is authenticated
            await _authService.GetValidAccessTokenAsync();

            _logger.LogInformation("Converting invoice {MessageId} to PDF", messageId);

            // First download the invoice
            var invoice = await _eFacturaClient.DownloadInvoiceAsync(messageId);
            
            // Then convert to PDF
            var pdfData = await _eFacturaClient.ConvertToPdfAsync(invoice);

            return File(pdfData, "application/pdf", $"invoice_{messageId}.pdf");
        }
        catch (AuthenticationException)
        {
            return Unauthorized(new
            {
                error = "Authentication required",
                loginUrl = Url.Action("Login", "Auth")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting invoice {MessageId} to PDF", messageId);
            return StatusCode(500, new { error = "Failed to convert to PDF", details = ex.Message });
        }
    }

    private static bool IsUploadComplete(string status)
    {
        var completedStatuses = new[] { "procesat", "finalizat", "respins", "eroare", "completed", "processed", "rejected", "error" };
        return completedStatuses.Any(s => status.Contains(s, StringComparison.OrdinalIgnoreCase));
    }
}