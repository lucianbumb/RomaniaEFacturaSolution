# Romania EFactura Library - Configuration Reference v2.0.0

This document provides a complete reference for all configuration options available in the Romania EFactura Library v2.0.0.

## 🆕 What's New in v2.0.0

- **🔄 Flexible Token Storage**: Choose between MemoryCache, Cookie, or custom storage
- **📝 CIF as Parameters**: CIF is now passed as method parameters instead of configuration
- **🛡️ Enhanced Security**: Secure cookie options and automatic token cleanup
- **🔧 Better API Design**: Internal API client, cleaner public interface

## Configuration Schema

### EFactura Section

The main configuration section in `appsettings.json`:

```json
{
  "EFactura": {
    "Environment": "Test",                    // "Test" or "Production"
    "ClientId": "",                          // Your ANAF application Client ID
    "ClientSecret": "",                      // Your ANAF application Client Secret
    "RedirectUri": "",                       // Registered OAuth callback URL
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

### OAuth2 Authentication Configuration

#### Required Parameters

| Parameter | Description | Format | Required |
|-----------|-------------|---------|----------|
| `ClientId` | Client ID from ANAF application registration | String | ✅ Yes |
| `ClientSecret` | Client Secret from ANAF application registration | String | ✅ Yes |
| `RedirectUri` | OAuth callback URL registered with ANAF | Valid URL | ✅ Yes |

#### Example Configuration
```json
{
  "EFactura": {
    "Environment": "Test",
    "ClientId": "your-anaf-client-id",
    "ClientSecret": "your-anaf-client-secret",
    "RedirectUri": "https://yourapp.com/auth/callback"
  }
}
```

### Timeout Settings

| Parameter | Description | Default | Range |
|-----------|-------------|---------|-------|
| `TimeoutSeconds` | HTTP request timeout | 30 | 5-300 seconds |

## OAuth2 Authentication Flow

The library implements the OAuth2 Authorization Code Flow with ANAF:

### Step 1: Authorization URL Generation
```csharp
// Generate URL to redirect user to ANAF for authentication
var authUrl = authService.GetAuthorizationUrl("efactura", "unique-state");
// User is redirected to: https://logincert.anaf.ro/anaf-oauth2/v1/authorize?...
```

### Step 2: Authorization Code Exchange  
```csharp
// After user authenticates, ANAF redirects back with authorization code
var token = await authService.ExchangeCodeForTokenAsync(authorizationCode);
// Library exchanges code for access_token and refresh_token
```

### Step 3: Token Usage
```csharp
// Library automatically uses access_token for API calls
var result = await eFacturaClient.UploadInvoiceAsync(invoice);
// If token expires, library automatically refreshes using refresh_token
```

## Environment-Specific Configurations

### Development Environment (appsettings.json)

```json
{
  "EFactura": {
    "Environment": "Test",
    "ClientId": "test-client-id",
    "ClientSecret": "test-client-secret",
    "RedirectUri": "https://localhost:5001/auth/callback",
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
    "ClientId": "prod-client-id",
    "ClientSecret": "prod-client-secret",
    "RedirectUri": "https://yourapp.com/auth/callback",
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

### 1. OAuth2 Security

```json
// ❌ DON'T: Store production secrets in source control
{
  "ClientId": "production-client-id",
  "ClientSecret": "production-secret"
}

// ✅ DO: Use environment variables or secure vaults
{
  "ClientId": "#{ENV_CLIENT_ID}",
  "ClientSecret": "#{ENV_CLIENT_SECRET}"
}
```

### 2. Secret Management

```bash
# Use environment variables
export EFactura__ClientSecret="secure-secret"
export EFactura__ClientId="your-client-id"

# Or user secrets for development
dotnet user-secrets set "EFactura:ClientSecret" "your-secret"
dotnet user-secrets set "EFactura:ClientId" "your-client-id"
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
| "ClientId is required" | Missing or empty ClientId | Provide valid ANAF Client ID |
| "ClientSecret is required" | Missing or empty ClientSecret | Provide valid ANAF Client Secret |
| "RedirectUri is required" | Missing or empty RedirectUri | Provide registered callback URL |
| "Invalid RedirectUri format" | Malformed URL | Ensure valid URL format |

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

### Example 1: Development Configuration

```json
{
  "EFactura": {
    "Environment": "Test",
    "ClientId": "dev-client-id-from-anaf",
    "ClientSecret": "dev-client-secret-from-anaf",
    "RedirectUri": "https://localhost:5001/auth/callback",
    "TimeoutSeconds": 30
  }
}
```

### Example 2: Production Configuration with Azure Key Vault

```json
{
  "EFactura": {
    "Environment": "Production",
    "ClientId": "#{KeyVault.ANAFClientId}",
    "ClientSecret": "#{KeyVault.ANAFClientSecret}",
    "RedirectUri": "https://yourapp.com/auth/callback",
    "TimeoutSeconds": 90
  }
}
```

### Example 3: Environment Variables Configuration

```bash
# Set environment variables
export EFactura__Environment="Production"
export EFactura__ClientId="your-production-client-id"
export EFactura__ClientSecret="your-production-client-secret"
export EFactura__RedirectUri="https://yourapp.com/auth/callback"
```

## Configuration Troubleshooting

### Common Issues

1. **OAuth Authentication Failures**
   - Verify ClientId and ClientSecret are correct
   - Ensure RedirectUri matches exactly what's registered with ANAF
   - Check that your ANAF application is approved and active

2. **Redirect URI Mismatch**
   - RedirectUri in configuration must exactly match ANAF registration
   - Include protocol (https://) and exact path
   - No trailing slashes unless registered that way

3. **Token Exchange Failures**
   - Verify authorization code is used immediately (short expiry)
   - Check network connectivity to ANAF OAuth endpoints
   - Ensure correct environment (Test vs Production)

### Debugging Configuration

Enable debug logging to see OAuth flow details:

```json
{
  "Logging": {
    "LogLevel": {
      "RomaniaEFacturaLibrary.Services.Authentication": "Debug"
    }
  }
}
```

This will log:
- Authorization URL generation
- Token exchange requests/responses
- Token refresh operations
- Authentication errors

## ANAF Application Registration

Before using the library, you must register your application with ANAF:

1. **Register Application**: Contact ANAF to register your OAuth2 application
2. **Get Credentials**: Receive ClientId and ClientSecret from ANAF
3. **Register Callback URL**: Provide your RedirectUri to ANAF
4. **Test Environment**: Start with Test environment before Production

---

## Quick Setup Checklist

For a quick setup, ensure you have:

- [ ] ANAF OAuth2 application registration complete
- [ ] Valid ClientId from ANAF
- [ ] Valid ClientSecret from ANAF  
- [ ] Registered RedirectUri with ANAF
- [ ] Correct environment setting (Test/Production)
- [ ] Secure configuration storage (no secrets in source control)

Your Romania EFactura Library is ready for OAuth2 authentication with ANAF!
