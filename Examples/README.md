# Romania EFactura Library - Examples v2.0.0

This directory contains comprehensive examples demonstrating how to use the Romania EFactura Library v2.0.0 with all its new features including flexible token storage and CIF parameter support.

## 🎯 Controller Examples

### 🔐 AuthenticationController.cs
**Purpose**: Complete OAuth2 authentication flow with ANAF  
**Features**: 
- Initiates OAuth2 authorization with ANAF
- Handles OAuth2 callback with proper error handling
- Retrieves and manages access tokens
- Digital certificate integration
- Secure token storage (automatic based on your chosen storage)

**Endpoints**:
- `GET /api/auth/authorize` - Redirects to ANAF OAuth2 authorization
- `GET /api/auth/callback` - Processes OAuth2 callback from ANAF  
- `GET /api/auth/token` - Retrieves current stored access token
- `POST /api/auth/refresh` - Manually refreshes access token

**Key Features**:
```csharp
// Automatic token storage - no manual token management needed
var authUrl = await _authService.GetAuthorizationUrlAsync();
// Token is automatically stored after successful callback
```

### 📄 InvoiceController.cs
**Purpose**: Invoice validation and upload operations  
**Features**:
- UBL 2.1 XML invoice validation
- Secure invoice upload to ANAF
- Comprehensive error handling and logging
- Request validation with proper models

**Endpoints**:
- `POST /api/invoice/validate` - Validates UBL 2.1 XML invoices
- `POST /api/invoice/upload` - Uploads invoices to ANAF SPV

**Key Features**:
```csharp
// CIF now passed as parameter (v2.0.0 change)
var result = await _eFacturaClient.ValidateInvoiceAsync(cif, xmlContent);
var uploadResult = await _eFacturaClient.UploadInvoiceAsync(cif, xmlContent);
```

### 📋 InvoiceManagementController.cs  
**Purpose**: Complete invoice management and retrieval operations  
**Features**:
- List invoices with advanced filtering
- Download invoices in multiple formats (XML, PDF)
- Retrieve ANAF messages and notifications
- Bulk operations support

**Endpoints**:
- `GET /api/invoicemanagement/list` - Lists invoices with filtering options
- `GET /api/invoicemanagement/{id}/download` - Downloads invoice XML
- `GET /api/invoicemanagement/{id}/pdf` - Downloads invoice as PDF
- `GET /api/invoicemanagement/messages` - Retrieves ANAF messages
- `GET /api/invoicemanagement/search` - Advanced invoice search

**Key Features**:
```csharp
// Flexible filtering and search
var invoices = await _eFacturaClient.GetInvoicesAsync(cif, days: 30);
var messages = await _eFacturaClient.GetMessagesAsync(cif, days: 7);
var pdfBytes = await _eFacturaClient.DownloadInvoicePdfAsync(messageId);
```

## ⚙️ Configuration Setup

### 1. Dependency Injection (Program.cs)

Choose your preferred token storage method:

#### Option A: MemoryCache Storage (Recommended for Development)
```csharp
builder.Services.AddRomaniaEFacturaLibrary(builder.Configuration);
builder.Services.AddMemoryCacheTokenStorage();
```

#### Option B: Cookie Storage (Recommended for Web Applications)
```csharp
builder.Services.AddRomaniaEFacturaLibrary(builder.Configuration);
builder.Services.AddCookieTokenStorage();
```

#### Option C: Custom Storage (For Production/Enterprise)
```csharp
builder.Services.AddRomaniaEFacturaLibrary(builder.Configuration);
builder.Services.AddScoped<ITokenStorageService, YourCustomTokenStorage>();
```

### 2. Configuration (appsettings.json)

```json
{
  "EFactura": {
    "Environment": "Test", // "Test" or "Production"
    "ClientId": "your-oauth2-client-id",
    "ClientSecret": "your-oauth2-client-secret", 
    "RedirectUri": "https://localhost:7000/api/auth/callback",
    "Scope": "efactura",
    "CertificateThumbprint": "your-certificate-thumbprint", // Optional
    "CertificateStoreName": "My", // Optional
    "CertificateStoreLocation": "CurrentUser" // Optional
  }
}
```

### 3. Required NuGet Packages

```xml
<PackageReference Include="RomaniaEFacturaLibrary" Version="2.0.0" />
<PackageReference Include="Microsoft.AspNetCore.Authentication" Version="2.2.0" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="9.0.0" />
```

## 🚀 Usage Examples

### Complete Authentication Flow
```csharp
// 1. User clicks login
[HttpGet("login")]
public async Task<IActionResult> Login()
{
    var authUrl = await _authService.GetAuthorizationUrlAsync();
    return Redirect(authUrl);
}

// 2. ANAF redirects back with code
[HttpGet("callback")]  
public async Task<IActionResult> Callback([FromQuery] string code)
{
    var token = await _authService.GetAccessTokenAsync(code);
    // Token automatically stored - ready to use library!
    return Ok("Authentication successful");
}

// 3. Use the library - token automatically retrieved
[HttpPost("upload-invoice")]
public async Task<IActionResult> UploadInvoice([FromBody] UploadRequest request)
{
    // Library automatically uses stored token
    var result = await _eFacturaClient.UploadInvoiceAsync(request.Cif, request.XmlContent);
    return Ok(result);
}
```

### Invoice Management Workflow
```csharp
// Upload invoice
var uploadResult = await _eFacturaClient.UploadInvoiceAsync("12345678", xmlContent);

// Check status  
var invoices = await _eFacturaClient.GetInvoicesAsync("12345678", days: 1);

// Download processed invoice
var invoice = await _eFacturaClient.DownloadInvoiceAsync(messageId);

// Get as PDF
var pdfBytes = await _eFacturaClient.DownloadInvoicePdfAsync(messageId);
```

## 🔄 Migration from v1.x

### Key Changes in v2.0.0
1. **CIF as Parameter**: Pass CIF to each method instead of configuration
2. **Token Storage**: Choose storage method during DI registration  
3. **Internal API**: Use `IEFacturaClient` instead of `EFacturaApiClient`

### Before (v1.x)
```csharp
// Configuration-based CIF
await client.ValidateInvoiceAsync(xmlContent);

// Manual token management
var token = await auth.GetTokenAsync();
client.SetToken(token);
```

### After (v2.0.0)  
```csharp
// Parameter-based CIF
await client.ValidateInvoiceAsync(cif, xmlContent);

// Automatic token management - nothing to do!
// Library handles token storage/retrieval automatically
```

## 🧪 Testing the Examples

### 1. Run the Example Project
```bash
cd Examples
dotnet run
```

### 2. Test Authentication Flow
1. Navigate to `/api/auth/authorize`
2. Complete ANAF OAuth flow
3. Check `/api/auth/token` to verify token storage

### 3. Test Invoice Operations
```bash
# Validate invoice
curl -X POST "https://localhost:7000/api/invoice/validate" \
  -H "Content-Type: application/json" \
  -d '{"cif": "12345678", "xmlContent": "<Invoice>...</Invoice>"}'

# Upload invoice  
curl -X POST "https://localhost:7000/api/invoice/upload" \
  -H "Content-Type: application/json" \
  -d '{"cif": "12345678", "xmlContent": "<Invoice>...</Invoice>"}'
```

## 📋 Request/Response Models

### ValidateInvoiceRequest
```csharp
public class ValidateInvoiceRequest
{
    public string Cif { get; set; } = string.Empty;
    public string XmlContent { get; set; } = string.Empty;
}
```

### UploadInvoiceRequest
```csharp
public class UploadInvoiceRequest  
{
    public string Cif { get; set; } = string.Empty;
    public string XmlContent { get; set; } = string.Empty;
}
```

### GetInvoicesRequest
```csharp
public class GetInvoicesRequest
{
    public string Cif { get; set; } = string.Empty;
    public int Days { get; set; } = 30;
}
```

## 🔒 Security Considerations

### Token Storage Security
- **MemoryCache**: Tokens stored in server memory, lost on restart
- **Cookie**: Tokens stored in HTTP-only, secure cookies
- **Custom**: Implement database/Redis storage for enterprise scenarios

### Production Configuration
```json
{
  "EFactura": {
    "Environment": "Production",
    "BaseUrl": "https://api.anaf.ro/prod/FCTEL/rest", 
    "ClientId": "production-client-id",
    "ClientSecret": "production-client-secret",
    "CertificateThumbprint": "production-certificate-thumbprint"
  }
}
```

## 🎯 Production Ready Features

- ✅ **Comprehensive Error Handling**: All controllers include proper error handling
- ✅ **Request Validation**: Input validation with clear error messages  
- ✅ **Logging Integration**: Structured logging throughout
- ✅ **Async/Await**: Non-blocking operations
- ✅ **Dependency Injection**: Proper DI container usage
- ✅ **Security**: Secure token storage and transmission
- ✅ **Documentation**: Complete API documentation with Swagger

## 📖 Additional Resources

- **[Token Storage Guide](../TokenStorageGuide.md)** - Detailed token management guide
- **[Main README](../README.md)** - Complete library documentation  
- **[API Tests](../RomaniaEFacturaLibrary.Tests/)** - Unit test examples
- **[Console App](../RomaniaEFacturaConsole/)** - Interactive testing tool

The examples are designed to be production-ready templates that you can copy and adapt for your specific use case!
