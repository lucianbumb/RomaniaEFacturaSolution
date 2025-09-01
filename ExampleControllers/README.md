# Romania EFactura Library - Example Controllers

This directory contains sample ASP.NET Core controllers demonstrating how to use the Romania EFactura Library in web applications.

## Controllers Overview

### 1. AuthController.cs
**OAuth2 Authentication Management**

Handles the complete OAuth2 authentication flow with ANAF:

- `GET /api/auth/login` - Initiates OAuth2 flow by redirecting to ANAF
- `GET /api/auth/callback` - Handles OAuth2 callback with authorization code
- `GET /api/auth/status` - Checks current authentication status
- `POST /api/auth/refresh` - Manually refreshes access token
- `POST /api/auth/logout` - Clears session data

**Key Features:**
- CSRF protection with state parameter
- Session-based token storage
- Automatic token refresh handling
- Comprehensive error handling

### 2. EFacturaController.cs
**EFactura Invoice Operations**

Provides complete invoice management functionality:

- `GET /api/efactura/dashboard` - Authentication status and quick stats
- `POST /api/efactura/validate` - Validates invoice before upload
- `POST /api/efactura/upload` - Uploads invoice to ANAF SPV
- `GET /api/efactura/upload/{uploadId}/status` - Checks upload status
- `POST /api/efactura/upload/{uploadId}/wait` - Waits for upload completion
- `GET /api/efactura/invoices` - Lists invoices for date range
- `GET /api/efactura/invoices/{messageId}` - Downloads specific invoice
- `GET /api/efactura/invoices/{messageId}/raw` - Downloads raw ZIP file
- `GET /api/efactura/invoices/{messageId}/pdf` - Converts invoice to PDF

**Key Features:**
- Authentication requirement enforcement
- Comprehensive error handling
- File download responses
- Pagination and filtering support

### 3. ExamplesController.cs
**Usage Examples and Documentation**

Provides practical examples and documentation:

- `GET /api/examples/create-simple-invoice` - Simple invoice creation
- `GET /api/examples/create-complex-invoice` - Multi-VAT invoice example
- `GET /api/examples/fluent-builder` - Fluent builder demonstration
- `GET /api/examples/authentication-flow` - OAuth2 flow explanation
- `GET /api/examples/complete-workflow` - End-to-end workflow

**Key Features:**
- Self-documenting API examples
- Code samples in responses
- Step-by-step workflows
- Best practices demonstration

### 4. InvoiceBuilder.cs
**Invoice Creation Utilities**

Utility classes for building UBL invoices:

- `InvoiceBuilder.CreateSampleRomanianInvoice()` - Standard Romanian invoice
- `InvoiceBuilder.CreateComplexSampleInvoice()` - Multi-VAT invoice
- `InvoiceBuilder.CreateMinimalInvoice()` - Minimal test invoice
- `InvoiceBuilder.Create().Build()` - Fluent builder pattern

**Features:**
- Pre-configured Romanian defaults
- Automatic VAT calculations
- Fluent builder interface
- Multiple complexity levels

## Setup Instructions

### 1. Add Controllers to Your Project

```csharp
// In Program.cs
builder.Services.AddControllers();
builder.Services.AddSession(); // For authentication state
builder.Services.AddEFacturaServices(builder.Configuration);

var app = builder.Build();

app.UseSession();
app.UseAuthentication(); // If using additional auth
app.UseAuthorization();
app.MapControllers();
```

### 2. Configure Authentication

```json
{
  "EFactura": {
    "Environment": "Test",
    "ClientId": "YOUR_CLIENT_ID_FROM_ANAF",
    "ClientSecret": "YOUR_CLIENT_SECRET_FROM_ANAF",
    "RedirectUri": "https://yourapp.com/api/auth/callback",
    "Cif": "YOUR_COMPANY_CIF",
    "TimeoutSeconds": 30
  }
}
```

### 3. Register Redirect URI with ANAF

Ensure your `RedirectUri` is registered with ANAF exactly as configured.

## Usage Examples

### Basic Authentication Flow

```javascript
// 1. Start authentication
window.location.href = '/api/auth/login';

// 2. User is redirected to ANAF, selects certificate, comes back

// 3. Check authentication status
fetch('/api/auth/status')
  .then(response => response.json())
  .then(data => {
    if (data.isAuthenticated) {
      console.log('Ready to use EFactura API');
    }
  });
```

### Invoice Operations

```javascript
// Create invoice using builder
const invoice = {
  // Use InvoiceBuilder patterns from examples
};

// Validate invoice
const validation = await fetch('/api/efactura/validate', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify(invoice)
}).then(r => r.json());

if (validation.isValid) {
  // Upload invoice
  const upload = await fetch('/api/efactura/upload', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(invoice)
  }).then(r => r.json());
  
  console.log('Upload ID:', upload.uploadId);
}
```

### Download Invoices

```javascript
// Get recent invoices
const invoices = await fetch('/api/efactura/invoices')
  .then(r => r.json());

// Download specific invoice as PDF
const pdfUrl = `/api/efactura/invoices/${messageId}/pdf`;
window.open(pdfUrl);
```

## Error Handling

All controllers include comprehensive error handling:

- **401 Unauthorized**: Authentication required
- **400 Bad Request**: Invalid input data
- **500 Internal Server Error**: Unexpected errors

Authentication errors include helpful information:

```json
{
  "error": "Authentication required",
  "loginUrl": "/api/auth/login"
}
```

## Security Considerations

### Session Security
- Use HTTPS in production
- Configure secure session cookies
- Implement session timeout

### Token Storage
- Example uses session storage (development)
- Consider secure token storage for production
- Implement token refresh logic

### CSRF Protection
- State parameter validation implemented
- Consider additional CSRF tokens for forms

## Development Tips

### Testing Authentication
Use the Examples controller to understand the flow:
```
GET /api/examples/authentication-flow
```

### Invoice Validation
Always validate before uploading:
```
POST /api/efactura/validate
```

### Monitoring Uploads
Use the wait endpoint for real-time status:
```
POST /api/efactura/upload/{uploadId}/wait
```

### Debugging
Enable detailed logging in appsettings.json:
```json
{
  "Logging": {
    "LogLevel": {
      "RomaniaEFacturaLibrary": "Debug"
    }
  }
}
```

## Integration Patterns

### SPA Integration
For Single Page Applications:
1. Handle authentication in backend
2. Store tokens securely
3. Provide API endpoints for frontend
4. Use examples controller for guidance

### Background Processing
For batch operations:
1. Store authentication tokens securely
2. Implement token refresh
3. Process invoices in background
4. Monitor upload status

### Microservices
For distributed systems:
1. Centralize authentication service
2. Share tokens securely
3. Implement circuit breakers
4. Monitor service health

## Support

For additional examples and support:
- Check the main README.md
- Review the test cases
- Use the Examples controller endpoints
- See the console application for offline examples