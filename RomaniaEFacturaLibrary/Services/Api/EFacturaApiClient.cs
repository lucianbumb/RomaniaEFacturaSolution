using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFacturaLibrary.Configuration;
using RomaniaEFacturaLibrary.Models.Api;
using RomaniaEFacturaLibrary.Models.Ubl;
using RomaniaEFacturaLibrary.Services.Authentication;
using RomaniaEFacturaLibrary.Services.Xml;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RomaniaEFacturaLibrary.Services.Api;

/// <summary>
/// Main client for interacting with ANAF EFactura API
/// </summary>
public interface IEFacturaApiClient
{
    /// <summary>
    /// Validates an invoice XML without uploading it for a specific CIF
    /// </summary>
    Task<ValidationResult> ValidateInvoiceAsync(string xmlContent, string cif, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Uploads an invoice to SPV for a specific CIF
    /// </summary>
    Task<UploadResponse> UploadInvoiceAsync(UblInvoice invoice, string cif, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Uploads an invoice XML directly for a specific CIF
    /// </summary>
    Task<UploadResponse> UploadInvoiceXmlAsync(string xmlContent, string cif, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the status of an uploaded invoice
    /// </summary>
    Task<StatusResponse> GetUploadStatusAsync(string uploadId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets list of messages/invoices for a specific CIF
    /// </summary>
    Task<MessagesResponse> GetMessagesAsync(string cif, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Downloads an invoice by ID
    /// </summary>
    Task<byte[]> DownloadInvoiceAsync(string messageId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Converts XML invoice to PDF
    /// </summary>
    Task<byte[]> ConvertXmlToPdfAsync(string xmlContent, string documentType = "FACT1", CancellationToken cancellationToken = default);
}

public class EFacturaApiClient : IEFacturaApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EFacturaConfig _config;
    private readonly IAuthenticationService _authService;
    private readonly IXmlService _xmlService;
    private readonly ILogger<EFacturaApiClient> _logger;

    public EFacturaApiClient(
        IHttpClientFactory httpClientFactory,
        IOptions<EFacturaConfig> config,
        IAuthenticationService authService,
        IXmlService xmlService,
        ILogger<EFacturaApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config.Value;
        _authService = authService;
        _xmlService = xmlService;
        _logger = logger;
    }

    public async Task<ValidationResult> ValidateInvoiceAsync(string xmlContent, string cif, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cif))
        {
            throw new ArgumentException("CIF parameter is required", nameof(cif));
        }

        _logger.LogInformation("Validating invoice XML for CIF: {Cif}", cif);
        
        using var httpClient = _httpClientFactory.CreateClient();
        
        // For validation, we may need to include CIF in the request URL or headers
        // Based on ANAF documentation, validation endpoint might need CIF parameter
        var content = new StringContent(xmlContent, Encoding.UTF8, "text/plain");
        var response = await httpClient.PostAsync($"{_config.BaseUrl}/validare/FACT1?cif={cif}", content, cancellationToken);
        
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Validation failed with status {StatusCode}: {Content}", response.StatusCode, responseContent);
            throw new EFacturaApiException($"Validation failed: {response.StatusCode}");
        }

        var result = JsonSerializer.Deserialize<ValidationResult>(responseContent);
        
        _logger.LogInformation("Validation completed for CIF {Cif}. Success: {Success}, Errors: {ErrorCount}", 
            cif, result?.Success, result?.Errors?.Count ?? 0);
            
        return result ?? new ValidationResult();
    }

    public async Task<UploadResponse> UploadInvoiceAsync(UblInvoice invoice, string cif, CancellationToken cancellationToken = default)
    {
        var xmlContent = await _xmlService.SerializeInvoiceAsync(invoice, cancellationToken);
        return await UploadInvoiceXmlAsync(xmlContent, cif, cancellationToken);
    }

    public async Task<UploadResponse> UploadInvoiceXmlAsync(string xmlContent, string cif, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cif))
        {
            throw new ArgumentException("CIF parameter is required", nameof(cif));
        }

        _logger.LogInformation("Uploading invoice to SPV for CIF: {Cif}", cif);
        
        // Get access token using the new authentication service
        var token = await _authService.GetValidAccessTokenAsync(null, cancellationToken);
        
        using var httpClient = _httpClientFactory.CreateClient();
        
        // Prepare request with CIF parameter
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_config.BaseUrl}/upload?standard=UBL&cif={cif}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(xmlContent, Encoding.UTF8, "text/plain");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        
        var response = await httpClient.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Upload failed with status {StatusCode}: {Content}", response.StatusCode, responseContent);
            throw new EFacturaApiException($"Upload failed: {response.StatusCode} - {responseContent}");
        }

        var result = JsonSerializer.Deserialize<UploadResponse>(responseContent);
        
        _logger.LogInformation("Invoice uploaded successfully for CIF {Cif}. Upload ID: {UploadId}", cif, result?.UploadId);
        
        return result ?? new UploadResponse();
    }

    public async Task<StatusResponse> GetUploadStatusAsync(string uploadId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting upload status for ID: {UploadId}", uploadId);
        
        var token = await _authService.GetValidAccessTokenAsync(null, cancellationToken);
        
        using var httpClient = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"{_config.BaseUrl}/stareMesaj?id_incarcare={uploadId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        var response = await httpClient.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Status check failed with status {StatusCode}: {Content}", response.StatusCode, responseContent);
            throw new EFacturaApiException($"Status check failed: {response.StatusCode}");
        }

        var result = JsonSerializer.Deserialize<StatusResponse>(responseContent);
        
        _logger.LogInformation("Status retrieved: {Status}", result?.Status);
        
        return result ?? new StatusResponse();
    }

    public async Task<MessagesResponse> GetMessagesAsync(string cif, DateTime? from = null, DateTime? to = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cif))
        {
            throw new ArgumentException("CIF parameter is required", nameof(cif));
        }

        _logger.LogInformation("Getting messages list for CIF: {Cif}", cif);
        
        var token = await _authService.GetValidAccessTokenAsync(null, cancellationToken);
        
        var url = $"{_config.BaseUrl}/listaMesajeFactura?cif={cif}";
        if (from.HasValue)
            url += $"&data_start={from.Value:yyyy-MM-dd}";
        if (to.HasValue)
            url += $"&data_sfarsit={to.Value:yyyy-MM-dd}";
        
        using var httpClient = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        var response = await httpClient.SendAsync(request, cancellationToken);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Messages list failed with status {StatusCode}: {Content}", response.StatusCode, responseContent);
            throw new EFacturaApiException($"Messages list failed: {response.StatusCode}");
        }

        var result = JsonSerializer.Deserialize<MessagesResponse>(responseContent);
        
        _logger.LogInformation("Retrieved {Count} messages for CIF: {Cif}", result?.Messages?.Count ?? 0, cif);
        
        return result ?? new MessagesResponse();
    }

    public async Task<byte[]> DownloadInvoiceAsync(string messageId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Downloading invoice with ID: {MessageId}", messageId);
        
        var token = await _authService.GetValidAccessTokenAsync(null, cancellationToken);
        
        using var httpClient = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"{_config.BaseUrl}/descarcare?id={messageId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        
        var response = await httpClient.SendAsync(request, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Download failed with status {StatusCode}: {Content}", response.StatusCode, errorContent);
            throw new EFacturaApiException($"Download failed: {response.StatusCode}");
        }

        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        
        _logger.LogInformation("Downloaded {Size} bytes for message {MessageId}", content.Length, messageId);
        
        return content;
    }

    public async Task<byte[]> ConvertXmlToPdfAsync(string xmlContent, string documentType = "FACT1", CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Converting XML to PDF");
        
        using var httpClient = _httpClientFactory.CreateClient();
        
        // This endpoint doesn't require authentication
        var content = new StringContent(xmlContent, Encoding.UTF8, "text/plain");
        var response = await httpClient.PostAsync($"https://webservicesp.anaf.ro/prod/FCTEL/rest/transformare/{documentType}", content, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("PDF conversion failed with status {StatusCode}: {Content}", response.StatusCode, errorContent);
            throw new EFacturaApiException($"PDF conversion failed: {response.StatusCode}");
        }

        var pdfContent = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        
        _logger.LogInformation("PDF conversion successful, generated {Size} bytes", pdfContent.Length);
        
        return pdfContent;
    }
}

/// <summary>
/// Exception thrown when API operations fail
/// </summary>
public class EFacturaApiException : Exception
{
    public EFacturaApiException(string message) : base(message) { }
    public EFacturaApiException(string message, Exception innerException) : base(message, innerException) { }
}
