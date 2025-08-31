using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RomaniaEFacturaLibrary.Services;
using System.ComponentModel.DataAnnotations;

namespace RomaniaEFacturaLibrary.Examples.Controllers;

/// <summary>
/// Controller for downloading and managing invoices from SPV
/// </summary>
[ApiController]
[Route("api/efactura/[controller]")]
[Authorize]
public class InvoiceManagementController : ControllerBase
{
    private readonly IEFacturaClient _eFacturaClient;
    private readonly ILogger<InvoiceManagementController> _logger;

    public InvoiceManagementController(
        IEFacturaClient eFacturaClient,
        ILogger<InvoiceManagementController> logger)
    {
        _eFacturaClient = eFacturaClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets a list of invoices for a specific CIF and date range
    /// </summary>
    /// <param name="cif">Company fiscal identifier</param>
    /// <param name="from">Start date (default: 30 days ago)</param>
    /// <param name="to">End date (default: today)</param>
    /// <param name="pageSize">Number of results per page (default: 50)</param>
    /// <param name="page">Page number (default: 1)</param>
    /// <returns>List of invoices with pagination</returns>
    [HttpGet("list")]
    [ProducesResponseType(typeof(InvoiceListResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> GetInvoices(
        [FromQuery, Required] string cif,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int pageSize = 50,
        [FromQuery] int page = 1)
    {
        try
        {
            var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
            var toDate = to ?? DateTime.UtcNow;

            if (pageSize <= 0 || pageSize > 1000)
                pageSize = 50;
            
            if (page <= 0)
                page = 1;

            _logger.LogInformation("Getting invoices for CIF: {Cif}, From: {From}, To: {To}, Page: {Page}, PageSize: {PageSize}", 
                cif, fromDate, toDate, page, pageSize);

            var allInvoices = await _eFacturaClient.GetInvoicesAsync(cif, fromDate, toDate);

            // Apply pagination
            var totalCount = allInvoices.Count;
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            var skipCount = (page - 1) * pageSize;
            var pagedInvoices = allInvoices.Skip(skipCount).Take(pageSize).ToList();

            var response = new InvoiceListResponse
            {
                Cif = cif,
                FromDate = fromDate,
                ToDate = toDate,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalPages,
                HasNextPage = page < totalPages,
                HasPreviousPage = page > 1,
                Invoices = pagedInvoices.Select(inv => new InvoiceSummary
                {
                    Id = inv.Id,
                    Type = inv.Type,
                    Cif = inv.Cif,
                    CreationDate = inv.CreationDate,
                    RequestId = inv.RequestId
                }).ToList(),
                RetrievedAt = DateTime.UtcNow
            };

            _logger.LogInformation("Retrieved {Count} invoices for CIF: {Cif} (Page {Page}/{TotalPages})", 
                pagedInvoices.Count, cif, page, totalPages);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get invoices for CIF: {Cif}", cif);
            return BadRequest(new ErrorResponse
            {
                Error = "get_invoices_failed",
                ErrorDescription = $"Failed to retrieve invoices: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Downloads a specific invoice by message ID
    /// </summary>
    /// <param name="messageId">Message ID of the invoice to download</param>
    /// <param name="format">Download format (xml, raw, pdf)</param>
    /// <returns>Invoice file</returns>
    [HttpGet("download/{messageId}")]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DownloadInvoice(
        string messageId,
        [FromQuery] string format = "xml")
    {
        try
        {
            _logger.LogInformation("Downloading invoice with ID: {MessageId}, Format: {Format}", messageId, format);

            switch (format.ToLowerInvariant())
            {
                case "xml":
                    var invoice = await _eFacturaClient.DownloadInvoiceAsync(messageId);
                    var fileName = $"invoice_{messageId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xml";
                    
                    // You would need to serialize the UblInvoice back to XML
                    // For now, returning a placeholder response
                    var xmlContent = $"<!-- Invoice {messageId} XML content would go here -->";
                    var xmlBytes = System.Text.Encoding.UTF8.GetBytes(xmlContent);
                    
                    _logger.LogInformation("Downloaded invoice XML: {MessageId}, Size: {Size} bytes", messageId, xmlBytes.Length);
                    return File(xmlBytes, "application/xml", fileName);

                case "raw":
                    var rawData = await _eFacturaClient.DownloadRawInvoiceAsync(messageId);
                    var rawFileName = $"invoice_{messageId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";
                    
                    _logger.LogInformation("Downloaded raw invoice: {MessageId}, Size: {Size} bytes", messageId, rawData.Length);
                    return File(rawData, "application/zip", rawFileName);

                case "pdf":
                    var invoiceForPdf = await _eFacturaClient.DownloadInvoiceAsync(messageId);
                    var pdfData = await _eFacturaClient.ConvertToPdfAsync(invoiceForPdf);
                    var pdfFileName = $"invoice_{messageId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf";
                    
                    _logger.LogInformation("Downloaded invoice PDF: {MessageId}, Size: {Size} bytes", messageId, pdfData.Length);
                    return File(pdfData, "application/pdf", pdfFileName);

                default:
                    return BadRequest(new ErrorResponse
                    {
                        Error = "invalid_format",
                        ErrorDescription = $"Invalid format '{format}'. Supported formats: xml, raw, pdf"
                    });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download invoice: {MessageId}", messageId);
            return BadRequest(new ErrorResponse
            {
                Error = "download_failed",
                ErrorDescription = $"Failed to download invoice: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Gets detailed information about a specific invoice
    /// </summary>
    /// <param name="messageId">Message ID of the invoice</param>
    /// <returns>Detailed invoice information</returns>
    [HttpGet("details/{messageId}")]
    [ProducesResponseType(typeof(InvoiceDetailsResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetInvoiceDetails(string messageId)
    {
        try
        {
            _logger.LogInformation("Getting invoice details for ID: {MessageId}", messageId);

            var invoice = await _eFacturaClient.DownloadInvoiceAsync(messageId);

            var response = new InvoiceDetailsResponse
            {
                MessageId = messageId,
                InvoiceId = invoice.Id,
                IssueDate = invoice.IssueDate,
                InvoiceTypeCode = invoice.InvoiceTypeCode,
                DocumentCurrencyCode = invoice.DocumentCurrencyCode,
                
                Supplier = new PartyInfo
                {
                    Name = invoice.AccountingSupplierParty?.PartyLegalEntity?.RegistrationName ?? "Unknown",
                    CompanyId = invoice.AccountingSupplierParty?.PartyLegalEntity?.CompanyId ?? "Unknown",
                    Address = GetAddressInfo(invoice.AccountingSupplierParty?.PostalAddress)
                },
                
                Customer = new PartyInfo
                {
                    Name = invoice.AccountingCustomerParty?.PartyLegalEntity?.RegistrationName ?? "Unknown",
                    CompanyId = invoice.AccountingCustomerParty?.PartyLegalEntity?.CompanyId ?? "Unknown",
                    Address = GetAddressInfo(invoice.AccountingCustomerParty?.PostalAddress)
                },
                
                InvoiceLines = invoice.InvoiceLines?.Select(line => new InvoiceLineInfo
                {
                    Id = line.Id,
                    ItemName = line.Item?.Name ?? "Unknown",
                    Quantity = line.InvoicedQuantity?.Value ?? 0,
                    UnitCode = line.InvoicedQuantity?.UnitCode ?? "EA",
                    UnitPrice = line.Price?.PriceAmount?.Value ?? 0,
                    LineTotal = line.LineExtensionAmount?.Value ?? 0
                }).ToList() ?? new List<InvoiceLineInfo>(),
                
                MonetaryTotals = new MonetaryInfo
                {
                    LineExtensionAmount = invoice.LegalMonetaryTotal?.LineExtensionAmount?.Value ?? 0,
                    TaxExclusiveAmount = invoice.LegalMonetaryTotal?.TaxExclusiveAmount?.Value ?? 0,
                    TaxInclusiveAmount = invoice.LegalMonetaryTotal?.TaxInclusiveAmount?.Value ?? 0,
                    PayableAmount = invoice.LegalMonetaryTotal?.PayableAmount?.Value ?? 0,
                    CurrencyCode = invoice.DocumentCurrencyCode
                },
                
                RetrievedAt = DateTime.UtcNow
            };

            _logger.LogInformation("Retrieved invoice details for ID: {MessageId}, Invoice: {InvoiceId}", 
                messageId, invoice.Id);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get invoice details for ID: {MessageId}", messageId);
            return BadRequest(new ErrorResponse
            {
                Error = "get_details_failed",
                ErrorDescription = $"Failed to retrieve invoice details: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Downloads multiple invoices as a ZIP archive
    /// </summary>
    /// <param name="request">Bulk download request</param>
    /// <returns>ZIP file containing requested invoices</returns>
    [HttpPost("bulk-download")]
    [ProducesResponseType(typeof(FileResult), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> BulkDownload([FromBody] BulkDownloadRequest request)
    {
        try
        {
            _logger.LogInformation("Bulk downloading {Count} invoices, Format: {Format}", 
                request.MessageIds.Count, request.Format);

            if (request.MessageIds.Count == 0)
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "no_invoices",
                    ErrorDescription = "No invoice IDs provided"
                });
            }

            if (request.MessageIds.Count > 100) // Limit to prevent abuse
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "too_many_invoices",
                    ErrorDescription = "Maximum 100 invoices can be downloaded at once"
                });
            }

            using var zipStream = new System.IO.MemoryStream();
            using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create, true))
            {
                foreach (var messageId in request.MessageIds)
                {
                    try
                    {
                        byte[] fileData;
                        string fileName;
                        string mimeType;

                        switch (request.Format.ToLowerInvariant())
                        {
                            case "xml":
                                var invoice = await _eFacturaClient.DownloadInvoiceAsync(messageId);
                                // Serialize invoice to XML (placeholder)
                                var xmlContent = $"<!-- Invoice {messageId} XML content -->";
                                fileData = System.Text.Encoding.UTF8.GetBytes(xmlContent);
                                fileName = $"invoice_{messageId}.xml";
                                mimeType = "application/xml";
                                break;

                            case "raw":
                                fileData = await _eFacturaClient.DownloadRawInvoiceAsync(messageId);
                                fileName = $"invoice_{messageId}.zip";
                                mimeType = "application/zip";
                                break;

                            case "pdf":
                                var invoiceForPdf = await _eFacturaClient.DownloadInvoiceAsync(messageId);
                                fileData = await _eFacturaClient.ConvertToPdfAsync(invoiceForPdf);
                                fileName = $"invoice_{messageId}.pdf";
                                mimeType = "application/pdf";
                                break;

                            default:
                                continue; // Skip invalid formats
                        }

                        var zipEntry = archive.CreateEntry(fileName);
                        using var entryStream = zipEntry.Open();
                        await entryStream.WriteAsync(fileData);

                        _logger.LogDebug("Added invoice to ZIP: {MessageId}, Size: {Size} bytes", messageId, fileData.Length);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to add invoice to ZIP: {MessageId}", messageId);
                        // Continue with other invoices
                    }
                }
            }

            zipStream.Position = 0;
            var zipData = zipStream.ToArray();
            var zipFileName = $"invoices_bulk_{DateTime.UtcNow:yyyyMMdd_HHmmss}.zip";

            _logger.LogInformation("Bulk download completed. {Count} invoices requested, ZIP size: {Size} bytes", 
                request.MessageIds.Count, zipData.Length);

            return File(zipData, "application/zip", zipFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bulk download failed");
            return BadRequest(new ErrorResponse
            {
                Error = "bulk_download_failed",
                ErrorDescription = $"Bulk download failed: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Searches invoices by various criteria
    /// </summary>
    /// <param name="request">Search criteria</param>
    /// <returns>Matching invoices</returns>
    [HttpPost("search")]
    [ProducesResponseType(typeof(InvoiceSearchResponse), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(401)]
    public async Task<IActionResult> SearchInvoices([FromBody] InvoiceSearchRequest request)
    {
        try
        {
            _logger.LogInformation("Searching invoices for CIF: {Cif}, Criteria: {Criteria}", 
                request.Cif, System.Text.Json.JsonSerializer.Serialize(request));

            var allInvoices = await _eFacturaClient.GetInvoicesAsync(request.Cif, request.FromDate, request.ToDate);

            // Apply filters
            var filteredInvoices = allInvoices.AsEnumerable();

            if (!string.IsNullOrEmpty(request.InvoiceIdPattern))
            {
                filteredInvoices = filteredInvoices.Where(inv => 
                    inv.Id.Contains(request.InvoiceIdPattern, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(request.Type))
            {
                filteredInvoices = filteredInvoices.Where(inv => 
                    inv.Type?.Equals(request.Type, StringComparison.OrdinalIgnoreCase) == true);
            }

            var results = filteredInvoices.ToList();

            var response = new InvoiceSearchResponse
            {
                Cif = request.Cif,
                SearchCriteria = request,
                TotalFound = results.Count,
                Results = results.Select(inv => new InvoiceSummary
                {
                    Id = inv.Id,
                    Type = inv.Type,
                    Cif = inv.Cif,
                    CreationDate = inv.CreationDate,
                    RequestId = inv.RequestId
                }).ToList(),
                SearchedAt = DateTime.UtcNow
            };

            _logger.LogInformation("Search completed for CIF: {Cif}, Found: {Count} invoices", 
                request.Cif, results.Count);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice search failed for CIF: {Cif}", request.Cif);
            return BadRequest(new ErrorResponse
            {
                Error = "search_failed",
                ErrorDescription = $"Invoice search failed: {ex.Message}"
            });
        }
    }

    private static AddressInfo? GetAddressInfo(RomaniaEFacturaLibrary.Models.Ubl.Address? address)
    {
        if (address == null) return null;

        return new AddressInfo
        {
            StreetName = address.StreetName ?? "",
            CityName = address.CityName ?? "",
            PostalZone = address.PostalZone ?? "",
            CountryCode = address.Country?.IdentificationCode ?? ""
        };
    }
}

// DTOs for request/response
public class InvoiceListResponse
{
    public string Cif { get; set; } = string.Empty;
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
    public List<InvoiceSummary> Invoices { get; set; } = new();
    public DateTime RetrievedAt { get; set; }
}

public class InvoiceSummary
{
    public string Id { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string Cif { get; set; } = string.Empty;
    public DateTime CreationDate { get; set; }
    public string? RequestId { get; set; }
}

public class InvoiceDetailsResponse
{
    public string MessageId { get; set; } = string.Empty;
    public string InvoiceId { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public string InvoiceTypeCode { get; set; } = string.Empty;
    public string DocumentCurrencyCode { get; set; } = string.Empty;
    public PartyInfo Supplier { get; set; } = new();
    public PartyInfo Customer { get; set; } = new();
    public List<InvoiceLineInfo> InvoiceLines { get; set; } = new();
    public MonetaryInfo MonetaryTotals { get; set; } = new();
    public DateTime RetrievedAt { get; set; }
}

public class PartyInfo
{
    public string Name { get; set; } = string.Empty;
    public string CompanyId { get; set; } = string.Empty;
    public AddressInfo? Address { get; set; }
}

public class AddressInfo
{
    public string StreetName { get; set; } = string.Empty;
    public string CityName { get; set; } = string.Empty;
    public string PostalZone { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}

public class InvoiceLineInfo
{
    public string Id { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public class MonetaryInfo
{
    public decimal LineExtensionAmount { get; set; }
    public decimal TaxExclusiveAmount { get; set; }
    public decimal TaxInclusiveAmount { get; set; }
    public decimal PayableAmount { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
}

public class BulkDownloadRequest
{
    [Required]
    public List<string> MessageIds { get; set; } = new();
    
    public string Format { get; set; } = "xml"; // xml, raw, pdf
}

public class InvoiceSearchRequest
{
    [Required]
    public string Cif { get; set; } = string.Empty;
    
    public DateTime FromDate { get; set; } = DateTime.UtcNow.AddDays(-30);
    public DateTime ToDate { get; set; } = DateTime.UtcNow;
    public string? InvoiceIdPattern { get; set; }
    public string? Type { get; set; }
}

public class InvoiceSearchResponse
{
    public string Cif { get; set; } = string.Empty;
    public InvoiceSearchRequest SearchCriteria { get; set; } = new();
    public int TotalFound { get; set; }
    public List<InvoiceSummary> Results { get; set; } = new();
    public DateTime SearchedAt { get; set; }
}
