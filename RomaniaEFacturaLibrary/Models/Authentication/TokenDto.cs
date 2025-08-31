using System.Text.Json.Serialization;

namespace RomaniaEFacturaLibrary.Models.Authentication;

/// <summary>
/// Data transfer object for storing token information
/// </summary>
public class TokenDto
{
    /// <summary>
    /// The access token
    /// </summary>
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;
    
    /// <summary>
    /// The refresh token
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;
    
    /// <summary>
    /// Token type (usually "Bearer")
    /// </summary>
    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = "Bearer";
    
    /// <summary>
    /// Token expiration time in seconds
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
    
    /// <summary>
    /// Token scope
    /// </summary>
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
    /// Username associated with this token
    /// </summary>
    public string UserName { get; set; } = string.Empty;
    
    /// <summary>
    /// Checks if the token is still valid (not expired)
    /// </summary>
    [JsonIgnore]
    public bool IsValid => DateTime.UtcNow < ExpiresAt.AddMinutes(-5); // 5 minute buffer
    
    /// <summary>
    /// Checks if the token is expired
    /// </summary>
    [JsonIgnore]
    public bool IsExpired => !IsValid;
    
    /// <summary>
    /// Gets the time remaining before expiration
    /// </summary>
    [JsonIgnore]
    public TimeSpan TimeToExpiry => ExpiresAt - DateTime.UtcNow;
}
