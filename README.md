# Romania EFactura Library

A comprehensive C# library for integrating with the Romanian EFactura (SPV - Spatiu Privat Virtual) system from ANAF.

## Overview

This library provides a complete solution for:
- **Authentication** with ANAF using OAuth2 and digital certificates
- **UBL 2.1 XML** invoice creation and validation
- **API Integration** with ANAF test and production environments
- **Invoice Management** (upload, download, status tracking)
- **XML Processing** with proper namespace handling and validation

## Projects Structure

- **`RomaniaEFacturaLibrary`** - Main library with all EFactura functionality
- **`RomaniaEFacturaConsole`** - Interactive console application for testing
- **`RomaniaEFacturaLibrary.Tests`** - Comprehensive unit test suite

## Features

### 🔐 Authentication
- OAuth2 authentication with digital certificates via browser
- JWT token support with automatic refresh
- Support for test and production environments
- ClientId/ClientSecret configuration from ANAF registration

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
    "ClientId": "YOUR_CLIENT_ID_FROM_ANAF_REGISTRATION",
    "ClientSecret": "YOUR_CLIENT_SECRET_FROM_ANAF_REGISTRATION",
    "RedirectUri": "https://localhost:7000/efactura-oauth",
    "Cif": "YOUR_COMPANY_CIF_HERE",
    "TimeoutSeconds": 30
  }
}
```

### 3. Dependency Injection Setup

```csharp
// In Program.cs
builder.Services.AddEFacturaServices(builder.Configuration);

// Or with custom configuration
builder.Services.AddEFacturaServices(options =>
{
    options.Environment = EFacturaEnvironment.Test;
    options.ClientId = "your-client-id";
    options.ClientSecret = "your-client-secret";
    options.RedirectUri = "https://yourapp.com/oauth-callback";
    options.Cif = "12345678";
});
```

### 4. OAuth2 Authentication (Required)

The library uses OAuth2 Authorization Code flow with browser-based authentication:

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
        // Step 1: Redirect user to ANAF OAuth page
        var authUrl = _authService.GetAuthorizationUrl("efactura");
        return Redirect(authUrl);
    }

    [HttpGet("oauth-callback")]
    public async Task<IActionResult> OAuthCallback(string code, string state)
    {
        // Step 2: Exchange authorization code for JWT token
        var tokenResponse = await _authService.ExchangeCodeForTokenAsync(code);
        
        if (tokenResponse != null)
        {
            // Authentication successful - redirect to main app
            return RedirectToAction("Dashboard");
        }
        
        return BadRequest("Authentication failed");
    }
}
```

### 5. Using the EFactura Client

```csharp
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
        try
        {
            // Validate invoice
            var validation = await _eFacturaClient.ValidateInvoiceAsync(invoice);
            if (!validation.Success)
            {
                return BadRequest(validation.Errors);
            }

            // Upload to ANAF
            var result = await _eFacturaClient.UploadInvoiceAsync(invoice);
            return Ok(new { UploadId = result.UploadId });
        }
        catch (AuthenticationException)
        {
            return Unauthorized("Please authenticate first");
        }
    }

    [HttpGet("invoices")]
    public async Task<IActionResult> GetInvoices(DateTime? from = null, DateTime? to = null)
    {
        try
        {
            var invoices = await _eFacturaClient.GetInvoicesAsync(from, to);
            return Ok(invoices);
        }
        catch (AuthenticationException)
        {
            return Unauthorized("Please authenticate first");
        }
    }

    [HttpGet("download/{messageId}")]
    public async Task<IActionResult> DownloadInvoice(string messageId)
    {
        try
        {
            var invoice = await _eFacturaClient.DownloadInvoiceAsync(messageId);
            return Ok(invoice);
        }
        catch (AuthenticationException)
        {
            return Unauthorized("Please authenticate first");
        }
    }
}
```

## Authentication Flow

The library implements the correct ANAF OAuth2 flow:

1. **Redirect to ANAF**: Call `GetAuthorizationUrl()` and redirect user's browser
2. **Certificate Selection**: ANAF prompts user to insert USB certificate device
3. **Authorization Code**: ANAF redirects back with authorization code
4. **Token Exchange**: Call `ExchangeCodeForTokenAsync()` to get JWT tokens
5. **API Access**: Use authenticated client for all subsequent operations

## Configuration Requirements

To use this library, you need:

1. **ANAF Application Registration**: Register your application with ANAF to get ClientId and ClientSecret
2. **Valid CIF**: Company fiscal identification code registered with ANAF for EFactura
3. **Digital Certificate**: For production use (development can use test environment)
4. **Redirect URI**: Must be registered with ANAF and match exactly

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

- **.NET 8.0** or later (supports .NET 8 and .NET 9)
- **Valid ANAF Application Registration** with ClientId/ClientSecret
- **Digital Certificate** from a qualified provider for production use
- **Valid CIF** registered with ANAF for EFactura

## Key Dependencies

- `Microsoft.Extensions.DependencyInjection` - Dependency injection
- `Microsoft.Extensions.Logging` - Logging framework
- `Microsoft.Extensions.Http` - HTTP client factory
- `System.Text.Json` - JSON serialization

## Error Handling

The library provides comprehensive error handling:

- `AuthenticationException`: OAuth2 authentication failures
- `EFacturaApiException`: API communication errors
- `ValidationException`: Invoice validation errors

Always wrap API calls in try-catch blocks to handle authentication expiration.

## Contributing

This library follows Romanian EFactura specifications and OAuth2 standards.

## License

This project is provided as-is for educational and development purposes. Please ensure compliance with ANAF regulations and Romanian law when using in production.
