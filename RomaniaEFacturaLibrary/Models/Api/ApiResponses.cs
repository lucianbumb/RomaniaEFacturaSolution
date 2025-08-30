using System.Text.Json.Serialization;

namespace RomaniaEFacturaLibrary.Models.Api;

/// <summary>
/// Base response from ANAF API
/// </summary>
public class ApiResponse
{
    [JsonPropertyName("mesaj")]
    public string Message { get; set; } = string.Empty;
    
    [JsonPropertyName("eroare")]
    public string Error { get; set; } = string.Empty;
    
    [JsonPropertyName("titlu")]
    public string Title { get; set; } = string.Empty;
    
    public bool IsSuccess => string.IsNullOrEmpty(Error);
}

/// <summary>
/// Upload response from ANAF
/// </summary>
public class UploadResponse : ApiResponse
{
    [JsonPropertyName("id_incarcare")]
    public string UploadId { get; set; } = string.Empty;
}

/// <summary>
/// Status response for uploaded invoice
/// </summary>
public class StatusResponse : ApiResponse
{
    [JsonPropertyName("stare")]
    public string Status { get; set; } = string.Empty;
    
    [JsonPropertyName("detalii")]
    public string Details { get; set; } = string.Empty;
    
    [JsonPropertyName("validare")]
    public ValidationResult? Validation { get; set; }
}

/// <summary>
/// Validation result for invoice
/// </summary>
public class ValidationResult
{
    [JsonPropertyName("succes")]
    public bool Success { get; set; }
    
    [JsonPropertyName("erori")]
    public List<ValidationError> Errors { get; set; } = new();
    
    [JsonPropertyName("avertismente")]
    public List<ValidationWarning> Warnings { get; set; } = new();
}

/// <summary>
/// Validation error
/// </summary>
public class ValidationError
{
    [JsonPropertyName("cod")]
    public string Code { get; set; } = string.Empty;
    
    [JsonPropertyName("mesaj")]
    public string Message { get; set; } = string.Empty;
    
    [JsonPropertyName("xpath")]
    public string XPath { get; set; } = string.Empty;
}

/// <summary>
/// Validation warning
/// </summary>
public class ValidationWarning
{
    [JsonPropertyName("cod")]
    public string Code { get; set; } = string.Empty;
    
    [JsonPropertyName("mesaj")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// List messages response
/// </summary>
public class MessagesResponse : ApiResponse
{
    [JsonPropertyName("mesaje")]
    public List<MessageInfo> Messages { get; set; } = new();
}

/// <summary>
/// Message information
/// </summary>
public class MessageInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("data_creare")]
    public DateTime CreationDate { get; set; }
    
    [JsonPropertyName("cif")]
    public string Cif { get; set; } = string.Empty;
    
    [JsonPropertyName("tip")]
    public string Type { get; set; } = string.Empty;
    
    [JsonPropertyName("id_solicitare")]
    public string RequestId { get; set; } = string.Empty;
}
