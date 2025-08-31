# Romania E-Factura Library - Token Storage Guide

This library now provides flexible token storage options for OAuth2 authentication with the Romanian e-Factura system.

## Features

- **Flexible Token Storage**: Choose between MemoryCache or Cookie storage
- **CIF as Parameters**: CIF is now passed as method parameters instead of configuration
- **Automatic Token Refresh**: Tokens are automatically refreshed when needed
- **HttpContext Integration**: Works seamlessly with ASP.NET Core applications

## Configuration

### 1. Basic Setup with MemoryCache (Default)

```csharp
// In Program.cs or Startup.cs
builder.Services.AddEFacturaServices(config =>
{
    config.BaseUrl = "https://api.anaf.ro/prod/FCTEL/rest";
    config.ClientId = "your-client-id";
    config.ClientSecret = "your-client-secret";
    config.RedirectUri = "https://your-app.com/callback";
    config.Scope = "efactura";
});
```

### 2. Setup with Cookie Storage

```csharp
// In Program.cs or Startup.cs
builder.Services.AddEFacturaServicesWithCookieStorage(config =>
{
    config.BaseUrl = "https://api.anaf.ro/prod/FCTEL/rest";
    config.ClientId = "your-client-id";
    config.ClientSecret = "your-client-secret";
    config.RedirectUri = "https://your-app.com/callback";
    config.Scope = "efactura";
});
```

### 3. Setup with Custom Token Storage

```csharp
// Implement your own token storage
public class DatabaseTokenStorageService : ITokenStorageService
{
    // Implement interface methods...
}

// Register in DI container
builder.Services.AddEFacturaServicesWithCustomStorage<DatabaseTokenStorageService>(config =>
{
    config.BaseUrl = "https://api.anaf.ro/prod/FCTEL/rest";
    config.ClientId = "your-client-id";
    config.ClientSecret = "your-client-secret";
    config.RedirectUri = "https://your-app.com/callback";
    config.Scope = "efactura";
});
```

### 4. Setup from Configuration

```json
// appsettings.json
{
  "EFactura": {
    "BaseUrl": "https://api.anaf.ro/prod/FCTEL/rest",
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret",
    "RedirectUri": "https://your-app.com/callback",
    "Scope": "efactura"
  }
}
```

```csharp
// In Program.cs
builder.Services.AddEFacturaServices(builder.Configuration);
// or with Cookie storage
builder.Services.AddEFacturaServicesWithCookieStorage(builder.Configuration);
```

## Usage Examples

### 1. OAuth2 Authentication Flow

```csharp
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authService;
    
    public AuthController(IAuthenticationService authService)
    {
        _authService = authService;
    }
    
    [HttpGet("login")]
    public IActionResult Login()
    {
        var authUrl = _authService.GetAuthorizationUrl(
            clientId: "your-client-id",
            redirectUri: "https://your-app.com/callback",
            scope: "efactura",
            state: "random-state-value"
        );
        
        return Redirect(authUrl);
    }
    
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string code, string state)
    {
        try
        {
            var token = await _authService.GetAccessTokenAsync(
                code: code,
                clientId: "your-client-id",
                clientSecret: "your-client-secret",
                redirectUri: "https://your-app.com/callback"
            );
            
            // Token is automatically stored using the configured storage service
            // and associated with the current user from HttpContext
            
            return Ok("Authentication successful");
        }
        catch (Exception ex)
        {
            return BadRequest($"Authentication failed: {ex.Message}");
        }
    }
}
```

### 2. Using API with CIF Parameters

```csharp
[ApiController]
[Route("api/[controller]")]
public class InvoiceController : ControllerBase
{
    private readonly IEFacturaApiClient _apiClient;
    
    public InvoiceController(IEFacturaApiClient apiClient)
    {
        _apiClient = apiClient;
    }
    
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateInvoice([FromBody] ValidateRequest request)
    {
        try
        {
            var result = await _apiClient.ValidateInvoiceAsync(
                xmlContent: request.XmlContent,
                cif: request.Cif
            );
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest($"Validation failed: {ex.Message}");
        }
    }
    
    [HttpPost("upload")]
    public async Task<IActionResult> UploadInvoice([FromBody] UploadRequest request)
    {
        try
        {
            var result = await _apiClient.UploadInvoiceXmlAsync(
                xmlContent: request.XmlContent,
                cif: request.Cif,
                environment: "prod"
            );
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest($"Upload failed: {ex.Message}");
        }
    }
    
    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages(
        [FromQuery] string cif,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        try
        {
            var result = await _apiClient.GetMessagesAsync(
                cif: cif,
                from: from,
                to: to
            );
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest($"Failed to get messages: {ex.Message}");
        }
    }
}

public class ValidateRequest
{
    public string XmlContent { get; set; } = string.Empty;
    public string Cif { get; set; } = string.Empty;
}

public class UploadRequest
{
    public string XmlContent { get; set; } = string.Empty;
    public string Cif { get; set; } = string.Empty;
}
```

### 3. Manual Token Management

```csharp
[ApiController]
[Route("api/[controller]")]
public class TokenController : ControllerBase
{
    private readonly ITokenStorageService _tokenStorage;
    private readonly IAuthenticationService _authService;
    
    public TokenController(ITokenStorageService tokenStorage, IAuthenticationService authService)
    {
        _tokenStorage = tokenStorage;
        _authService = authService;
    }
    
    [HttpGet("status")]
    public async Task<IActionResult> GetTokenStatus()
    {
        var hasValidToken = await _tokenStorage.HasValidTokenAsync(HttpContext);
        return Ok(new { HasValidToken = hasValidToken });
    }
    
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken()
    {
        try
        {
            // This will automatically refresh the token if needed
            var token = await _authService.GetValidAccessTokenAsync();
            return Ok(new { Message = "Token refreshed successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest($"Token refresh failed: {ex.Message}");
        }
    }
    
    [HttpDelete("logout")]
    public async Task<IActionResult> Logout()
    {
        await _tokenStorage.RemoveTokenAsync(HttpContext);
        return Ok(new { Message = "Logged out successfully" });
    }
}
```

## Token Storage Options

### MemoryCache Storage
- **Pros**: Fast, server-side storage
- **Cons**: Lost on application restart, not shared across instances
- **Best for**: Single-instance applications, development

### Cookie Storage
- **Pros**: Persists across browser sessions, works with load balancers
- **Cons**: Limited size, sent with every request
- **Best for**: Web applications, multi-instance deployments

### Custom Storage
Implement `ITokenStorageService` for:
- Database storage
- Redis storage
- Encrypted file storage
- Any other storage mechanism

## Important Notes

1. **CIF Parameter**: All API methods now require CIF as a parameter instead of reading from configuration
2. **HttpContext Required**: Token storage services need HttpContext to identify the current user
3. **Automatic Refresh**: Tokens are automatically refreshed when they expire
4. **Security**: Cookies are configured with security flags (HttpOnly, Secure, SameSite)
5. **User Identification**: The library extracts username from various claim types in HttpContext

## Migration from Previous Versions

If you were using the library before this update:

1. **Remove CIF from configuration**: CIF is no longer part of `EFacturaConfig`
2. **Add CIF to method calls**: Pass CIF as parameter to API methods
3. **Choose storage method**: Use one of the new service registration methods
4. **Update authentication flow**: Ensure users are authenticated before using API methods

```csharp
// Old way
await _apiClient.ValidateInvoiceAsync(xmlContent);

// New way
await _apiClient.ValidateInvoiceAsync(xmlContent, cif: "12345678");
```
