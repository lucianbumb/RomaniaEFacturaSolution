using Microsoft.AspNetCore.Mvc;
using RomaniaEFacturaLibrary.Services;
using RomaniaEFacturaLibrary.Services.Authentication;
using RomaniaEFacturaLibrary.Models.Ubl;

namespace RomaniaEFacturaLibrary.Examples;

/// <summary>
/// Examples demonstrating multi-tenant usage with CIF parameters
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MultiTenantExamplesController : ControllerBase
{
    private readonly IEFacturaClient _eFacturaClient;
    private readonly IAuthenticationService _authService;
    private readonly ILogger<MultiTenantExamplesController> _logger;

    public MultiTenantExamplesController(
        IEFacturaClient eFacturaClient,
        IAuthenticationService authService,
        ILogger<MultiTenantExamplesController> logger)
    {
        _eFacturaClient = eFacturaClient;
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Example: Upload invoice for a specific company (CIF)
    /// </summary>
    [HttpPost("upload/{cif}")]
    public async Task<IActionResult> UploadInvoiceForCompany(
        [FromRoute] string cif,
        [FromBody] UblInvoice invoice)
    {
        try
        {
            _logger.LogInformation("Uploading invoice {InvoiceId} for CIF: {Cif}", invoice.Id, cif);

            // Validate first (now requires CIF)
            var validation = await _eFacturaClient.ValidateInvoiceAsync(invoice, cif);
            if (!validation.Success)
            {
                return BadRequest(new 
                { 
                    error = "Invoice validation failed",
                    errors = validation.Errors?.Select(e => e.Message) ?? Array.Empty<string>()
                });
            }

            // Upload (now requires CIF)
            var uploadResponse = await _eFacturaClient.UploadInvoiceAsync(invoice, cif);

            return Ok(new
            {
                success = true,
                cif,
                invoiceId = invoice.Id,
                uploadId = uploadResponse.UploadId,
                message = $"Invoice uploaded successfully for CIF {cif}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading invoice for CIF {Cif}", cif);
            return StatusCode(500, new { error = "Upload failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Example: Get invoices for multiple companies
    /// </summary>
    [HttpGet("invoices/multi-tenant")]
    public async Task<IActionResult> GetInvoicesForMultipleCompanies(
        [FromQuery] string[] cifs,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        try
        {
            if (cifs?.Length == 0)
            {
                return BadRequest(new { error = "At least one CIF must be provided" });
            }

            var results = new Dictionary<string, object>();

            foreach (var cif in cifs)
            {
                try
                {
                    _logger.LogInformation("Getting invoices for CIF: {Cif}", cif);
                    
                    var invoices = await _eFacturaClient.GetInvoicesAsync(cif, from, to);
                    
                    results[cif] = new
                    {
                        success = true,
                        count = invoices.Count,
                        invoices = invoices.Select(inv => new
                        {
                            id = inv.Id,
                            issueDate = inv.IssueDate,
                            totalAmount = inv.LegalMonetaryTotal?.TaxInclusiveAmount?.Value,
                            currency = inv.DocumentCurrencyCode
                        })
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get invoices for CIF {Cif}", cif);
                    results[cif] = new
                    {
                        success = false,
                        error = ex.Message
                    };
                }
            }

            return Ok(new
            {
                dateRange = new { from, to },
                results,
                totalCompanies = cifs.Length,
                successfulCompanies = results.Values.Count(r => ((dynamic)r).success)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting invoices for multiple companies");
            return StatusCode(500, new { error = "Failed to get invoices", details = ex.Message });
        }
    }

    /// <summary>
    /// Example: Batch validate invoices for different companies
    /// </summary>
    [HttpPost("validate/batch")]
    public async Task<IActionResult> BatchValidateInvoices(
        [FromBody] Dictionary<string, UblInvoice> invoicesByCif)
    {
        try
        {
            var results = new Dictionary<string, object>();

            foreach (var (cif, invoice) in invoicesByCif)
            {
                try
                {
                    _logger.LogInformation("Validating invoice {InvoiceId} for CIF: {Cif}", invoice.Id, cif);
                    
                    var validation = await _eFacturaClient.ValidateInvoiceAsync(invoice, cif);
                    
                    results[cif] = new
                    {
                        invoiceId = invoice.Id,
                        isValid = validation.Success,
                        errors = validation.Errors?.Select(e => e.Message) ?? Array.Empty<string>()
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to validate invoice for CIF {Cif}", cif);
                    results[cif] = new
                    {
                        invoiceId = invoice.Id,
                        isValid = false,
                        error = ex.Message
                    };
                }
            }

            return Ok(new
            {
                totalInvoices = invoicesByCif.Count,
                validInvoices = results.Values.Count(r => ((dynamic)r).isValid),
                results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in batch validation");
            return StatusCode(500, new { error = "Batch validation failed", details = ex.Message });
        }
    }
}

/// <summary>
/// Example service showing how to work with multiple companies programmatically
/// </summary>
public class MultiTenantEFacturaService
{
    private readonly IEFacturaClient _eFacturaClient;
    private readonly IAuthenticationService _authService;
    private readonly ILogger<MultiTenantEFacturaService> _logger;

    public MultiTenantEFacturaService(
        IEFacturaClient eFacturaClient,
        IAuthenticationService authService,
        ILogger<MultiTenantEFacturaService> logger)
    {
        _eFacturaClient = eFacturaClient;
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Process invoices for a specific tenant (company)
    /// </summary>
    public async Task<ProcessingResult> ProcessInvoicesForTenant(string tenantCif, List<UblInvoice> invoices)
    {
        var result = new ProcessingResult
        {
            TenantCif = tenantCif,
            TotalInvoices = invoices.Count
        };

        foreach (var invoice in invoices)
        {
            try
            {
                // Step 1: Validate
                _logger.LogInformation("Processing invoice {InvoiceId} for tenant {TenantCif}", invoice.Id, tenantCif);
                
                var validation = await _eFacturaClient.ValidateInvoiceAsync(invoice, tenantCif);
                if (!validation.Success)
                {
                    result.FailedInvoices.Add(new FailedInvoice
                    {
                        InvoiceId = invoice.Id,
                        Error = $"Validation failed: {string.Join(", ", validation.Errors?.Select(e => e.Message) ?? Array.Empty<string>())}"
                    });
                    continue;
                }

                // Step 2: Upload
                var uploadResponse = await _eFacturaClient.UploadInvoiceAsync(invoice, tenantCif);
                
                // Step 3: Wait for processing (optional)
                var finalStatus = await _eFacturaClient.WaitForUploadCompletionAsync(uploadResponse.UploadId, TimeSpan.FromMinutes(5));
                
                result.SuccessfulInvoices.Add(new SuccessfulInvoice
                {
                    InvoiceId = invoice.Id,
                    UploadId = uploadResponse.UploadId,
                    FinalStatus = finalStatus.Status
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing invoice {InvoiceId} for tenant {TenantCif}", invoice.Id, tenantCif);
                result.FailedInvoices.Add(new FailedInvoice
                {
                    InvoiceId = invoice.Id,
                    Error = ex.Message
                });
            }
        }

        _logger.LogInformation("Completed processing for tenant {TenantCif}. Success: {Success}, Failed: {Failed}", 
            tenantCif, result.SuccessfulInvoices.Count, result.FailedInvoices.Count);

        return result;
    }

    /// <summary>
    /// Get consolidated invoice data across multiple tenants
    /// </summary>
    public async Task<ConsolidatedReport> GetConsolidatedReport(List<string> tenantCifs, DateTime from, DateTime to)
    {
        var report = new ConsolidatedReport
        {
            DateRange = new DateRange { From = from, To = to },
            TenantReports = new Dictionary<string, TenantReport>()
        };

        foreach (var cif in tenantCifs)
        {
            try
            {
                var invoices = await _eFacturaClient.GetInvoicesAsync(cif, from, to);
                
                report.TenantReports[cif] = new TenantReport
                {
                    TotalInvoices = invoices.Count,
                    TotalAmount = invoices.Sum(i => i.LegalMonetaryTotal?.TaxInclusiveAmount?.Value ?? 0),
                    Currency = invoices.FirstOrDefault()?.DocumentCurrencyCode ?? "RON",
                    InvoiceIds = invoices.Select(i => i.Id).ToList()
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get report data for tenant {Cif}", cif);
                report.TenantReports[cif] = new TenantReport
                {
                    Error = ex.Message
                };
            }
        }

        return report;
    }
}

#region Data Models

public class ProcessingResult
{
    public string TenantCif { get; set; } = string.Empty;
    public int TotalInvoices { get; set; }
    public List<SuccessfulInvoice> SuccessfulInvoices { get; set; } = new();
    public List<FailedInvoice> FailedInvoices { get; set; } = new();
}

public class SuccessfulInvoice
{
    public string InvoiceId { get; set; } = string.Empty;
    public string UploadId { get; set; } = string.Empty;
    public string FinalStatus { get; set; } = string.Empty;
}

public class FailedInvoice
{
    public string InvoiceId { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

public class ConsolidatedReport
{
    public DateRange DateRange { get; set; } = new();
    public Dictionary<string, TenantReport> TenantReports { get; set; } = new();
}

public class DateRange
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}

public class TenantReport
{
    public int TotalInvoices { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "RON";
    public List<string> InvoiceIds { get; set; } = new();
    public string? Error { get; set; }
}

#endregion