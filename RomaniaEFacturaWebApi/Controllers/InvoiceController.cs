using Microsoft.AspNetCore.Mvc;
using RomaniaEFacturaLibrary.Services;
using RomaniaEFacturaLibrary.Services.Authentication;
using RomaniaEFacturaLibrary.Models.Ubl;

namespace RomaniaEFacturaWebApi.Controllers;

/// <summary>
/// Controller for invoice upload and management operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class InvoiceController : ControllerBase
{
    private readonly IEFacturaClient _eFacturaClient;
    private readonly IAuthenticationService _authService;
    private readonly ILogger<InvoiceController> _logger;
    private const string DefaultCif = "123456789";

    public InvoiceController(
        IEFacturaClient eFacturaClient,
        IAuthenticationService authService,
        ILogger<InvoiceController> logger)
    {
        _eFacturaClient = eFacturaClient;
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Uploads an invoice to ANAF SPV
    /// </summary>
    [HttpPost("upload")]
    public async Task<IActionResult> UploadInvoice(
        [FromBody] UblInvoice invoice,
        [FromQuery] string? cif = null)
    {
        try
        {
            if (invoice == null)
            {
                return BadRequest(new { error = "Invoice data is required" });
            }

            // Use provided CIF or default
            var targetCif = cif ?? DefaultCif;

            _logger.LogInformation("Uploading invoice {InvoiceId} for CIF: {Cif}", invoice.Id, targetCif);

            // Step 1: Validate invoice
            var validation = await _eFacturaClient.ValidateInvoiceAsync(invoice, targetCif);
            if (!validation.Success)
            {
                _logger.LogWarning("Invoice validation failed for {InvoiceId}", invoice.Id);
                return BadRequest(new
                {
                    error = "Invoice validation failed",
                    invoiceId = invoice.Id,
                    cif = targetCif,
                    errors = validation.Errors?.Select(e => e.Message) ?? Array.Empty<string>(),
                    suggestion = "Please fix the validation errors and try again"
                });
            }

            // Step 2: Upload invoice
            var uploadResponse = await _eFacturaClient.UploadInvoiceAsync(invoice, targetCif);

            _logger.LogInformation("Invoice {InvoiceId} uploaded successfully with ID {UploadId}", 
                invoice.Id, uploadResponse.UploadId);

            return Ok(new
            {
                success = true,
                uploadId = uploadResponse.UploadId,
                invoiceId = invoice.Id,
                cif = targetCif,
                message = "Invoice uploaded successfully",
                uploadedAt = DateTime.UtcNow,
                nextSteps = new[]
                {
                    $"Check upload status: GET /api/invoice/status/{uploadResponse.UploadId}",
                    $"Wait for processing: POST /api/invoice/wait/{uploadResponse.UploadId}",
                    "Monitor the upload progress through ANAF SPV"
                }
            });
        }
        catch (AuthenticationException)
        {
            return Unauthorized(new
            {
                error = "Authentication required",
                message = "Please authenticate first using /api/efactura/login",
                loginUrl = Url.Action("Login", "EFactura")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading invoice {InvoiceId}", invoice?.Id);
            return StatusCode(500, new 
            { 
                error = "Upload failed", 
                details = ex.Message,
                invoiceId = invoice?.Id,
                cif = cif ?? DefaultCif
            });
        }
    }

    /// <summary>
    /// Gets the status of an uploaded invoice
    /// </summary>
    [HttpGet("status/{uploadId}")]
    public async Task<IActionResult> GetUploadStatus([FromRoute] string uploadId)
    {
        try
        {
            if (string.IsNullOrEmpty(uploadId))
            {
                return BadRequest(new { error = "Upload ID is required" });
            }

            _logger.LogInformation("Checking status for upload {UploadId}", uploadId);

            var status = await _eFacturaClient.GetUploadStatusAsync(uploadId);

            var isComplete = IsUploadComplete(status.Status);

            return Ok(new
            {
                uploadId,
                status = status.Status,
                message = status.Message,
                isComplete,
                checkedAt = DateTime.UtcNow,
                nextSteps = isComplete 
                    ? new[] { "Upload processing is complete", "Check ANAF SPV for final status" }
                    : new[] { "Upload is still processing", "Check again in a few seconds", $"Or use: POST /api/invoice/wait/{uploadId}" }
            });
        }
        catch (AuthenticationException)
        {
            return Unauthorized(new
            {
                error = "Authentication required",
                loginUrl = Url.Action("Login", "EFactura")
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
    [HttpPost("wait/{uploadId}")]
    public async Task<IActionResult> WaitForUploadCompletion(
        [FromRoute] string uploadId,
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

            _logger.LogInformation("Waiting for upload {UploadId} to complete (timeout: {Timeout} minutes)", 
                uploadId, timeoutMinutes);

            var timeout = TimeSpan.FromMinutes(timeoutMinutes);
            var finalStatus = await _eFacturaClient.WaitForUploadCompletionAsync(uploadId, timeout);

            var isComplete = IsUploadComplete(finalStatus.Status);

            return Ok(new
            {
                uploadId,
                finalStatus = finalStatus.Status,
                message = finalStatus.Message,
                isComplete,
                completedAt = DateTime.UtcNow,
                timeoutMinutes,
                result = isComplete ? "Processing completed" : $"Timed out after {timeoutMinutes} minutes"
            });
        }
        catch (AuthenticationException)
        {
            return Unauthorized(new
            {
                error = "Authentication required",
                loginUrl = Url.Action("Login", "EFactura")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error waiting for upload completion {UploadId}", uploadId);
            return StatusCode(500, new { error = "Failed to wait for completion", details = ex.Message });
        }
    }

    /// <summary>
    /// Validates an invoice without uploading it
    /// </summary>
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateInvoice(
        [FromBody] UblInvoice invoice,
        [FromQuery] string? cif = null)
    {
        try
        {
            if (invoice == null)
            {
                return BadRequest(new { error = "Invoice data is required" });
            }

            // Use provided CIF or default
            var targetCif = cif ?? DefaultCif;

            _logger.LogInformation("Validating invoice {InvoiceId} for CIF: {Cif}", invoice.Id, targetCif);

            var validation = await _eFacturaClient.ValidateInvoiceAsync(invoice, targetCif);

            return Ok(new
            {
                invoiceId = invoice.Id,
                cif = targetCif,
                isValid = validation.Success,
                errors = validation.Errors?.Select(e => new
                {
                    message = e.Message,
                    severity = "Error"
                }).ToArray() ?? Array.Empty<object>(),
                validatedAt = DateTime.UtcNow,
                nextSteps = validation.Success 
                    ? new[] { "Invoice is valid", "You can now upload it using POST /api/invoice/upload" }
                    : new[] { "Fix the validation errors", "Try validation again before uploading" }
            });
        }
        catch (AuthenticationException)
        {
            return Unauthorized(new
            {
                error = "Authentication required",
                loginUrl = Url.Action("Login", "EFactura")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating invoice {InvoiceId}", invoice?.Id);
            return StatusCode(500, new { error = "Validation failed", details = ex.Message });
        }
    }

    /// <summary>
    /// Creates a sample invoice for testing purposes
    /// </summary>
    [HttpGet("sample")]
    public IActionResult CreateSampleInvoice()
    {
        try
        {
            var invoice = CreateSampleUblInvoice();

            return Ok(new
            {
                message = "Sample invoice created successfully",
                cif = DefaultCif,
                invoice,
                nextSteps = new[]
                {
                    "Use this invoice data for testing",
                    "Validate it: POST /api/invoice/validate",
                    "Upload it: POST /api/invoice/upload",
                    "Modify the data as needed for your tests"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating sample invoice");
            return StatusCode(500, new { error = "Failed to create sample invoice", details = ex.Message });
        }
    }

    private static bool IsUploadComplete(string status)
    {
        if (string.IsNullOrEmpty(status))
            return false;

        var completedStatuses = new[] { "procesat", "finalizat", "respins", "eroare", "completed", "processed", "rejected", "error" };
        return completedStatuses.Any(s => status.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    private static UblInvoice CreateSampleUblInvoice()
    {
        var invoice = new UblInvoice
        {
            Id = $"DEMO-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}",
            IssueDate = DateTime.Today,
            DueDate = DateTime.Today.AddDays(30),
            DocumentCurrencyCode = "RON"
        };

        // Supplier (your company)
        invoice.AccountingSupplierParty = new Party
        {
            PartyName = new PartyName { Name = "Demo Supplier SRL" },
            PostalAddress = new PostalAddress
            {
                StreetName = "Strada Demo nr. 1",
                CityName = "Bucuresti",
                PostalZone = "010101",
                Country = new Country { IdentificationCode = "RO" }
            },
            PartyTaxSchemes = new List<PartyTaxScheme>
            {
                new() { CompanyId = "RO123456789", TaxScheme = new TaxScheme { Id = "VAT" } }
            },
            PartyLegalEntity = new PartyLegalEntity
            {
                RegistrationName = "Demo Supplier SRL",
                CompanyId = "J40/12345/2020"
            },
            Contact = new Contact
            {
                Telephone = "+40721123456",
                ElectronicMail = "contact@demo-supplier.ro"
            }
        };

        // Customer
        invoice.AccountingCustomerParty = new Party
        {
            PartyName = new PartyName { Name = "Demo Customer SRL" },
            PostalAddress = new PostalAddress
            {
                StreetName = "Strada Client nr. 2",
                CityName = "Cluj-Napoca",
                PostalZone = "400001",
                Country = new Country { IdentificationCode = "RO" }
            },
            PartyTaxSchemes = new List<PartyTaxScheme>
            {
                new() { CompanyId = "RO987654321", TaxScheme = new TaxScheme { Id = "VAT" } }
            },
            PartyLegalEntity = new PartyLegalEntity
            {
                RegistrationName = "Demo Customer SRL",
                CompanyId = "J12/54321/2019"
            }
        };

        // Invoice lines
        var line1 = new InvoiceLine
        {
            Id = "1",
            InvoicedQuantity = new Quantity { Value = 2, UnitCode = "EA" },
            LineExtensionAmount = new Amount { Value = 200.00m, CurrencyId = "RON" },
            Item = new Item
            {
                Name = "Demo Product 1",
                ClassifiedTaxCategories = new List<TaxCategory>
                {
                    new() { Id = "S", Percent = 19, TaxScheme = new TaxScheme { Id = "VAT" } }
                }
            },
            Price = new Price
            {
                PriceAmount = new Amount { Value = 100.00m, CurrencyId = "RON" }
            }
        };

        invoice.InvoiceLines = new List<InvoiceLine> { line1 };

        // Tax totals
        var taxSubtotal = new TaxSubtotal
        {
            TaxableAmount = new Amount { Value = 200.00m, CurrencyId = "RON" },
            TaxAmount = new Amount { Value = 38.00m, CurrencyId = "RON" },
            TaxCategory = new TaxCategory
            {
                Id = "S",
                Percent = 19,
                TaxScheme = new TaxScheme { Id = "VAT" }
            }
        };

        invoice.TaxTotals = new List<TaxTotal>
        {
            new()
            {
                TaxAmount = new Amount { Value = 38.00m, CurrencyId = "RON" },
                TaxSubtotals = new List<TaxSubtotal> { taxSubtotal }
            }
        };

        // Monetary totals
        invoice.LegalMonetaryTotal = new MonetaryTotal
        {
            LineExtensionAmount = new Amount { Value = 200.00m, CurrencyId = "RON" },
            TaxExclusiveAmount = new Amount { Value = 200.00m, CurrencyId = "RON" },
            TaxInclusiveAmount = new Amount { Value = 238.00m, CurrencyId = "RON" },
            PayableAmount = new Amount { Value = 238.00m, CurrencyId = "RON" }
        };

        return invoice;
    }
}