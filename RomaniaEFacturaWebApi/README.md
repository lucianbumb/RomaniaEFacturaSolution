# Romania EFactura Web API

A complete ASP.NET Core Web API for interacting with the Romanian ANAF EFactura system using the Romania EFactura Library v2.1.0.

## ?? Quick Start

### 1. **Configuration**
Update `appsettings.json` with your ANAF credentials:

```json
{
  "EFactura": {
    "Environment": "Test",
    "ClientId": "YOUR_CLIENT_ID_FROM_ANAF",
    "ClientSecret": "YOUR_CLIENT_SECRET_FROM_ANAF",
    "RedirectUri": "https://localhost:7000/api/efactura/callback",
    "TimeoutSeconds": 30
  }
}
```

### 2. **Run the API**
```bash
dotnet run --project RomaniaEFacturaWebApi
```

The API will be available at:
- **HTTPS**: https://localhost:7000
- **HTTP**: http://localhost:5000
- **Swagger**: https://localhost:7000/swagger

### 3. **API Overview**
```bash
GET https://localhost:7000/api
```

## ?? **Available Endpoints**

### **?? Authentication**

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/efactura/login` | Initiate OAuth2 authentication |
| `GET` | `/api/efactura/callback` | OAuth2 callback (used by ANAF) |
| `GET` | `/api/efactura/status` | Check authentication status |
| `POST` | `/api/efactura/logout` | Logout and clear tokens |

### **?? Invoice Operations**

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/efactura/download` | Download invoices for CIF 123456789 |
| `POST` | `/api/invoice/upload` | Upload an invoice to ANAF SPV |
| `POST` | `/api/invoice/validate` | Validate invoice without uploading |
| `GET` | `/api/invoice/status/{uploadId}` | Check upload status |
| `POST` | `/api/invoice/wait/{uploadId}` | Wait for upload completion |
| `GET` | `/api/invoice/sample` | Get sample invoice data |

### **?? Documentation**

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api` | API documentation and help |
| `GET` | `/swagger` | Interactive API documentation |

## ?? **Usage Examples**

### **1. Authentication Flow**

```bash
# Step 1: Get authorization URL
curl -X GET "https://localhost:7000/api/efactura/login"

# Response includes authUrl - open in browser with digital certificate
# User completes authentication, gets redirected to callback

# Step 2: Check authentication status
curl -X GET "https://localhost:7000/api/efactura/status"
```

### **2. Download Invoices**

```bash
# Download invoices for default CIF (123456789)
curl -X GET "https://localhost:7000/api/efactura/download"

# Download with date range
curl -X GET "https://localhost:7000/api/efactura/download?from=2025-01-01&to=2025-01-31"

# Download for specific CIF
curl -X GET "https://localhost:7000/api/efactura/download?cif=987654321"
```

### **3. Upload Invoice**

```bash
# Get sample invoice data
curl -X GET "https://localhost:7000/api/invoice/sample"

# Upload invoice (using sample data)
curl -X POST "https://localhost:7000/api/invoice/upload" \
  -H "Content-Type: application/json" \
  -d @sample_invoice.json

# Check upload status
curl -X GET "https://localhost:7000/api/invoice/status/{uploadId}"
```

### **4. Validate Invoice**

```bash
# Validate without uploading
curl -X POST "https://localhost:7000/api/invoice/validate" \
  -H "Content-Type: application/json" \
  -d @invoice_data.json
```

## ?? **Multi-Tenant Support**

The API supports multiple companies by accepting CIF parameters:

```bash
# Upload for specific company
curl -X POST "https://localhost:7000/api/invoice/upload?cif=123456789"

# Download invoices for specific company
curl -X GET "https://localhost:7000/api/efactura/download?cif=987654321"

# Validate for specific company
curl -X POST "https://localhost:7000/api/invoice/validate?cif=555666777"
```

## ?? **Configuration Options**

### **Default CIF**
The API uses `123456789` as the default CIF. You can:
- Override it per request using `?cif=YOUR_CIF` parameter
- Change the default in the controller code

### **Token Storage**
The API uses MemoryCache token storage by default. Tokens persist across requests but are lost on application restart.

### **Environment**
- **Test**: Uses ANAF test environment
- **Production**: Change `Environment` to `"Production"` in appsettings.json

## ??? **Security Features**

- ? **OAuth2 with Digital Certificates** - Secure authentication with ANAF
- ? **CSRF Protection** - State parameter validation
- ? **Persistent Token Storage** - Automatic token refresh
- ? **HTTPS Enforcement** - Secure communication
- ? **Session Management** - Proper OAuth2 state handling

## ?? **Response Examples**

### **Authentication Status**
```json
{
  "isAuthenticated": true,
  "hasValidToken": true,
  "message": "User is authenticated with valid token",
  "defaultCif": "123456789",
  "availableEndpoints": {
    "login": "/api/EFactura/Login",
    "download": "/api/EFactura/Download",
    "upload": "/api/invoice/upload"
  }
}
```

### **Invoice Download**
```json
{
  "success": true,
  "cif": "123456789",
  "dateRange": {
    "from": "2025-01-01T00:00:00",
    "to": "2025-01-31T23:59:59"
  },
  "count": 5,
  "invoices": [
    {
      "id": "INV-2025-001",
      "issueDate": "2025-01-15T00:00:00",
      "supplier": "My Company SRL",
      "customer": "Client Company SRL",
      "totalAmount": 238.00,
      "currency": "RON",
      "lines": 2
    }
  ]
}
```

### **Upload Success**
```json
{
  "success": true,
  "uploadId": "upload_123456789_20250115",
  "invoiceId": "DEMO-20250115-1234",
  "cif": "123456789",
  "message": "Invoice uploaded successfully",
  "uploadedAt": "2025-01-15T10:30:00Z",
  "nextSteps": [
    "Check upload status: GET /api/invoice/status/upload_123456789_20250115",
    "Wait for processing: POST /api/invoice/wait/upload_123456789_20250115"
  ]
}
```

## ?? **Error Handling**

The API provides comprehensive error responses:

```json
{
  "error": "Authentication required",
  "message": "Please authenticate first using /api/efactura/login",
  "loginUrl": "/api/EFactura/Login"
}
```

```json
{
  "error": "Invoice validation failed",
  "invoiceId": "DEMO-001",
  "cif": "123456789",
  "errors": [
    "Supplier CIF is required",
    "Invoice total amount mismatch"
  ],
  "suggestion": "Please fix the validation errors and try again"
}
```

## ?? **Development Workflow**

1. **Setup**: Configure appsettings.json with ANAF credentials
2. **Authentication**: Use `/api/efactura/login` to authenticate
3. **Testing**: Use `/api/invoice/sample` to get test data
4. **Validation**: Validate invoices before uploading
5. **Upload**: Upload validated invoices
6. **Monitor**: Track upload status and completion

## ?? **Documentation**

- **Interactive API Docs**: https://localhost:7000/swagger
- **API Help**: https://localhost:7000/api
- **Library Docs**: See IMPLEMENTATION_GUIDE_v2.1.0.md
- **GitHub**: https://github.com/lucianbumb/RomaniaEFacturaSolution

---

**???? Ready to integrate with ANAF EFactura system! ???**