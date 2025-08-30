# Romania EFactura Library - Configuration Reference

This document provides a complete reference for all configuration options available in the Romania EFactura Library.

## Configuration Schema

### EFactura Section

The main configuration section in `appsettings.json`:

```json
{
  "EFactura": {
    "Environment": "Test",                    // "Test" or "Production"
    "CertificatePath": "",                   // Path to .pfx certificate file
    "CertificatePassword": "",               // Password for .pfx file
    "CertificateThumbprint": "",             // Certificate thumbprint (alternative to file)
    "CertificateStoreName": "My",            // Certificate store name (default: "My")
    "CertificateStoreLocation": "CurrentUser", // Store location (default: "CurrentUser")
    "CertificateSubject": "",                // Certificate subject (alternative identifier)
    "Cif": "",                               // Your company's CIF (required)
    "TimeoutSeconds": 30                     // HTTP timeout in seconds
  }
}
```

## Configuration Options Explained

### Environment Settings

| Value | Description | ANAF API Base URL |
|-------|-------------|-------------------|
| `Test` | Development/Testing environment | `https://api.anaf.ro/test/FCTEL/rest` |
| `Production` | Live production environment | `https://api.anaf.ro/prod/FCTEL/rest` |

### Certificate Configuration Options

Choose **ONE** of the following certificate configuration methods:

#### Option 1: Certificate File (.pfx)
```json
{
  "CertificatePath": "Certificates/certificate.pfx",
  "CertificatePassword": "your-password"
}
```

#### Option 2: Certificate Thumbprint (USB Token/Smart Card)
```json
{
  "CertificateThumbprint": "ABC123DEF456789...",
  "CertificateStoreName": "My",
  "CertificateStoreLocation": "CurrentUser"
}
```

#### Option 3: Certificate Subject
```json
{
  "CertificateSubject": "CN=Your Company Name, O=Your Organization",
  "CertificateStoreName": "My",
  "CertificateStoreLocation": "CurrentUser"
}
```

### Certificate Store Locations

| Value | Description |
|-------|-------------|
| `CurrentUser` | Certificate store for current user (default) |
| `LocalMachine` | System-wide certificate store |

### Certificate Store Names

| Value | Description |
|-------|-------------|
| `My` | Personal certificate store (default) |
| `Root` | Trusted root certificate store |
| `CA` | Intermediate certificate authorities |

### Company Information

| Parameter | Description | Format | Required |
|-----------|-------------|---------|----------|
| `Cif` | Romanian Fiscal Identification Code | Numbers only (e.g., "12345678") | ✅ Yes |

### Timeout Settings

| Parameter | Description | Default | Range |
|-----------|-------------|---------|-------|
| `TimeoutSeconds` | HTTP request timeout | 30 | 5-300 seconds |

## Environment-Specific Configurations

### Development Environment (appsettings.json)

```json
{
  "EFactura": {
    "Environment": "Test",
    "CertificatePath": "Certificates/test-certificate.pfx",
    "CertificatePassword": "test-password",
    "Cif": "12345678",
    "TimeoutSeconds": 30
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "RomaniaEFacturaLibrary": "Debug"
    }
  }
}
```

### Production Environment (appsettings.Production.json)

```json
{
  "EFactura": {
    "Environment": "Production",
    "CertificateThumbprint": "YOUR_CERT_THUMBPRINT",
    "Cif": "YOUR_REAL_CIF",
    "TimeoutSeconds": 60
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "RomaniaEFacturaLibrary": "Information"
    }
  }
}
```

## Security Best Practices

### 1. Certificate Security

```json
// ❌ DON'T: Store production certificates in source control
{
  "CertificatePath": "prod-certificate.pfx",
  "CertificatePassword": "production-password"
}

// ✅ DO: Use environment variables or secure vaults
{
  "CertificateThumbprint": "#{ENV_CERT_THUMBPRINT}",
  "Cif": "#{ENV_CIF}"
}
```

### 2. Password Management

```bash
# Use environment variables
export EFactura__CertificatePassword="secure-password"
export EFactura__Cif="12345678"

# Or user secrets for development
dotnet user-secrets set "EFactura:CertificatePassword" "your-password"
```

### 3. Azure Key Vault Integration

```csharp
// Program.cs
if (builder.Environment.IsProduction())
{
    builder.Configuration.AddAzureKeyVault(
        new Uri("https://your-vault.vault.azure.net/"),
        new DefaultAzureCredential());
}
```

## Configuration Validation

The library automatically validates configuration on startup. Common validation errors:

| Error | Cause | Solution |
|-------|-------|----------|
| "Invalid Environment" | Environment not "Test" or "Production" | Check spelling and case |
| "CIF is required" | Missing or empty CIF | Provide valid Romanian CIF |
| "Certificate not found" | Invalid certificate path/thumbprint | Verify certificate exists |
| "Certificate expired" | Certificate past expiry date | Renew certificate |

## Logging Configuration

### Recommended Logging Levels

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "RomaniaEFacturaLibrary.Services.Authentication": "Information",
      "RomaniaEFacturaLibrary.Services.Api": "Information",
      "RomaniaEFacturaLibrary.Services.Xml": "Warning",
      "System.Net.Http.HttpClient": "Warning"
    }
  }
}
```

### Debug Logging (Development Only)

```json
{
  "Logging": {
    "LogLevel": {
      "RomaniaEFacturaLibrary": "Debug",
      "System.Net.Http.HttpClient.RomaniaEFacturaLibrary": "Trace"
    }
  }
}
```

## Complete Configuration Examples

### Example 1: USB Token Configuration

```json
{
  "EFactura": {
    "Environment": "Production",
    "CertificateThumbprint": "1234567890ABCDEF1234567890ABCDEF12345678",
    "Cif": "12345678",
    "TimeoutSeconds": 60
  }
}
```

### Example 2: File-Based Certificate (Development)

```json
{
  "EFactura": {
    "Environment": "Test",
    "CertificatePath": "Certificates/dev-certificate.pfx",
    "CertificatePassword": "dev-password",
    "Cif": "12345678",
    "TimeoutSeconds": 30
  }
}
```

### Example 3: Azure Production Configuration

```json
{
  "EFactura": {
    "Environment": "Production",
    "CertificateThumbprint": "#{KeyVault.CertificateThumbprint}",
    "Cif": "#{KeyVault.CompanyCif}",
    "TimeoutSeconds": 90
  }
}
```

## Configuration Troubleshooting

### Common Issues

1. **Certificate Not Loading**
   - Verify certificate exists at specified path
   - Check certificate permissions
   - Ensure password is correct

2. **USB Token Not Recognized**
   - Install token drivers
   - Check certificate appears in Certificate Manager
   - Try different certificate identification method

3. **Authentication Failures**
   - Verify CIF is correct
   - Check certificate is not expired
   - Ensure using correct environment

### Debugging Configuration

Enable debug logging to see configuration loading:

```json
{
  "Logging": {
    "LogLevel": {
      "RomaniaEFacturaLibrary.Configuration": "Debug"
    }
  }
}
```

This will log:
- Configuration values being loaded
- Certificate loading attempts
- Environment selection
- Validation results

---

## Quick Setup Checklist

For a quick setup, ensure you have:

- [ ] Valid Romanian CIF
- [ ] Digital certificate (file or USB token)
- [ ] Certificate password (if using .pfx file)
- [ ] Correct environment setting
- [ ] Appropriate timeout values
- [ ] Secure configuration storage

Your Romania EFactura Library is ready to use!
