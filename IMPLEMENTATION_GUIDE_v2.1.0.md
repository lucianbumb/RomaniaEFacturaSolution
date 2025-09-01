# Romania EFactura Library v2.1.0 - Implementation Guide

## ?? Major Update: Enhanced Multi-Tenant Support & Persistent Token Storage

Version 2.1.0 introduces **production-ready token management** and **complete multi-tenant support** with CIF parameters for all operations.

## ? New Features

### ?? **Persistent Token Storage**
- **MemoryCache Storage** (default) - tokens persist across requests
- **Cookie Storage** - browser-based token storage
- **Custom Storage** - implement your own token storage
- **Automatic token refresh** - seamless JWT token management
- **Multi-user support** - concurrent user authentication

### ?? **Enhanced Multi-Tenant Support**
- **CIF parameters** required for upload, validation, and invoice operations
- **Dynamic company selection** - no hardcoded CIF in configuration
- **Batch processing** - handle multiple companies simultaneously
- **Per-tenant authentication** - isolated token management

### ?? **Improved Architecture**
- **Production-ready** token persistence
- **Web application friendly** - proper session handling
- **Scalable design** - supports multiple concurrent users
- **Flexible registration** - choose your storage strategy

---

## ?? **Migration Guide from v2.0.x**

### **Breaking Changes:**
1. **CIF Parameter Required**: All upload and validation methods now require CIF parameter
2. **Service Registration**: Enhanced with token storage options
3. **Authentication Service**: Now uses persistent token storage

### **Before (v2.0.x):**
```csharp
// Old approach - CIF in configuration
await client.UploadInvoiceAsync(invoice);
await client.ValidateInvoiceAsync(invoice);
await client.GetInvoicesAsync(from, to);
```

### **After (v2.1.0):**
```csharp
// New approach - CIF as parameter
await client.UploadInvoiceAsync(invoice, "12345678");
await client.ValidateInvoiceAsync(invoice, "12345678");
await client.GetInvoicesAsync("12345678", from, to);
```

---

## ??? **Setup & Configuration**

### **1. Basic Setup (MemoryCache - Recommended)**
```csharp
// Program.cs
builder.Services.AddEFacturaServices(builder.Configuration);

// appsettings.json
{
  "EFactura": {
    "Environment": "Test", // or "Production"
    "ClientId": "YOUR_CLIENT_ID_FROM_ANAF",
    "ClientSecret": "YOUR_CLIENT_SECRET_FROM_ANAF",
    "RedirectUri": "https://yourapp.com/api/auth/callback"
    // Note: CIF removed from config - now passed as parameter
  }
}
```

### **2. Cookie-Based Storage**
```csharp
// Program.cs
builder.Services.AddEFacturaServicesWithCookieStorage(builder.Configuration);
```

### **3. Custom Storage Implementation**
```csharp
// Implement your storage
public class DatabaseTokenStorageService : ITokenStorageService
{
    public async Task SetTokenAsync(string userName, TokenDto token, CancellationToken cancellationToken = default)
    {
        // Store in database
    }
    
    public async Task<TokenDto?> GetTokenAsync(string userName, CancellationToken cancellationToken = default)
    {
        // Retrieve from database
    }
    
    // ... implement other methods
}

// Register custom storage
builder.Services.AddEFacturaServicesWithCustomStorage<DatabaseTokenStorageService>(builder.Configuration);
```

---

## ?? **Usage Examples**

### **Authentication with Automatic Storage**
```csharp
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authService;

    [HttpGet("login")]
    public IActionResult Login()
    {
        var authUrl = _authService.GetAuthorizationUrl("efactura", Guid.NewGuid().ToString());
        return Redirect(authUrl);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string code, string state)
    {
        // Exchange code for token - automatically stored in persistent storage
        var tokenResponse = await _authService.ExchangeCodeForTokenAsync(code);
        
        return Ok(new { success = true, message = "Authentication successful" });
    }
}
```

### **Multi-Tenant Invoice Operations**
```csharp
[ApiController]
public class InvoiceController : ControllerBase
{
    private readonly IEFacturaClient _client;

    // Upload invoice for specific company
    [HttpPost("upload/{cif}")]
    public async Task<IActionResult> UploadInvoice(string cif, [FromBody] UblInvoice invoice)
    {
        // Validate for specific CIF
        var validation = await _client.ValidateInvoiceAsync(invoice, cif);
        if (!validation.Success)
        {
            return BadRequest(validation.Errors);
        }

        // Upload for specific CIF
        var result = await _client.UploadInvoiceAsync(invoice, cif);
        
        return Ok(new { uploadId = result.UploadId, cif });
    }

    // Get invoices for specific company
    [HttpGet("invoices/{cif}")]
    public async Task<IActionResult> GetInvoices(
        string cif, 
        DateTime? from = null, 
        DateTime? to = null)
    {
        var invoices = await _client.GetInvoicesAsync(cif, from, to);
        
        return Ok(new 
        { 
            cif, 
            count = invoices.Count, 
            invoices = invoices.Select(i => new { i.Id, i.IssueDate, TotalAmount = i.LegalMonetaryTotal?.TaxInclusiveAmount?.Value })
        });
    }
}
```

### **Multi-Company Batch Processing**
```csharp
public class MultiTenantService
{
    private readonly IEFacturaClient _client;

    public async Task ProcessInvoicesForMultipleCompanies(Dictionary<string, List<UblInvoice>> invoicesByCif)
    {
        foreach (var (cif, invoices) in invoicesByCif)
        {
            foreach (var invoice in invoices)
            {
                try
                {
                    // Each operation specifies the company CIF
                    await _client.ValidateInvoiceAsync(invoice, cif);
                    var result = await _client.UploadInvoiceAsync(invoice, cif);
                    
                    Console.WriteLine($"Uploaded invoice {invoice.Id} for CIF {cif}: {result.UploadId}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to process invoice {invoice.Id} for CIF {cif}: {ex.Message}");
                }
            }
        }
    }
}
```

---

## ?? **Advanced Configuration**

### **Token Storage Customization**
```csharp
// Configure MemoryCache options
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 1000;
    options.CompactionPercentage = 0.2;
});

// Configure session for web apps
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
```

### **Custom User Identification**
```csharp
// The library automatically detects user from HttpContext
// Or you can specify user manually
var token = await authService.GetValidAccessTokenAsync("specific-user");
await authService.RemoveTokenAsync("user-to-logout");
```

### **Error Handling**
```csharp
try
{
    var result = await client.UploadInvoiceAsync(invoice, cif);
}
catch (AuthenticationException)
{
    // Redirect to login
    return Redirect("/api/auth/login");
}
catch (ArgumentException ex) when (ex.ParamName == "cif")
{
    // Invalid CIF provided
    return BadRequest("Valid CIF is required");
}
catch (EFacturaApiException ex)
{
    // ANAF API error
    return StatusCode(500, $"ANAF API error: {ex.Message}");
}
```

---

## ?? **Token Storage Comparison**

| Storage Type | Persistence | Multi-User | Performance | Use Case |
|--------------|-------------|------------|-------------|----------|
| **MemoryCache** | Application lifetime | ? Yes | ? Fastest | Web apps, services |
| **Cookie** | Browser session | ? Yes | ?? Good | Web applications |
| **Custom** | Your choice | ? Yes | ?? Depends | Enterprise, database |

---

## ?? **Best Practices**

### **1. Production Setup**
```csharp
// Use HTTPS in production
builder.Services.Configure<EFacturaConfig>(config =>
{
    config.Environment = EFacturaEnvironment.Production;
    config.RedirectUri = "https://yourapp.com/api/auth/callback"; // HTTPS!
});

// Configure proper logging
builder.Services.AddLogging(logging =>
{
    logging.AddApplicationInsights();
    logging.SetMinimumLevel(LogLevel.Information);
});
```

### **2. Multi-Tenant Architecture**
```csharp
// Service to manage multiple companies
public class TenantService
{
    public async Task<List<InvoiceData>> GetAllTenantInvoices(List<string> tenantCifs)
    {
        var allInvoices = new List<InvoiceData>();
        
        foreach (var cif in tenantCifs)
        {
            var invoices = await _client.GetInvoicesAsync(cif, DateTime.Today.AddDays(-30), DateTime.Today);
            allInvoices.AddRange(invoices.Select(i => new InvoiceData { Cif = cif, Invoice = i }));
        }
        
        return allInvoices;
    }
}
```

### **3. Error Recovery**
```csharp
// Implement retry logic
public async Task<UploadResponse> UploadWithRetry(UblInvoice invoice, string cif, int maxRetries = 3)
{
    for (int attempt = 1; attempt <= maxRetries; attempt++)
    {
        try
        {
            return await _client.UploadInvoiceAsync(invoice, cif);
        }
        catch (AuthenticationException) when (attempt < maxRetries)
        {
            // Token might be expired, the library will auto-refresh
            await Task.Delay(1000 * attempt);
        }
    }
    
    throw new InvalidOperationException("Upload failed after retries");
}
```

---

## ?? **Performance Optimizations**

### **Batch Operations**
```csharp
// Process multiple invoices efficiently
public async Task<BatchResult> BatchUpload(string cif, List<UblInvoice> invoices)
{
    var tasks = invoices.Select(async invoice =>
    {
        try
        {
            var result = await _client.UploadInvoiceAsync(invoice, cif);
            return new { Success = true, InvoiceId = invoice.Id, UploadId = result.UploadId };
        }
        catch (Exception ex)
        {
            return new { Success = false, InvoiceId = invoice.Id, Error = ex.Message };
        }
    });

    var results = await Task.WhenAll(tasks);
    
    return new BatchResult
    {
        TotalProcessed = results.Length,
        Successful = results.Count(r => r.Success),
        Failed = results.Count(r => !r.Success)
    };
}
```

---

## ?? **Migration Checklist**

- [ ] Update service registration to use new methods
- [ ] Add CIF parameters to all upload/validation calls
- [ ] Remove CIF from configuration (optional)
- [ ] Test authentication flow with new token storage
- [ ] Update error handling for new exceptions
- [ ] Verify multi-tenant functionality
- [ ] Test token persistence across requests
- [ ] Update any existing example controllers

---

## ?? **You're Ready!**

Romania EFactura Library v2.1.0 now provides **enterprise-grade** token management and **complete multi-tenant support**. Your application can:

? **Handle multiple companies** with dynamic CIF parameters  
? **Persist authentication** across requests and restarts  
? **Scale horizontally** with proper token storage  
? **Support concurrent users** with isolated authentication  
? **Integrate seamlessly** with existing web applications  

**Happy coding! ??????**