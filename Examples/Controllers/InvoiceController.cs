using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RomaniaEFacturaLibrary.Models.Ubl;
using RomaniaEFacturaLibrary.Services;
using System.ComponentModel.DataAnnotations;

namespace RomaniaEFacturaLibrary.Examples.Controllers;

/// <summary>
/// Controller for invoice operations (validation, upload, status checking)
/// </summary>
[ApiController]
[Route("api/efactura/[controller]")]
[Authorize]
public class InvoiceController : ControllerBase
{
    private readonly IEFacturaClient _eFacturaClient;
    private readonly ILogger<InvoiceController> _logger;

    public InvoiceController(
        IEFacturaClient eFacturaClient,
        ILogger<InvoiceController> logger)
    {
        _eFacturaClient = eFacturaClient;
        _logger = logger;
    }

    /// <summary>
    /// Validates an invoice XML without uploading it
    /// </summary>
    /// <param name="request">Invoice validation request</param>
    /// <returns>Validation result</returns>
    [HttpPost("validate")]
    [ProducesResponseType(typeof(InvoiceValidationResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> ValidateInvoice([FromBody] InvoiceValidationRequest request)
    {
        try
        {
            _logger.LogInformation("Validating invoice for CIF: {Cif}", request.Cif);

            var result = await _eFacturaClient.ValidateInvoiceAsync(request.Invoice, request.Cif);

            var response = new InvoiceValidationResponse
            {
                Success = result.Success,
                Cif = request.Cif,
                InvoiceId = request.Invoice.Id,
                Errors = result.Errors?.Select(e => e.Message).ToList() ?? new List<string>(),
                Warnings = result.Warnings?.Select(w => w.Message).ToList() ?? new List<string>(),
                ValidatedAt = DateTime.UtcNow
            };

            if (result.Success)
            {
                _logger.LogInformation("Invoice validation successful for CIF: {Cif}, Invoice: {InvoiceId}", 
                    request.Cif, request.Invoice.Id);
            }
            else
            {
                _logger.LogWarning("Invoice validation failed for CIF: {Cif}, Invoice: {InvoiceId}. Errors: {ErrorCount}", 
                    request.Cif, request.Invoice.Id, result.Errors?.Count ?? 0);
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice validation failed for CIF: {Cif}", request.Cif);
            return BadRequest(new ErrorResponse
            {
                Error = "validation_failed",
                ErrorDescription = $"Invoice validation failed: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Uploads an invoice to SPV after validation
    /// </summary>
    /// <param name="request">Invoice upload request</param>
    /// <returns>Upload result with upload ID</returns>
    [HttpPost("upload")]
    [ProducesResponseType(typeof(InvoiceUploadResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> UploadInvoice([FromBody] InvoiceUploadRequest request)
    {
        try
        {
            _logger.LogInformation("Uploading invoice for CIF: {Cif}, Environment: {Environment}, Invoice: {InvoiceId}", 
                request.Cif, request.Environment, request.Invoice.Id);

            var result = await _eFacturaClient.UploadInvoiceAsync(request.Invoice, request.Cif, request.Environment);

            var response = new InvoiceUploadResponse
            {
                Success = result.Success,
                UploadId = result.UploadId,
                Cif = request.Cif,
                InvoiceId = request.Invoice.Id,
                Environment = request.Environment,
                UploadedAt = DateTime.UtcNow,
                Message = result.Message
            };

            _logger.LogInformation("Invoice upload successful for CIF: {Cif}, Upload ID: {UploadId}", 
                request.Cif, result.UploadId);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice upload failed for CIF: {Cif}, Invoice: {InvoiceId}", 
                request.Cif, request.Invoice.Id);
            return BadRequest(new ErrorResponse
            {
                Error = "upload_failed",
                ErrorDescription = $"Invoice upload failed: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Gets the upload status of a previously uploaded invoice
    /// </summary>
    /// <param name="uploadId">Upload ID returned from upload operation</param>
    /// <returns>Current upload status</returns>
    [HttpGet("upload/{uploadId}/status")]
    [ProducesResponseType(typeof(UploadStatusResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetUploadStatus(string uploadId)
    {
        try
        {
            _logger.LogInformation("Getting upload status for ID: {UploadId}", uploadId);

            var result = await _eFacturaClient.GetUploadStatusAsync(uploadId);

            var response = new UploadStatusResponse
            {
                UploadId = uploadId,
                Status = result.Status,
                Message = result.Message,
                CheckedAt = DateTime.UtcNow,
                IsCompleted = IsCompletedStatus(result.Status),
                IsSuccess = IsSuccessStatus(result.Status),
                ErrorDetails = result.ErrorDetails
            };

            _logger.LogInformation("Upload status retrieved for ID: {UploadId}, Status: {Status}", 
                uploadId, result.Status);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get upload status for ID: {UploadId}", uploadId);
            return BadRequest(new ErrorResponse
            {
                Error = "status_check_failed",
                ErrorDescription = $"Failed to get upload status: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Waits for upload completion and returns final status
    /// </summary>
    /// <param name="uploadId">Upload ID to monitor</param>
    /// <param name="timeoutMinutes">Maximum wait time in minutes (default: 5)</param>
    /// <returns>Final upload status</returns>
    [HttpPost("upload/{uploadId}/wait")]
    [ProducesResponseType(typeof(UploadStatusResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(408)] // Timeout
    public async Task<IActionResult> WaitForUploadCompletion(
        string uploadId, 
        [FromQuery] int timeoutMinutes = 5)
    {
        try
        {
            _logger.LogInformation("Waiting for upload completion: {UploadId}, Timeout: {TimeoutMinutes} minutes", 
                uploadId, timeoutMinutes);

            var timeout = TimeSpan.FromMinutes(timeoutMinutes);
            var result = await _eFacturaClient.WaitForUploadCompletionAsync(uploadId, timeout);

            var response = new UploadStatusResponse
            {
                UploadId = uploadId,
                Status = result.Status,
                Message = result.Message,
                CheckedAt = DateTime.UtcNow,
                IsCompleted = IsCompletedStatus(result.Status),
                IsSuccess = IsSuccessStatus(result.Status),
                ErrorDetails = result.ErrorDetails
            };

            if (response.IsCompleted)
            {
                _logger.LogInformation("Upload completed for ID: {UploadId}, Final status: {Status}", 
                    uploadId, result.Status);
            }
            else
            {
                _logger.LogWarning("Upload timeout for ID: {UploadId}, Last status: {Status}", 
                    uploadId, result.Status);
                return StatusCode(408, response); // Request Timeout
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to wait for upload completion: {UploadId}", uploadId);
            return BadRequest(new ErrorResponse
            {
                Error = "wait_failed",
                ErrorDescription = $"Failed to wait for upload completion: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Converts an invoice to PDF format
    /// </summary>
    /// <param name="request">PDF conversion request</param>
    /// <returns>PDF file</returns>
    [HttpPost("convert-to-pdf")]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> ConvertToPdf([FromBody] PdfConversionRequest request)
    {
        try
        {
            _logger.LogInformation("Converting invoice to PDF: {InvoiceId}", request.Invoice.Id);

            var pdfData = await _eFacturaClient.ConvertToPdfAsync(request.Invoice);

            var fileName = $"invoice_{request.Invoice.Id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf";

            _logger.LogInformation("PDF conversion successful for invoice: {InvoiceId}, Size: {Size} bytes", 
                request.Invoice.Id, pdfData.Length);

            return File(pdfData, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PDF conversion failed for invoice: {InvoiceId}", request.Invoice.Id);
            return BadRequest(new ErrorResponse
            {
                Error = "pdf_conversion_failed",
                ErrorDescription = $"PDF conversion failed: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Validates and uploads an invoice in a single operation
    /// </summary>
    /// <param name="request">Combined validation and upload request</param>
    /// <returns>Combined validation and upload result</returns>
    [HttpPost("validate-and-upload")]
    [ProducesResponseType(typeof(ValidateAndUploadResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> ValidateAndUpload([FromBody] ValidateAndUploadRequest request)
    {
        try
        {
            _logger.LogInformation("Validating and uploading invoice for CIF: {Cif}, Invoice: {InvoiceId}", 
                request.Cif, request.Invoice.Id);

            // First validate
            var validation = await _eFacturaClient.ValidateInvoiceAsync(request.Invoice, request.Cif);
            
            var response = new ValidateAndUploadResponse
            {
                Cif = request.Cif,
                InvoiceId = request.Invoice.Id,
                Environment = request.Environment,
                ValidationSuccess = validation.Success,
                ValidationErrors = validation.Errors?.Select(e => e.Message).ToList() ?? new List<string>(),
                ValidationWarnings = validation.Warnings?.Select(w => w.Message).ToList() ?? new List<string>(),
                ValidatedAt = DateTime.UtcNow
            };

            if (!validation.Success)
            {
                _logger.LogWarning("Invoice validation failed, skipping upload. CIF: {Cif}, Invoice: {InvoiceId}", 
                    request.Cif, request.Invoice.Id);
                
                response.UploadSkipped = true;
                response.Message = "Upload skipped due to validation errors";
                return Ok(response);
            }

            // If validation successful, proceed with upload
            if (request.UploadIfValid)
            {
                var upload = await _eFacturaClient.UploadInvoiceAsync(request.Invoice, request.Cif, request.Environment);
                
                response.UploadSuccess = upload.Success;
                response.UploadId = upload.UploadId;
                response.UploadedAt = DateTime.UtcNow;
                response.Message = upload.Message;

                _logger.LogInformation("Validation and upload successful. CIF: {Cif}, Upload ID: {UploadId}", 
                    request.Cif, upload.UploadId);
            }
            else
            {
                response.UploadSkipped = true;
                response.Message = "Validation successful, upload skipped as requested";
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validate and upload failed for CIF: {Cif}, Invoice: {InvoiceId}", 
                request.Cif, request.Invoice.Id);
            return BadRequest(new ErrorResponse
            {
                Error = "validate_and_upload_failed",
                ErrorDescription = $"Validate and upload operation failed: {ex.Message}"
            });
        }
    }

    private static bool IsCompletedStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "completed" => true,
            "processed" => true,
            "accepted" => true,
            "rejected" => true,
            "failed" => true,
            "error" => true,
            _ => false
        };
    }

    private static bool IsSuccessStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "completed" => true,
            "processed" => true,
            "accepted" => true,
            _ => false
        };
    }
}

// DTOs for request/response
public class InvoiceValidationRequest
{
    [Required]
    public UblInvoice Invoice { get; set; } = new();
    
    [Required]
    public string Cif { get; set; } = string.Empty;
}

public class InvoiceUploadRequest
{
    [Required]
    public UblInvoice Invoice { get; set; } = new();
    
    [Required]
    public string Cif { get; set; } = string.Empty;
    
    public string Environment { get; set; } = "prod";
}

public class PdfConversionRequest
{
    [Required]
    public UblInvoice Invoice { get; set; } = new();
}

public class ValidateAndUploadRequest
{
    [Required]
    public UblInvoice Invoice { get; set; } = new();
    
    [Required]
    public string Cif { get; set; } = string.Empty;
    
    public string Environment { get; set; } = "prod";
    public bool UploadIfValid { get; set; } = true;
}

public class InvoiceValidationResponse
{
    public bool Success { get; set; }
    public string Cif { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public DateTime ValidatedAt { get; set; }
}

public class InvoiceUploadResponse
{
    public bool Success { get; set; }
    public string? UploadId { get; set; }
    public string Cif { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public string? Message { get; set; }
}

public class UploadStatusResponse
{
    public string UploadId { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? Message { get; set; }
    public DateTime CheckedAt { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorDetails { get; set; }
}

public class ValidateAndUploadResponse
{
    public string Cif { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    
    public bool ValidationSuccess { get; set; }
    public List<string> ValidationErrors { get; set; } = new();
    public List<string> ValidationWarnings { get; set; } = new();
    public DateTime ValidatedAt { get; set; }
    
    public bool UploadSuccess { get; set; }
    public bool UploadSkipped { get; set; }
    public string? UploadId { get; set; }
    public DateTime? UploadedAt { get; set; }
    
    public string? Message { get; set; }
}
