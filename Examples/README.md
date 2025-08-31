# Romania EFactura Library - Examples

This directory contains comprehensive examples demonstrating how to use the Romania EFactura Library v2.0.0.

## Controllers

### AuthenticationController
- **Purpose**: Demonstrates OAuth2 authentication flow with ANAF
- **Endpoints**:
  - `GET /api/auth/authorize` - Initiates OAuth2 authorization
  - `GET /api/auth/callback` - Handles OAuth2 callback
  - `GET /api/auth/token` - Retrieves current access token

### InvoiceController
- **Purpose**: Shows invoice validation and upload operations
- **Endpoints**:
  - `POST /api/invoice/validate` - Validates UBL 2.1 XML invoices
  - `POST /api/invoice/upload` - Uploads invoices to ANAF

### InvoiceManagementController
- **Purpose**: Demonstrates invoice management and retrieval
- **Endpoints**:
  - `GET /api/invoicemanagement/list` - Lists invoices with filtering
  - `GET /api/invoicemanagement/{id}/download` - Downloads invoice XML
  - `GET /api/invoicemanagement/{id}/pdf` - Downloads invoice PDF
  - `GET /api/invoicemanagement/messages` - Retrieves ANAF messages

## Configuration

All examples require proper dependency injection setup in your `Program.cs`:

```csharp
// Add Romania EFactura Library services
builder.Services.AddRomaniaEFacturaLibrary(builder.Configuration);

// Choose token storage method:
// Option 1: Memory Cache (default)
builder.Services.AddMemoryCacheTokenStorage();

// Option 2: Cookie Storage
// builder.Services.AddCookieTokenStorage();
```

## Required Configuration

Update your `appsettings.json`:

```json
{
  "EFactura": {
    "Environment": "Test", // or "Production"
    "ClientId": "your-oauth2-client-id",
    "ClientSecret": "your-oauth2-client-secret",
    "RedirectUri": "https://localhost:7000/api/auth/callback",
    "Scope": "efactura",
    "CertificateThumbprint": "your-certificate-thumbprint",
    "CertificateStoreName": "My",
    "CertificateStoreLocation": "CurrentUser"
  }
}
```

## Usage Notes

- All invoice operations now require the `cif` parameter
- Token storage is automatic based on your chosen implementation
- The library handles OAuth2 refresh tokens automatically
- Certificate-based authentication is supported for production use

## Testing

The examples include proper error handling, logging, and validation to serve as production-ready templates for your implementation.
