using System.Text.Json.Serialization;

namespace RomaniaEFacturaLibrary.Models.Authentication;

/// <summary>
/// OAuth token response from ANAF
/// </summary>
public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;
    
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;
    
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
    
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;
    
    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;
    
    /// <summary>
    /// When the token was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the token expires
    /// </summary>
    public DateTime ExpiresAt => CreatedAt.AddSeconds(ExpiresIn);
    
    /// <summary>
    /// Check if the token is still valid
    /// </summary>
    public bool IsValid => DateTime.UtcNow < ExpiresAt.AddMinutes(-5); // 5 minute buffer
}

/// <summary>
/// OAuth error response from ANAF
/// </summary>
public class TokenErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;
    
    [JsonPropertyName("error_description")]
    public string ErrorDescription { get; set; } = string.Empty;
    
    [JsonPropertyName("error_uri")]
    public string ErrorUri { get; set; } = string.Empty;
}
