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
- **`RomaniaEFacturaLibrary.Tests`** - Comprehensive unit test suite (20 tests)

## Features

### 🔐 Authentication
- OAuth2 authentication with X.509 digital certificates
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
    "CertificatePath": "path/to/certificate.pfx",
    "CertificatePassword": "certificate-password",
    "Cif": "your-company-cif",
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
    options.CertificatePath = "certificate.pfx";
    options.CertificatePassword = "password";
    options.Cif = "12345678";
});
```

### 4. Basic Usage

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
        // Validate invoice
        var validation = await _eFacturaClient.ValidateInvoiceAsync(invoice);
        if (!validation.IsValid)
        {
            return BadRequest(validation.Errors);
        }

        // Upload to ANAF
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
- **Digital Certificate** from a qualified provider for production use
- **Valid CIF** registered with ANAF for EFactura

## Key Dependencies

- `Microsoft.Extensions.DependencyInjection` - Dependency injection
- `Microsoft.Extensions.Logging` - Logging framework
- `Microsoft.Extensions.Http` - HTTP client factory
- `System.Security.Cryptography.X509Certificates` - Certificate handling
- `System.Text.Json` - JSON serialization

## Build Status

✅ **Solution builds successfully**  
✅ **All 20 unit tests pass**  
✅ **Release configuration ready**

## Contributing

This library follows Romanian EFactura specifications and UBL 2.1 standards.

## License

This project is provided as-is for educational and development purposes. Please ensure compliance with ANAF regulations and Romanian law when using in production.
