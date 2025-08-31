# Romania EFactura Library

A comprehensive C# library for integrating with the Romanian EFactura (SPV - Spatiu Privat Virtual) system from ANAF.

## 🚀 Quick Links

- **📋 [Implementation Guide](IMPLEMENTATION_GUIDE.md)** - Complete step-by-step setup
- **⚙️ [Configuration Guide](CONFIGURATION_GUIDE.md)** - All configuration options  
- **📦 [Publishing Guide](PUBLISHING_GUIDE.md)** - NuGet publishing instructions
- **📊 [Project Summary](PROJECT_SUMMARY.md)** - Complete project overview

## Overview

This library provides a complete solution for:
- **Authentication** with ANAF using OAuth2 and digital certificates
- **UBL 2.1 XML** invoice creation and validation
- **API Integration** with ANAF test and production environments
- **Invoice Management** (upload, download, status tracking)
- **XML Processing** with proper namespace handling and validation

## 📁 Repository Structure

- **`RomaniaEFacturaLibrary/`** - Main library with all EFactura functionality
- **`RomaniaEFacturaConsole/`** - Interactive console application for testing
- **`RomaniaEFacturaLibrary.Tests/`** - Comprehensive unit test suite (29 tests)
- **`ExampleBlazorUsage/`** - Example Blazor application implementation
- **`documentation_efactura/`** - Official ANAF documentation and guides
- **Documentation Files**:
  - `IMPLEMENTATION_GUIDE.md` - Step-by-step implementation guide
  - `CONFIGURATION_GUIDE.md` - Complete configuration reference
  - `PUBLISHING_GUIDE.md` - NuGet publishing instructions
  - `PROJECT_SUMMARY.md` - Project overview and status

## Features

### 🔐 Authentication
- OAuth2 authorization code flow with ANAF
- Client credentials (ClientId, ClientSecret) from ANAF application registration
- Automatic token management and refresh
- Support for test and production environments

### 📄 UBL 2.1 XML Support
- Complete UBL 2.1 invoice models
- Proper XML serialization/deserialization
- Romanian EFactura-specific customizations
- XML validation and formatting

### 🌐 ANAF API Integration
- Upload invoices to ANAF SPV
- Check upload status and validation results
- Download invoices and attachments
- List recent invoices with filtering

### 🏗️ ASP.NET Core Ready
- Dependency injection support
- Configuration-based setup
- Logging integration
- Easy integration with web applications

## Quick Start

### 1. Installation

Add the library to your project:

```xml
<PackageReference Include="RomaniaEFacturaLibrary" Version="1.0.0" />
```

### 2. Configuration

Configure in `appsettings.json`:

```json
{
  "EFactura": {
    "Environment": "Test",
    "ClientId": "your-anaf-client-id",
    "ClientSecret": "your-anaf-client-secret", 
    "RedirectUri": "https://yourapp.com/auth/callback",
    "TimeoutSeconds": 30
  }
}
```

### 3. Dependency Injection Setup

```csharp
// In Program.cs or Startup.cs
services.AddEFacturaServices(configuration);

// Or with custom configuration
services.AddEFacturaServices(options =>
{
    options.Environment = EFacturaEnvironment.Test;
    options.ClientId = "your-client-id";
    options.ClientSecret = "your-client-secret";
    options.RedirectUri = "https://yourapp.com/auth/callback";
});
```

### 4. Basic Usage

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
