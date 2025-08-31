# Romania EFactura Library - Complete Implementation Guide

This comprehensive guide will walk you through implementing the Romania EFactura Library in your ASP.NET Core application, from installation to production deployment with digital certificates.

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Installation](#installation)
3. [Digital Certificate Setup](#digital-certificate-setup)
4. [Project Configuration](#project-configuration)
5. [Dependency Injection Setup](#dependency-injection-setup)
6. [Creating Controllers and Endpoints](#creating-controllers-and-endpoints)
7. [Configuration Files](#configuration-files)
8. [Testing Your Implementation](#testing-your-implementation)
9. [Production Deployment](#production-deployment)
10. [Troubleshooting](#troubleshooting)

## Prerequisites

Before starting, ensure you have:

- **.NET 8.0 or .NET 9.0 SDK** installed
- **Visual Studio 2022** or **VS Code** with C# extension
- **Valid Romanian CIF** registered with ANAF for EFactura
- **Digital Certificate** from a qualified provider (for production) or test certificate
- **USB Token/Smart Card** with the digital certificate (if applicable)

## Installation

### Step 1: Install the NuGet Package

Add the Romania EFactura Library to your ASP.NET Core project:

#### Using Package Manager Console
```powershell
Install-Package RomaniaEFacturaLibrary
```

#### Using .NET CLI
```bash
dotnet add package RomaniaEFacturaLibrary
```

#### Using PackageReference in .csproj
```xml
<PackageReference Include="RomaniaEFacturaLibrary" Version="1.0.0" />
```

### Step 2: Verify Installation

After installation, your project should have the following dependencies automatically added:
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging
- Microsoft.Extensions.Http
- System.Security.Cryptography.X509Certificates

## Digital Certificate Setup

### Understanding Digital Certificates for EFactura

Romania's EFactura system requires digital certificates for authentication. These certificates can be:

1. **Qualified Digital Certificates** (for production)
   - Issued by authorized providers (CertSign, Zertificon, etc.)
   - Stored on USB tokens or smart cards
   - Required for production ANAF environment

2. **Test Certificates** (for development)
   - Self-signed certificates for testing
   - Provided by ANAF for sandbox environment

### Step 3: Certificate Installation and Access

#### Option A: USB Token/Smart Card Certificate

When you insert your USB token or smart card:

1. **Install Certificate Drivers**
   - Install the drivers provided by your certificate issuer
   - Ensure the certificate appears in Windows Certificate Store

2. **Locate Certificate Path**
   - Open `certmgr.msc` (Certificate Manager)
   - Navigate to **Personal > Certificates**
   - Find your certificate and note the **Thumbprint** or **Subject**

3. **Export Certificate (if needed)**
   - Right-click your certificate
   - Choose **All Tasks > Export**
   - Export as `.pfx` with password protection
   - Save to a secure location in your application

#### Option B: File-Based Certificate

If you have a `.pfx` certificate file:

1. **Secure Storage**
   ```
   YourProject/
   ├── Certificates/
   │   ├── test-certificate.pfx      # Test environment
   │   └── prod-certificate.pfx      # Production environment
   ├── appsettings.json
   └── appsettings.Production.json
   ```

2. **File Security**
   - Store certificates outside the web root
   - Use restrictive file permissions
   - Never commit certificates to source control

## Project Configuration

### Step 4: Create Configuration Models

If you want to extend the configuration, create additional models:

```csharp
// Models/EFacturaSettings.cs
public class EFacturaSettings
{
    public string Environment { get; set; } = "Test";
    public string CertificatePath { get; set; } = "";
    public string CertificatePassword { get; set; } = "";
    public string CertificateThumbprint { get; set; } = "";
    public string Cif { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 30;
    public bool EnableLogging { get; set; } = true;
    public string LogLevel { get; set; } = "Information";
}
```

## Dependency Injection Setup

### Step 5: Configure Services in Program.cs

```csharp
// Program.cs
using RomaniaEFacturaLibrary.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure EFactura Services
builder.Services.AddEFacturaServices(builder.Configuration);

// Alternative: Manual configuration
/*
builder.Services.AddEFacturaServices(options =>
{
    options.Environment = EFacturaEnvironment.Test; // or Production
    options.CertificatePath = "Certificates/certificate.pfx";
    options.CertificatePassword = "your-certificate-password";
    options.Cif = "12345678";
    options.TimeoutSeconds = 30;
});
*/

// Add logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
    logging.SetMinimumLevel(LogLevel.Information);
});

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
```

## Creating Controllers and Endpoints

### Step 6: Create EFactura Controller

Create a complete controller with all necessary endpoints:

```csharp
// Controllers/EFacturaController.cs
using Microsoft.AspNetCore.Mvc;
using RomaniaEFacturaLibrary.Services;
using RomaniaEFacturaLibrary.Models.Ubl;
using RomaniaEFacturaLibrary.Models.Api;

namespace YourProject.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EFacturaController : ControllerBase
    {
        private readonly IEFacturaClient _eFacturaClient;
        private readonly ILogger<EFacturaController> _logger;

        public EFacturaController(
            IEFacturaClient eFacturaClient,
            ILogger<EFacturaController> logger)
        {
            _eFacturaClient = eFacturaClient;
            _logger = logger;
        }

        /// <summary>
        /// Validates an invoice before uploading
        /// </summary>
        [HttpPost("validate")]
        public async Task<IActionResult> ValidateInvoice([FromBody] UblInvoice invoice)
        {
            try
            {
                _logger.LogInformation("Validating invoice {InvoiceId}", invoice.Id);
                
                var validation = await _eFacturaClient.ValidateInvoiceAsync(invoice);
                
                if (validation.IsValid)
                {
                    return Ok(new { 
                        IsValid = true, 
                        Message = "Invoice is valid" 
                    });
                }

                return BadRequest(new { 
                    IsValid = false, 
                    Errors = validation.Errors 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating invoice {InvoiceId}", invoice.Id);
                return StatusCode(500, new { Error = "Internal server error during validation" });
            }
        }

        /// <summary>
        /// Uploads an invoice to ANAF SPV
        /// </summary>
        [HttpPost("upload")]
        public async Task<IActionResult> UploadInvoice([FromBody] UblInvoice invoice)
        {
            try
            {
                _logger.LogInformation("Uploading invoice {InvoiceId}", invoice.Id);

                // First validate
                var validation = await _eFacturaClient.ValidateInvoiceAsync(invoice);
                if (!validation.IsValid)
                {
                    return BadRequest(new { 
                        Message = "Invoice validation failed", 
                        Errors = validation.Errors 
                    });
                }

                // Upload to ANAF
                var result = await _eFacturaClient.UploadInvoiceAsync(invoice);
                
                if (result.IsSuccess)
                {
                    return Ok(new { 
                        Success = true,
                        UploadId = result.UploadId,
                        Message = "Invoice uploaded successfully" 
                    });
                }

                return BadRequest(new { 
                    Success = false,
                    Errors = result.Errors 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading invoice {InvoiceId}", invoice.Id);
                return StatusCode(500, new { Error = "Internal server error during upload" });
            }
        }

        /// <summary>
        /// Checks the upload status of an invoice
        /// </summary>
        [HttpGet("status/{uploadId}")]
        public async Task<IActionResult> GetUploadStatus(string uploadId)
        {
            try
            {
                _logger.LogInformation("Checking status for upload {UploadId}", uploadId);
                
                var status = await _eFacturaClient.GetUploadStatusAsync(uploadId);
                
                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking status for upload {UploadId}", uploadId);
                return StatusCode(500, new { Error = "Internal server error checking status" });
            }
        }

        /// <summary>
        /// Lists recent invoices
        /// </summary>
        [HttpGet("invoices")]
        public async Task<IActionResult> GetInvoices(
            [FromQuery] int days = 30,
            [FromQuery] string? cif = null)
        {
            try
            {
                _logger.LogInformation("Retrieving invoices for last {Days} days", days);
                
                var filters = new Dictionary<string, object>();
                if (!string.IsNullOrEmpty(cif))
                {
                    filters["cif"] = cif;
                }

                var invoices = await _eFacturaClient.ListInvoicesAsync(days, filters);
                
                return Ok(invoices);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving invoices");
                return StatusCode(500, new { Error = "Internal server error retrieving invoices" });
            }
        }

        /// <summary>
        /// Downloads an invoice by ID
        /// </summary>
        [HttpGet("download/{invoiceId}")]
        public async Task<IActionResult> DownloadInvoice(string invoiceId)
        {
            try
            {
                _logger.LogInformation("Downloading invoice {InvoiceId}", invoiceId);
                
                var invoiceData = await _eFacturaClient.DownloadInvoiceAsync(invoiceId);
                
                if (invoiceData != null)
                {
                    return File(invoiceData, "application/xml", $"invoice-{invoiceId}.xml");
                }

                return NotFound(new { Message = "Invoice not found" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading invoice {InvoiceId}", invoiceId);
                return StatusCode(500, new { Error = "Internal server error downloading invoice" });
            }
        }

        /// <summary>
        /// Creates a sample invoice for testing
        /// </summary>
        [HttpPost("sample")]
        public IActionResult CreateSampleInvoice()
        {
            try
            {
                var sampleInvoice = new UblInvoice
                {
                    Id = $"SAMPLE-{DateTime.Now:yyyyMMdd}-{new Random().Next(1000, 9999)}",
                    IssueDate = DateTime.Now,
                    DueDate = DateTime.Now.AddDays(30),
                    DocumentCurrencyCode = "RON",
                    
                    AccountingSupplierParty = new Party
                    {
                        PartyLegalEntity = new PartyLegalEntity
                        {
                            RegistrationName = "Sample Company SRL",
                            CompanyId = "J40/12345/2020"
                        },
                        PartyTaxSchemes = new List<PartyTaxScheme>
                        {
                            new() { CompanyId = "RO12345678", TaxScheme = new TaxScheme { Id = "VAT" } }
                        },
                        PostalAddress = new PostalAddress
                        {
                            StreetName = "Sample Street 123",
                            CityName = "Bucharest",
                            PostalZone = "010101",
                            Country = new Country { IdentificationCode = "RO" }
                        }
                    },

                    AccountingCustomerParty = new Party
                    {
                        PartyLegalEntity = new PartyLegalEntity
                        {
                            RegistrationName = "Customer Company SRL",
                            CompanyId = "J12/54321/2019"
                        },
                        PartyTaxSchemes = new List<PartyTaxScheme>
                        {
                            new() { CompanyId = "RO87654321", TaxScheme = new TaxScheme { Id = "VAT" } }
                        },
                        PostalAddress = new PostalAddress
                        {
                            StreetName = "Customer Street 456",
                            CityName = "Cluj-Napoca",
                            PostalZone = "400001",
                            Country = new Country { IdentificationCode = "RO" }
                        }
                    },

                    InvoiceLines = new List<InvoiceLine>
                    {
                        new()
                        {
                            Id = "1",
                            InvoicedQuantity = new Quantity { Value = 2, UnitCode = "EA" },
                            LineExtensionAmount = new Amount { Value = 200, CurrencyId = "RON" },
                            Item = new Item 
                            { 
                                Name = "Sample Product",
                                Description = "Sample product description"
                            },
                            Price = new Price 
                            { 
                                PriceAmount = new Amount { Value = 100, CurrencyId = "RON" } 
                            },
                            TaxTotal = new TaxTotal
                            {
                                TaxAmount = new Amount { Value = 38, CurrencyId = "RON" },
                                TaxSubTotals = new List<TaxSubTotal>
                                {
                                    new()
                                    {
                                        TaxableAmount = new Amount { Value = 200, CurrencyId = "RON" },
                                        TaxAmount = new Amount { Value = 38, CurrencyId = "RON" },
                                        TaxCategory = new TaxCategory
                                        {
                                            Id = "S",
                                            Percent = 19,
                                            TaxScheme = new TaxScheme { Id = "VAT" }
                                        }
                                    }
                                }
                            }
                        }
                    },

                    TaxTotal = new TaxTotal
                    {
                        TaxAmount = new Amount { Value = 38, CurrencyId = "RON" }
                    },

                    LegalMonetaryTotal = new MonetaryTotal
                    {
                        LineExtensionAmount = new Amount { Value = 200, CurrencyId = "RON" },
                        TaxExclusiveAmount = new Amount { Value = 200, CurrencyId = "RON" },
                        TaxInclusiveAmount = new Amount { Value = 238, CurrencyId = "RON" },
                        PayableAmount = new Amount { Value = 238, CurrencyId = "RON" }
                    }
                };

                return Ok(sampleInvoice);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating sample invoice");
                return StatusCode(500, new { Error = "Internal server error creating sample invoice" });
            }
        }
    }
}
```

## Configuration Files

### Step 7: appsettings.json Configuration

Create comprehensive configuration for different environments:

#### appsettings.json (Development/Test)
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "RomaniaEFacturaLibrary": "Debug"
    }
  },
  "AllowedHosts": "*",
  "EFactura": {
    "Environment": "Test",
    "CertificatePath": "Certificates/test-certificate.pfx",
    "CertificatePassword": "test-password",
    "CertificateThumbprint": "",
    "Cif": "12345678",
    "TimeoutSeconds": 30
  }
}
```

#### appsettings.Production.json (Production)
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning",
      "RomaniaEFacturaLibrary": "Information"
    }
  },
  "EFactura": {
    "Environment": "Production",
    "CertificatePath": "Certificates/prod-certificate.pfx",
    "CertificatePassword": "production-password",
    "CertificateThumbprint": "",
    "Cif": "YOUR_REAL_CIF",
    "TimeoutSeconds": 60
  }
}
```

### Step 8: Secure Configuration Management

For production environments, use secure configuration:

#### Option A: Environment Variables
```bash
# Set these environment variables
EFactura__Environment=Production
EFactura__CertificatePath=/secure/path/certificate.pfx
EFactura__CertificatePassword=secure-password
EFactura__Cif=12345678
```

#### Option B: Azure Key Vault (Recommended for Azure)
```csharp
// Program.cs
builder.Configuration.AddAzureKeyVault(
    new Uri("https://your-keyvault.vault.azure.net/"),
    new DefaultAzureCredential());
```

#### Option C: User Secrets (Development)
```bash
dotnet user-secrets init
dotnet user-secrets set "EFactura:CertificatePassword" "your-password"
dotnet user-secrets set "EFactura:Cif" "12345678"
```

## Digital Certificate Configuration Details

### Step 9: USB Token/Smart Card Configuration

When using USB tokens or smart cards, follow these steps:

#### A. Identify Your Certificate

1. **Insert USB Token/Smart Card**
2. **Open Certificate Manager** (`certmgr.msc`)
3. **Navigate to Personal > Certificates**
4. **Find your certificate** and note:
   - **Subject**: Usually contains your company name
   - **Thumbprint**: Unique identifier
   - **Validity dates**: Ensure not expired

#### B. Configuration Options

**Option 1: Using Certificate Thumbprint** (Recommended for USB tokens)
```json
{
  "EFactura": {
    "Environment": "Production",
    "CertificateThumbprint": "ABC123DEF456...", // Copy from Certificate Manager
    "CertificatePath": "",  // Leave empty when using thumbprint
    "CertificatePassword": "", // Not needed for thumbprint
    "Cif": "12345678"
  }
}
```

**Option 2: Using Certificate Store Location**
```json
{
  "EFactura": {
    "Environment": "Production",
    "CertificateStoreName": "My",
    "CertificateStoreLocation": "CurrentUser",
    "CertificateSubject": "CN=Your Company Name",
    "Cif": "12345678"
  }
}
```

#### C. Update Configuration Service (if using custom certificate loading)

```csharp
// Services/CertificateService.cs
public class CertificateService
{
    public X509Certificate2 LoadCertificate(EFacturaConfig config)
    {
        // Load by thumbprint (USB token/smart card)
        if (!string.IsNullOrEmpty(config.CertificateThumbprint))
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            
            var certificates = store.Certificates
                .Find(X509FindType.FindByThumbprint, config.CertificateThumbprint, false);
                
            if (certificates.Count == 0)
                throw new InvalidOperationException($"Certificate with thumbprint {config.CertificateThumbprint} not found");
                
            return certificates[0];
        }
        
        // Load from file
        if (!string.IsNullOrEmpty(config.CertificatePath))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(
                config.CertificatePath, 
                config.CertificatePassword);
        }
        
        throw new InvalidOperationException("No certificate configuration provided");
    }
}
```

## Testing Your Implementation

### Step 10: Test Endpoints

Create a test controller to verify your setup:

```csharp
// Controllers/TestController.cs
[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    private readonly IEFacturaClient _eFacturaClient;

    public TestController(IEFacturaClient eFacturaClient)
    {
        _eFacturaClient = eFacturaClient;
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow });
    }

    [HttpGet("certificate")]
    public async Task<IActionResult> TestCertificate()
    {
        try
        {
            // This will test certificate loading and basic connectivity
            var sampleInvoice = new UblInvoice { Id = "TEST-001" };
            var validation = await _eFacturaClient.ValidateInvoiceAsync(sampleInvoice);
            
            return Ok(new { 
                CertificateLoaded = true, 
                Message = "Certificate and configuration are working" 
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { 
                CertificateLoaded = false, 
                Error = ex.Message 
            });
        }
    }
}
```

### Step 11: Manual Testing Steps

1. **Run your application**
   ```bash
   dotnet run
   ```

2. **Test health endpoint**
   ```
   GET https://localhost:5001/api/test/health
   ```

3. **Test certificate loading**
   ```
   GET https://localhost:5001/api/test/certificate
   ```

4. **Create and validate sample invoice**
   ```
   POST https://localhost:5001/api/efactura/sample
   POST https://localhost:5001/api/efactura/validate
   ```

## Production Deployment

### Step 12: Production Checklist

Before deploying to production:

#### Security Checklist
- [ ] Digital certificate is from authorized provider
- [ ] Certificate is not expired
- [ ] Certificate passwords are stored securely
- [ ] HTTPS is enforced
- [ ] Logging is configured appropriately
- [ ] Error handling doesn't expose sensitive information

#### Configuration Checklist
- [ ] Environment is set to "Production"
- [ ] Correct CIF is configured
- [ ] Certificate path is accessible
- [ ] Timeout values are appropriate for production
- [ ] Logging level is set to "Warning" or "Error"

#### Testing Checklist
- [ ] Test certificate loading in production environment
- [ ] Test ANAF connectivity
- [ ] Validate sample invoice
- [ ] Test error scenarios

### Step 13: Deployment Scripts

#### Docker Deployment
```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["YourProject.csproj", "."]
RUN dotnet restore "YourProject.csproj"
COPY . .
WORKDIR "/src"
RUN dotnet build "YourProject.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "YourProject.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Copy certificates (if using file-based certificates)
COPY Certificates/ /app/Certificates/

ENTRYPOINT ["dotnet", "YourProject.dll"]
```

#### Azure App Service
```bash
# Deploy to Azure App Service
az webapp deployment source config-zip \
  --resource-group myResourceGroup \
  --name myAppService \
  --src deployment.zip
```

## Troubleshooting

### Common Issues and Solutions

#### Issue 1: Certificate Not Found
**Error**: "Certificate with thumbprint XXX not found"

**Solutions**:
1. Verify certificate is installed in correct store
2. Check thumbprint value (remove spaces)
3. Ensure application has permission to access certificate store
4. Try running application as administrator (testing only)

#### Issue 2: Authentication Failed
**Error**: "OAuth2 authentication failed"

**Solutions**:
1. Verify certificate is valid and not expired
2. Check CIF is correctly configured
3. Ensure using correct environment (Test vs Production)
4. Verify certificate is issued by authorized provider

#### Issue 3: USB Token Not Recognized
**Error**: "Certificate not accessible"

**Solutions**:
1. Install USB token drivers
2. Insert token and wait for Windows recognition
3. Check certificate appears in Certificate Manager
4. Restart application after token insertion

#### Issue 4: Network Connectivity
**Error**: "Unable to connect to ANAF servers"

**Solutions**:
1. Check internet connectivity
2. Verify firewall allows HTTPS traffic
3. Test ANAF endpoints manually
4. Check proxy settings if applicable

### Debug Logging

Enable detailed logging for troubleshooting:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "RomaniaEFacturaLibrary": "Trace",
      "System.Net.Http.HttpClient": "Debug"
    }
  }
}
```

### Support and Resources

- **ANAF Official Documentation**: [https://www.anaf.ro](https://www.anaf.ro)
- **EFactura Technical Specifications**: Available on ANAF website
- **Certificate Providers**: CertSign, Zertificon, etc.
- **Library GitHub Repository**: [https://github.com/lucianbumb/RomaniaEFacturaSolution](https://github.com/lucianbumb/RomaniaEFacturaSolution)

---

## Summary

This guide covered:

1. ✅ **Package Installation** - Adding RomaniaEFacturaLibrary to your project
2. ✅ **Certificate Setup** - USB tokens, smart cards, and file-based certificates
3. ✅ **Configuration** - Complete appsettings.json setup for all environments
4. ✅ **Implementation** - Full controller with all EFactura endpoints
5. ✅ **Security** - Best practices for certificate and password management
6. ✅ **Testing** - Comprehensive testing approach
7. ✅ **Production Deployment** - Ready-to-deploy configuration
8. ✅ **Troubleshooting** - Common issues and solutions

Your Romania EFactura integration is now ready for production use!
