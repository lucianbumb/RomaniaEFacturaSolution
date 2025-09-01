using Microsoft.AspNetCore.Mvc;
using RomaniaEFacturaLibrary.Examples.Utilities;
using RomaniaEFacturaLibrary.Services;
using RomaniaEFacturaLibrary.Services.Authentication;

namespace RomaniaEFacturaLibrary.Examples.Controllers;

/// <summary>
/// Controller demonstrating simple usage examples for the EFactura library
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ExamplesController : ControllerBase
{
    private readonly IEFacturaClient _eFacturaClient;
    private readonly IAuthenticationService _authService;
    private readonly ILogger<ExamplesController> _logger;

    public ExamplesController(
        IEFacturaClient eFacturaClient,
        IAuthenticationService authService,
        ILogger<ExamplesController> logger)
    {
        _eFacturaClient = eFacturaClient;
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Example 1: Create and validate a simple invoice
    /// </summary>
    /// <returns>Sample invoice and validation results</returns>
    [HttpGet("create-simple-invoice")]
    public async Task<IActionResult> CreateSimpleInvoice()
    {
        try
        {
            _logger.LogInformation("Creating simple invoice example");

            // Create a simple invoice using the builder
            var invoice = InvoiceBuilder.CreateMinimalInvoice();

            // Try to validate it (this doesn't require authentication)
            try
            {
                await _authService.GetValidAccessTokenAsync();
                var validation = await _eFacturaClient.ValidateInvoiceAsync(invoice);
                
                return Ok(new
                {
                    example = "Simple Invoice Creation",
                    invoice = new
                    {
                        id = invoice.Id,
                        issueDate = invoice.IssueDate,
                        supplier = invoice.AccountingSupplierParty?.PartyLegalEntity?.RegistrationName,
                        customer = invoice.AccountingCustomerParty?.PartyLegalEntity?.RegistrationName,
                        totalAmount = invoice.LegalMonetaryTotal?.TaxInclusiveAmount?.Value,
                        currency = invoice.DocumentCurrencyCode,
                        linesCount = invoice.InvoiceLines?.Count ?? 0
                    },
                    validation = new
                    {
                        isValid = validation.Success,
                        errors = validation.Errors?.Select(e => e.Message) ?? Array.Empty<string>()
                    },
                    nextSteps = new[]
                    {
                        "Use POST /api/efactura/upload to upload this invoice",
                        "Use POST /api/efactura/validate to validate before upload",
                        "Check authentication status at GET /api/auth/status"
                    }
                });
            }
            catch (AuthenticationException)
            {
                return Ok(new
                {
                    example = "Simple Invoice Creation",
                    invoice = new
                    {
                        id = invoice.Id,
                        issueDate = invoice.IssueDate,
                        supplier = invoice.AccountingSupplierParty?.PartyLegalEntity?.RegistrationName,
                        customer = invoice.AccountingCustomerParty?.PartyLegalEntity?.RegistrationName,
                        totalAmount = invoice.LegalMonetaryTotal?.TaxInclusiveAmount?.Value,
                        currency = invoice.DocumentCurrencyCode,
                        linesCount = invoice.InvoiceLines?.Count ?? 0
                    },
                    validation = new
                    {
                        message = "Authentication required for validation",
                        loginUrl = Url.Action("Login", "Auth")
                    },
                    nextSteps = new[]
                    {
                        "Authenticate first using GET /api/auth/login",
                        "Then validate and upload the invoice"
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating simple invoice example");
            return StatusCode(500, new { error = "Failed to create example", details = ex.Message });
        }
    }

    /// <summary>
    /// Example 2: Create a complex invoice with multiple VAT rates
    /// </summary>
    /// <returns>Complex invoice example</returns>
    [HttpGet("create-complex-invoice")]
    public IActionResult CreateComplexInvoice()
    {
        try
        {
            _logger.LogInformation("Creating complex invoice example");

            var invoice = InvoiceBuilder.CreateComplexSampleInvoice();

            return Ok(new
            {
                example = "Complex Invoice with Multiple VAT Rates",
                invoice = new
                {
                    id = invoice.Id,
                    issueDate = invoice.IssueDate,
                    supplier = invoice.AccountingSupplierParty?.PartyLegalEntity?.RegistrationName,
                    customer = invoice.AccountingCustomerParty?.PartyLegalEntity?.RegistrationName,
                    totalAmount = invoice.LegalMonetaryTotal?.TaxInclusiveAmount?.Value,
                    currency = invoice.DocumentCurrencyCode,
                    lines = invoice.InvoiceLines?.Select(line => new
                    {
                        id = line.Id,
                        item = line.Item?.Name,
                        quantity = line.InvoicedQuantity?.Value,
                        unitPrice = line.Price?.PriceAmount?.Value,
                        lineTotal = line.LineExtensionAmount?.Value,
                        vatRate = line.Item?.ClassifiedTaxCategories?.FirstOrDefault()?.Percent
                    }),
                    vatBreakdown = invoice.TaxTotals?.FirstOrDefault()?.TaxSubtotals?.Select(ts => new
                    {
                        vatRate = ts.TaxCategory?.Percent,
                        taxableAmount = ts.TaxableAmount?.Value,
                        taxAmount = ts.TaxAmount?.Value
                    })
                },
                explanation = new
                {
                    purpose = "Shows how to handle invoices with multiple VAT rates (19%, 5%, 0%)",
                    vatRates = new[]
                    {
                        "19% - Standard VAT rate for most goods/services",
                        "5% - Reduced VAT rate for books, medicines",
                        "0% - Zero VAT for exports, certain services"
                    }
                },
                nextSteps = new[]
                {
                    "Authenticate using GET /api/auth/login",
                    "Validate using POST /api/efactura/validate",
                    "Upload using POST /api/efactura/upload"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating complex invoice example");
            return StatusCode(500, new { error = "Failed to create complex example", details = ex.Message });
        }
    }

    /// <summary>
    /// Example 3: Fluent invoice builder demonstration
    /// </summary>
    /// <returns>Fluent builder example</returns>
    [HttpGet("fluent-builder")]
    public IActionResult FluentBuilderExample()
    {
        try
        {
            _logger.LogInformation("Creating fluent builder example");

            // Demonstrate fluent builder
            var invoice = InvoiceBuilder.Create($"FLUENT-{DateTime.Now:yyyyMMdd}-001")
                .WithIssueDate(DateTime.Today)
                .WithDueDate(DateTime.Today.AddDays(30))
                .WithCurrency("RON")
                .WithSupplier(
                    name: "SC My Company SRL",
                    registrationName: "SC My Company SRL",
                    companyId: "J40/98765/2023",
                    vatId: "RO98765432"
                )
                .WithCustomer(
                    name: "SC Client Company SRL",
                    registrationName: "SC Client Company SRL", 
                    companyId: "J12/11111/2020",
                    vatId: "RO11111111"
                )
                .AddLine("Consultan?? software", 10, "HUR", 250.00m, 19)
                .AddLine("Licen?? software", 1, "EA", 1500.00m, 19)
                .AddLine("Manual utilizare", 2, "EA", 25.00m, 5)
                .Build();

            return Ok(new
            {
                example = "Fluent Invoice Builder",
                invoice = new
                {
                    id = invoice.Id,
                    issueDate = invoice.IssueDate,
                    dueDate = invoice.DueDate,
                    supplier = invoice.AccountingSupplierParty?.PartyLegalEntity?.RegistrationName,
                    customer = invoice.AccountingCustomerParty?.PartyLegalEntity?.RegistrationName,
                    totalAmount = invoice.LegalMonetaryTotal?.TaxInclusiveAmount?.Value,
                    currency = invoice.DocumentCurrencyCode,
                    lines = invoice.InvoiceLines?.Select(line => new
                    {
                        item = line.Item?.Name,
                        quantity = line.InvoicedQuantity?.Value,
                        unitPrice = line.Price?.PriceAmount?.Value,
                        lineTotal = line.LineExtensionAmount?.Value,
                        vatRate = line.Item?.ClassifiedTaxCategories?.FirstOrDefault()?.Percent
                    })
                },
                codeExample = @"
var invoice = InvoiceBuilder.Create(""FLUENT-001"")
    .WithIssueDate(DateTime.Today)
    .WithDueDate(DateTime.Today.AddDays(30))
    .WithSupplier(""My Company"", ""SC My Company SRL"", ""J40/98765/2023"", ""RO98765432"")
    .WithCustomer(""Client Company"", ""SC Client Company SRL"", ""J12/11111/2020"", ""RO11111111"")
    .AddLine(""Consultan?? software"", 10, ""HUR"", 250.00m, 19)
    .AddLine(""Licen?? software"", 1, ""EA"", 1500.00m, 19)
    .Build();",
                benefits = new[]
                {
                    "Fluent, readable API for invoice creation",
                    "Automatic VAT calculation",
                    "Validation of required fields",
                    "Support for Romanian accounting standards"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating fluent builder example");
            return StatusCode(500, new { error = "Failed to create fluent example", details = ex.Message });
        }
    }

    /// <summary>
    /// Example 4: Authentication flow demonstration
    /// </summary>
    /// <returns>Authentication process explanation</returns>
    [HttpGet("authentication-flow")]
    public IActionResult AuthenticationFlowExample()
    {
        try
        {
            var authUrl = _authService.GetAuthorizationUrl("efactura", "example-state-123");

            return Ok(new
            {
                example = "OAuth2 Authentication Flow",
                description = "Step-by-step process for authenticating with ANAF EFactura",
                steps = new[]
                {
                    new { step = 1, action = "Redirect to ANAF", url = authUrl, description = "User browser is redirected to ANAF OAuth2 page" },
                    new { step = 2, action = "Certificate selection", description = "ANAF prompts user to insert USB certificate and select it" },
                    new { step = 3, action = "User confirmation", description = "User confirms authentication with selected certificate" },
                    new { step = 4, action = "Callback with code", description = "ANAF redirects back with authorization code" },
                    new { step = 5, action = "Token exchange", url = "/api/auth/callback", description = "Application exchanges code for JWT access token" },
                    new { step = 6, action = "API access", description = "Use bearer token for all subsequent EFactura API calls" }
                },
                implementation = new
                {
                    startUrl = Url.Action("Login", "Auth"),
                    callbackUrl = Url.Action("Callback", "Auth"),
                    statusUrl = Url.Action("GetAuthStatus", "Auth"),
                    testUrls = new
                    {
                        dashboard = Url.Action("Dashboard", "EFactura"),
                        validateInvoice = Url.Action("ValidateInvoice", "EFactura"),
                        uploadInvoice = Url.Action("UploadInvoice", "EFactura")
                    }
                },
                requirements = new[]
                {
                    "Valid ANAF application registration (ClientId/ClientSecret)",
                    "Digital certificate on USB device or installed in certificate store",
                    "Registered redirect URI matching your application URL",
                    "Valid Romanian CIF registered for EFactura"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating authentication flow example");
            return StatusCode(500, new { error = "Failed to create auth example", details = ex.Message });
        }
    }

    /// <summary>
    /// Example 5: Complete workflow from creation to upload
    /// </summary>
    /// <returns>Full workflow example</returns>
    [HttpGet("complete-workflow")]
    public IActionResult CompleteWorkflowExample()
    {
        try
        {
            return Ok(new
            {
                example = "Complete EFactura Workflow",
                description = "End-to-end process for creating, validating, and uploading an invoice",
                workflow = new[]
                {
                    new 
                    { 
                        step = 1, 
                        title = "Authentication",
                        action = "GET /api/auth/login",
                        description = "Authenticate with ANAF using digital certificate",
                        result = "JWT access token for API calls"
                    },
                    new 
                    { 
                        step = 2, 
                        title = "Create Invoice",
                        action = "Use InvoiceBuilder or manual UblInvoice creation",
                        description = "Build invoice with all required Romanian fields",
                        result = "Complete UBL 2.1 invoice object"
                    },
                    new 
                    { 
                        step = 3, 
                        title = "Validate Invoice",
                        action = "POST /api/efactura/validate",
                        description = "Check invoice compliance before upload",
                        result = "Validation results with any errors"
                    },
                    new 
                    { 
                        step = 4, 
                        title = "Upload Invoice",
                        action = "POST /api/efactura/upload",
                        description = "Submit invoice to ANAF SPV system",
                        result = "Upload ID for tracking"
                    },
                    new 
                    { 
                        step = 5, 
                        title = "Check Status",
                        action = "GET /api/efactura/upload/{uploadId}/status",
                        description = "Monitor processing status",
                        result = "Current status (processing, completed, error)"
                    },
                    new 
                    { 
                        step = 6, 
                        title = "Download Invoices",
                        action = "GET /api/efactura/invoices",
                        description = "Retrieve processed invoices",
                        result = "List of available invoices"
                    }
                },
                sampleCode = @"
// 1. Authentication (handled by redirecting user to /api/auth/login)

// 2. Create invoice
var invoice = InvoiceBuilder.CreateSampleRomanianInvoice();

// 3. Validate
var validation = await eFacturaClient.ValidateInvoiceAsync(invoice);
if (!validation.Success) {
    // Handle errors
    return BadRequest(validation.Errors);
}

// 4. Upload
var uploadResponse = await eFacturaClient.UploadInvoiceAsync(invoice);

// 5. Wait for completion
var finalStatus = await eFacturaClient.WaitForUploadCompletionAsync(uploadResponse.UploadId);

// 6. Download invoices
var invoices = await eFacturaClient.GetInvoicesAsync(DateTime.Today.AddDays(-30), DateTime.Today);",
                tips = new[]
                {
                    "Always validate invoices before uploading to avoid rejections",
                    "Store upload IDs for tracking and status checking",
                    "Handle authentication expiration gracefully",
                    "Use appropriate date ranges when downloading invoices",
                    "Monitor upload status for large batches"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating complete workflow example");
            return StatusCode(500, new { error = "Failed to create workflow example", details = ex.Message });
        }
    }
}