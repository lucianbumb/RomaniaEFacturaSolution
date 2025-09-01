using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFacturaLibrary.Configuration;
using RomaniaEFacturaLibrary.Models.Api;
using RomaniaEFacturaLibrary.Models.Ubl;
using RomaniaEFacturaLibrary.Services.Api;
using RomaniaEFacturaLibrary.Services.Xml;
using System.IO.Compression;

namespace RomaniaEFacturaLibrary.Services;

/// <summary>
/// Main client for EFactura operations
/// </summary>
public interface IEFacturaClient
{
    /// <summary>
    /// Validates an invoice before uploading for a specific CIF
    /// </summary>
    Task<ValidationResult> ValidateInvoiceAsync(UblInvoice invoice, string cif, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Uploads an invoice to SPV for a specific CIF
    /// </summary>
    Task<UploadResponse> UploadInvoiceAsync(UblInvoice invoice, string cif, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the status of an uploaded invoice
    /// </summary>
    Task<StatusResponse> GetUploadStatusAsync(string uploadId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Waits for upload to complete and returns final status
    /// </summary>
    Task<StatusResponse> WaitForUploadCompletionAsync(string uploadId, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets invoices from a date range for a specific CIF
    /// </summary>
    Task<List<UblInvoice>> GetInvoicesAsync(string cif, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Downloads an invoice and extracts the XML content
    /// </summary>
    Task<UblInvoice> DownloadInvoiceAsync(string messageId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Downloads raw invoice data (ZIP file)
    /// </summary>
    Task<byte[]> DownloadRawInvoiceAsync(string messageId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Converts an invoice to PDF
    /// </summary>
    Task<byte[]> ConvertToPdfAsync(UblInvoice invoice, CancellationToken cancellationToken = default);
}

public class EFacturaClient : IEFacturaClient
{
    private readonly IEFacturaApiClient _apiClient;
    private readonly IXmlService _xmlService;
    private readonly EFacturaConfig _config;
    private readonly ILogger<EFacturaClient> _logger;

    public EFacturaClient(
        IEFacturaApiClient apiClient,
        IXmlService xmlService,
        IOptions<EFacturaConfig> config,
        ILogger<EFacturaClient> logger)
    {
        _apiClient = apiClient;
        _xmlService = xmlService;
        _config = config.Value;
        _logger = logger;
    }

    public async Task<ValidationResult> ValidateInvoiceAsync(UblInvoice invoice, string cif, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cif))
        {
            throw new ArgumentException("CIF parameter is required", nameof(cif));
        }

        _logger.LogInformation("Validating invoice {InvoiceId} for CIF: {Cif}", invoice.Id, cif);
        
        // First validate locally
        var xmlContent = await _xmlService.SerializeInvoiceAsync(invoice, cancellationToken);
        var localValidation = await _xmlService.ValidateXmlAsync(xmlContent, cancellationToken);
        
        if (!localValidation.IsValid)
        {
            _logger.LogWarning("Local validation failed for invoice {InvoiceId}", invoice.Id);
            return new ValidationResult
            {
                Success = false,
                Errors = localValidation.Errors.Select(e => new ValidationError { Message = e }).ToList()
            };
        }
        
        // Then validate with ANAF
        return await _apiClient.ValidateInvoiceAsync(xmlContent, cif, cancellationToken);
    }

    public async Task<UploadResponse> UploadInvoiceAsync(UblInvoice invoice, string cif, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cif))
        {
            throw new ArgumentException("CIF parameter is required", nameof(cif));
        }

        _logger.LogInformation("Uploading invoice {InvoiceId} for CIF: {Cif}", invoice.Id, cif);
        
        // Validate first
        var validation = await ValidateInvoiceAsync(invoice, cif, cancellationToken);
        if (!validation.Success)
        {
            var errors = string.Join(", ", validation.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"Invoice validation failed: {errors}");
        }
        
        return await _apiClient.UploadInvoiceAsync(invoice, cif, cancellationToken);
    }

    public async Task<StatusResponse> GetUploadStatusAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        return await _apiClient.GetUploadStatusAsync(uploadId, cancellationToken);
    }

    public async Task<StatusResponse> WaitForUploadCompletionAsync(string uploadId, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var maxWait = timeout ?? TimeSpan.FromMinutes(10);
        var start = DateTime.UtcNow;
        var delay = TimeSpan.FromSeconds(5);
        
        _logger.LogInformation("Waiting for upload {UploadId} to complete (max wait: {MaxWait})", uploadId, maxWait);
        
        while (DateTime.UtcNow - start < maxWait)
        {
            var status = await GetUploadStatusAsync(uploadId, cancellationToken);
            
            _logger.LogDebug("Upload {UploadId} status: {Status}", uploadId, status.Status);
            
            // Check if processing is complete
            if (IsUploadComplete(status.Status))
            {
                _logger.LogInformation("Upload {UploadId} completed with status: {Status}", uploadId, status.Status);
                return status;
            }
            
            // Wait before checking again
            await Task.Delay(delay, cancellationToken);
        }
        
        _logger.LogWarning("Upload {UploadId} did not complete within {MaxWait}", uploadId, maxWait);
        
        // Return last known status
        return await GetUploadStatusAsync(uploadId, cancellationToken);
    }

    public async Task<List<UblInvoice>> GetInvoicesAsync(string cif, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cif))
        {
            throw new ArgumentException("CIF parameter is required", nameof(cif));
        }

        _logger.LogInformation("Getting invoices for CIF: {Cif} from {From} to {To}", cif, from, to);
        
        var response = await _apiClient.GetMessagesAsync(cif, from, to, cancellationToken);
        
        var invoices = new List<UblInvoice>();
        
        foreach (var message in response.Messages)
        {
            try
            {
                var invoice = await DownloadInvoiceAsync(message.Id, cancellationToken);
                invoices.Add(invoice);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download invoice {MessageId}", message.Id);
                // Continue with other invoices
            }
        }
        
        return invoices;
    }

    public async Task<UblInvoice> DownloadInvoiceAsync(string messageId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Downloading and parsing invoice {MessageId}", messageId);
        
        var rawData = await _apiClient.DownloadInvoiceAsync(messageId, cancellationToken);
        
        // Extract XML from ZIP
        var xmlContent = ExtractXmlFromZip(rawData, messageId);
        
        // Parse to UBL invoice
        return await _xmlService.DeserializeInvoiceAsync(xmlContent, cancellationToken);
    }

    public async Task<byte[]> DownloadRawInvoiceAsync(string messageId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Downloading raw invoice data {MessageId}", messageId);
        
        return await _apiClient.DownloadInvoiceAsync(messageId, cancellationToken);
    }

    public async Task<byte[]> ConvertToPdfAsync(UblInvoice invoice, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Converting invoice {InvoiceId} to PDF", invoice.Id);
        
        var xmlContent = await _xmlService.SerializeInvoiceAsync(invoice, cancellationToken);
        return await _apiClient.ConvertXmlToPdfAsync(xmlContent, "FACT1", cancellationToken);
    }

    private static bool IsUploadComplete(string status)
    {
        // Common status values that indicate completion
        var completedStatuses = new[] { "procesat", "finalizat", "respins", "eroare", "completed", "processed", "rejected", "error" };
        return completedStatuses.Any(s => status.Contains(s, StringComparison.OrdinalIgnoreCase));
    }

    private string ExtractXmlFromZip(byte[] zipData, string messageId)
    {
        try
        {
            using var memoryStream = new MemoryStream(zipData);
            using var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read);
            
            // Look for XML file (usually named with message ID or invoice number)
            var xmlEntry = archive.Entries.FirstOrDefault(e => 
                e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && 
                !e.Name.StartsWith("semnatura_", StringComparison.OrdinalIgnoreCase));
            
            if (xmlEntry == null)
            {
                throw new InvalidOperationException($"No XML file found in ZIP archive for message {messageId}");
            }
            
            using var entryStream = xmlEntry.Open();
            using var reader = new StreamReader(entryStream, System.Text.Encoding.UTF8);
            
            var xmlContent = reader.ReadToEnd();
            
            _logger.LogDebug("Extracted XML content from {FileName} ({Length} characters)", xmlEntry.Name, xmlContent.Length);
            
            return _xmlService.CleanXml(xmlContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract XML from ZIP for message {MessageId}", messageId);
            throw new InvalidOperationException($"Failed to extract XML from ZIP: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Information about an invoice
/// </summary>
public class InvoiceInfo
{
    public string Id { get; set; } = string.Empty;
    public DateTime CreationDate { get; set; }
    public string Cif { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
}
