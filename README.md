# Romania EFactura Library v2.0.0

A comprehensive C# library for integrating with the Romanian EFactura (SPV - Spatiu Privat Virtual) system from ANAF. Now with flexible token storage and enhanced security features.

## 🚀 What's New in v2.0.0

- **🔄 Flexible Token Storage**: Choose between MemoryCache, Cookie, or custom storage
- **📝 CIF as Parameters**: CIF is now passed as method parameters instead of configuration
- **🛡️ Enhanced Security**: Secure cookie options and automatic token cleanup
- **� Comprehensive Examples**: Complete controller examples for all operations
- **🔧 Better API Design**: Internal API client, cleaner public interface
- **🧪 Extensive Testing**: Comprehensive unit test coverage

## 🚀 Quick Links

- **📋 [Token Storage Guide](TokenStorageGuide.md)** - Complete token management guide
- **⚙️ [Configuration Guide](CONFIGURATION_GUIDE.md)** - All configuration options  
- **📦 [Publishing Guide](PUBLISHING_GUIDE.md)** - NuGet publishing instructions
- **📊 [Project Summary](PROJECT_SUMMARY.md)** - Complete project overview
- **🔗 [Example Controllers](Examples/Controllers/)** - Ready-to-use API controllers

## Overview

This library provides a complete solution for:
- **🔐 OAuth2 Authentication** with flexible token storage (MemoryCache/Cookie)
- **📄 UBL 2.1 XML** invoice creation and validation
- **🌐 API Integration** with ANAF test and production environments
- **📋 Invoice Management** (upload, download, status tracking, bulk operations)
- **🔄 XML Processing** with proper namespace handling and validation
- **🛡️ Security Features** with automatic token refresh and secure storage

## 📁 Repository Structure

- **`RomaniaEFacturaLibrary/`** - Main library with all EFactura functionality
  - `Services/TokenStorage/` - Flexible token storage implementations
  - `Services/Api/` - Internal ANAF API client (now internal)
  - `Services/` - Public EFactura client and services
- **`Examples/Controllers/`** - Complete controller examples
  - `AuthenticationController.cs` - OAuth2 authentication flow
  - `InvoiceController.cs` - Invoice validation and upload
  - `InvoiceManagementController.cs` - Download and management
- **`RomaniaEFacturaLibrary.Tests/`** - Comprehensive test suite
  - `TokenStorage/` - Token storage service tests
  - `Services/` - Client and service tests
- **`RomaniaEFacturaConsole/`** - Interactive console application
- **Documentation**:
  - `TokenStorageGuide.md` - Complete token storage guide
  - `IMPLEMENTATION_GUIDE.md` - Step-by-step implementation
  - `CONFIGURATION_GUIDE.md` - Configuration reference

## ✨ Features

### 🔐 Authentication & Token Management
- **OAuth2 Authorization Flow** with ANAF
- **Flexible Token Storage**:
  - MemoryCache (server-side, fast)
  - Cookie (client-side, persistent)
  - Custom storage (database, Redis, etc.)
- **Automatic Token Refresh** with 5-minute buffer
- **User-Isolated Storage** based on HttpContext
- **Secure Cookie Options** (HttpOnly, Secure, SameSite)

### 📄 UBL 2.1 XML Support
- Complete UBL 2.1 invoice models
- Proper XML serialization/deserialization
- Romanian EFactura-specific customizations
- XML validation and formatting
- PDF conversion support

### 🌐 ANAF API Integration
- **Upload Invoices** to ANAF SPV with validation
- **Download Invoices** in multiple formats (XML, PDF, raw ZIP)
- **Status Tracking** with real-time monitoring
- **Bulk Operations** for multiple invoice management
- **Search and Filter** invoices by criteria
- Support for test and production environments

### 🔧 Developer Experience
- **Comprehensive Examples** with ready-to-use controllers
- **Extensive Documentation** and usage guides
- **Unit Test Coverage** for all major components
- **Dependency Injection** support with multiple configuration options
- **Internal API Design** - clean public interface
- List recent invoices with filtering

## 🚀 Quick Start

### 1. Installation

Install via NuGet Package Manager:

```xml
<PackageReference Include="RomaniaEFacturaLibrary" Version="2.0.0" />
```

Or via Package Manager Console:
```powershell
Install-Package RomaniaEFacturaLibrary -Version 2.0.0
```

### 2. Configuration

Configure in `appsettings.json`:

```json
{
  "EFactura": {
    "BaseUrl": "https://api.anaf.ro/prod/FCTEL/rest",
    "ClientId": "your-anaf-client-id",
    "ClientSecret": "your-anaf-client-secret", 
    "RedirectUri": "https://yourapp.com/auth/callback",
    "Scope": "efactura"
  }
}
```

### 3. Service Registration

Choose your preferred token storage method:

#### Option A: MemoryCache Storage (Default)
```csharp
// In Program.cs
builder.Services.AddEFacturaServices(builder.Configuration);
// or
builder.Services.AddEFacturaServicesWithMemoryCache(config =>
{
    config.BaseUrl = "https://api.anaf.ro/prod/FCTEL/rest";
    config.ClientId = "your-client-id";
    config.ClientSecret = "your-client-secret";
    config.RedirectUri = "https://yourapp.com/auth/callback";
});
```

#### Option B: Cookie Storage
```csharp
// In Program.cs
builder.Services.AddEFacturaServicesWithCookieStorage(builder.Configuration);
```

#### Option C: Custom Storage
```csharp
// Implement your own storage
public class DatabaseTokenStorage : ITokenStorageService
{
    // Your implementation...
}

// Register in DI
builder.Services.AddEFacturaServicesWithCustomStorage<DatabaseTokenStorage>(config => 
{
    // Configuration...
});
```

### 4. Basic Usage

**Note**: In v2.0.0, CIF is now passed as a parameter to methods instead of configuration.

#### Authentication Flow
```csharp
[ApiController]
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
            redirectUri: "https://yourapp.com/auth/callback",
            scope: "efactura"
        );
        return Redirect(authUrl);
    }
    
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string code)
    {
        var token = await _authService.GetAccessTokenAsync(
            code: code,
            clientId: "your-client-id",
            clientSecret: "your-client-secret",
            redirectUri: "https://yourapp.com/auth/callback"
        );
        
        // Token is automatically stored
        return Ok("Authentication successful");
    }
}
```

#### Invoice Operations
```csharp
[ApiController]
[Authorize]
public class InvoiceController : ControllerBase
{
    private readonly IEFacturaClient _eFacturaClient;
    
    public InvoiceController(IEFacturaClient eFacturaClient)
    {
        _eFacturaClient = eFacturaClient;
    }
    
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateInvoice([FromBody] ValidateRequest request)
    {
        var result = await _eFacturaClient.ValidateInvoiceAsync(
            invoice: request.Invoice,
            cif: request.Cif  // CIF now required as parameter
        );
        
        return Ok(result);
    }
    
    [HttpPost("upload")]
    public async Task<IActionResult> UploadInvoice([FromBody] UploadRequest request)
    {
        var result = await _eFacturaClient.UploadInvoiceAsync(
            invoice: request.Invoice,
            cif: request.Cif,
            environment: "prod"  // Optional: "test" or "prod"
        );
        
        return Ok(result);
    }
    
    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages([FromQuery] string cif)
    {
        var messages = await _eFacturaClient.GetInvoicesAsync(
            cif: cif,
            from: DateTime.UtcNow.AddDays(-30),
            to: DateTime.UtcNow
        );
        
        return Ok(messages);
    }
    
    [HttpGet("download/{messageId}")]
    public async Task<IActionResult> DownloadInvoice(string messageId)
    {
        var invoice = await _eFacturaClient.DownloadInvoiceAsync(messageId);
        return Ok(invoice);
    }
    
    [HttpGet("download/{messageId}/pdf")]
    public async Task<IActionResult> DownloadPdf(string messageId)
    {
        var invoice = await _eFacturaClient.DownloadInvoiceAsync(messageId);
        var pdfData = await _eFacturaClient.ConvertToPdfAsync(invoice);
        
        return File(pdfData, "application/pdf", $"invoice_{messageId}.pdf");
    }
}
```

```csharp
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
        // Step 1: Get authorization URL for ANAF OAuth
        var authUrl = _authService.GetAuthorizationUrl("efactura", "unique-state");
        return Redirect(authUrl);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string code, string state)
    {
        // Step 2: Exchange authorization code for access token
        var token = await _authService.ExchangeCodeForTokenAsync(code);
        if (token != null)
        {
            // Store token securely (session, database, etc.)
            _authService.SetToken(token);
            return Ok("Authentication successful");
        }
        
        return BadRequest("Authentication failed");
    }
}

public class InvoiceController : ControllerBase
{
    private readonly IEFacturaClient _eFacturaClient;

    public InvoiceController(IEFacturaClient eFacturaClient)
    {
        _eFacturaClient = eFacturaClient;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> UploadInvoice([FromBody] UblInvoice invoice)
    {
        // Upload to ANAF (token will be automatically used)
        var result = await _eFacturaClient.UploadInvoiceAsync(invoice);
        if (result.IsSuccess)
        {
            return Ok(new { UploadId = result.UploadId });
        }

        return BadRequest(result.Errors);
    }
}
```

## Testing

Run the test suite:

```bash
dotnet test RomaniaEFacturaLibrary.Tests
```

Use the console application for interactive testing:

```bash
cd RomaniaEFacturaConsole
dotnet run
```

## Requirements

- **.NET 9.0** or later
- **ANAF Application Registration** (ClientId and ClientSecret)
- **Registered Redirect URI** with ANAF for OAuth callback

## Key Dependencies

- `Microsoft.Extensions.DependencyInjection` - Dependency injection
- `Microsoft.Extensions.Logging` - Logging framework
- `Microsoft.Extensions.Http` - HTTP client factory
- `System.Security.Cryptography.X509Certificates` - Certificate handling
- `System.Text.Json` - JSON serialization

## Build Status

✅ **Solution builds successfully**  
✅ **All 29 unit tests pass**  
✅ **Release configuration ready**  
✅ **Multi-target support** (.NET 8.0 and .NET 9.0)  
✅ **NuGet package ready**

## 🔗 Repository Information

- **GitHub Repository**: [https://github.com/lucianbumb/RomaniaEFacturaSolution](https://github.com/lucianbumb/RomaniaEFacturaSolution)
- **Package ID**: `RomaniaEFacturaLibrary`
- **Current Version**: `1.0.0`
- **License**: MIT

## Contributing

This library follows Romanian EFactura specifications and UBL 2.1 standards.

## License

This project is provided as-is for educational and development purposes. Please ensure compliance with ANAF regulations and Romanian law when using in production.
