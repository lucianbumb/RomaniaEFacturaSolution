# Romania EFactura Library - Troubleshooting Guide

## Common Dependency Injection Issues

### ? Error: "Unable to resolve service for type 'Microsoft.Extensions.Caching.Distributed.IDistributedCache'"

**Problem**: You're using sessions but `IDistributedCache` is not registered.

**Full Error**:
```
System.AggregateException: Some services are not able to be constructed (Error while validating the service descriptor 'ServiceType: Microsoft.AspNetCore.Session.ISessionStore Lifetime: Transient ImplementationType: Microsoft.AspNetCore.Session.DistributedSessionStore': Unable to resolve service for type 'Microsoft.Extensions.Caching.Distributed.IDistributedCache' while attempting to activate 'Microsoft.AspNetCore.Session.DistributedSessionStore'.)
```

**? Solution 1: Use the new extension method (Recommended)**
```csharp
// In Program.cs
builder.Services.AddEFacturaServicesWithSessions(builder.Configuration);
```

**? Solution 2: Add distributed cache manually**
```csharp
// In Program.cs
builder.Services.AddDistributedMemoryCache(); // Add this BEFORE AddSession()
builder.Services.AddSession();
builder.Services.AddEFacturaServices(builder.Configuration);
```

**? Solution 3: Just use EFactura services without sessions**
```csharp
// In Program.cs - if you don't need sessions for OAuth2 state
builder.Services.AddEFacturaServices(builder.Configuration);
// Don't call AddSession()
```

---

## Service Registration Options

### **Option 1: With Sessions (OAuth2 Support)**
```csharp
// Recommended for web applications with OAuth2
builder.Services.AddEFacturaServicesWithSessions(builder.Configuration);

// This automatically includes:
// - EFactura services
// - MemoryCache token storage
// - Distributed cache
// - Session configuration
// - HttpContextAccessor
```

### **Option 2: Basic Services Only**
```csharp
// For applications without OAuth2 or session requirements
builder.Services.AddEFacturaServices(builder.Configuration);

// This includes:
// - EFactura services
// - MemoryCache token storage
// - Distributed cache
// - HttpContextAccessor
```

### **Option 3: Cookie Token Storage**
```csharp
// For applications preferring cookie-based token storage
builder.Services.AddEFacturaServicesWithCookieStorage(builder.Configuration);
```

### **Option 4: Custom Token Storage**
```csharp
// For applications with custom token storage (e.g., database)
builder.Services.AddEFacturaServicesWithCustomStorage<YourCustomTokenStorage>(builder.Configuration);
```

---

## Configuration Issues

### ? Error: "Configuration section 'EFactura' not found"

**? Solution**: Ensure `appsettings.json` has the correct structure:
```json
{
  "EFactura": {
    "Environment": "Test",
    "ClientId": "YOUR_CLIENT_ID",
    "ClientSecret": "YOUR_CLIENT_SECRET",
    "RedirectUri": "https://localhost:7000/api/efactura/callback",
    "TimeoutSeconds": 30
  }
}
```

### ? Error: "Invalid configuration values"

**? Solution**: Verify all required fields:
- `Environment`: Must be "Test" or "Production"
- `ClientId`: From ANAF application registration
- `ClientSecret`: From ANAF application registration
- `RedirectUri`: Must match ANAF registration exactly

---

## Authentication Issues

### ? Error: "No valid access token available"

**Cause**: User not authenticated or token expired.

**? Solution**: 
1. Implement OAuth2 flow:
   ```csharp
   // Get authorization URL
   var authUrl = _authService.GetAuthorizationUrl("efactura", "state");
   
   // Redirect user to authUrl
   // Handle callback and exchange code for token
   var token = await _authService.ExchangeCodeForTokenAsync(code);
   ```

2. Or set token manually for testing:
   ```csharp
   _authService.SetToken(tokenResponse);
   ```

### ? Error: "Authentication failed with digital certificate"

**Cause**: Browser doesn't have valid digital certificate or certificate not selected.

**? Solution**:
1. Install valid digital certificate in browser
2. Ensure certificate is for the correct environment (test/production)
3. Check certificate expiration date
4. Try different browser

---

## Token Storage Issues

### ? Error: "Token storage not working across requests"

**Cause**: Using in-memory token storage without proper user identification.

**? Solution**: Use persistent token storage:
```csharp
// Option 1: Sessions (requires user authentication)
builder.Services.AddEFacturaServicesWithSessions(builder.Configuration);

// Option 2: Cookie storage
builder.Services.AddEFacturaServicesWithCookieStorage(builder.Configuration);

// Option 3: Custom storage (database)
builder.Services.AddEFacturaServicesWithCustomStorage<DatabaseTokenStorage>(builder.Configuration);
```

### ? Error: "Cannot determine current user from HttpContext"

**Cause**: User not authenticated or HttpContext not available.

**? Solution**: 
1. Ensure user is authenticated
2. Pass userName explicitly:
   ```csharp
   var token = await _authService.GetValidAccessTokenAsync("specific-user");
   ```

---

## CIF Parameter Issues

### ? Error: "CIF parameter is required"

**Cause**: Using old API without CIF parameter.

**? Solution**: Update to new API with CIF parameters:
```csharp
// Old (will not work in v2.1.0+)
await client.UploadInvoiceAsync(invoice);
await client.GetInvoicesAsync(from, to);

// New (correct)
await client.UploadInvoiceAsync(invoice, "123456789");
await client.GetInvoicesAsync("123456789", from, to);
```

---

## Environment-Specific Issues

### **Test Environment**
- Uses ANAF test servers
- May have different certificate requirements
- Some features might be limited

### **Production Environment**
- Requires production certificates
- Stricter validation
- Full ANAF API access

**Configuration**:
```json
{
  "EFactura": {
    "Environment": "Production", // or "Test"
    // ... other settings
  }
}
```

---

## Package Dependencies

### **Minimum Required Packages** (automatically included):
- `Microsoft.Extensions.DependencyInjection`
- `Microsoft.Extensions.Http`
- `Microsoft.Extensions.Caching.Memory`
- `Microsoft.AspNetCore.Http`
- `Microsoft.AspNetCore.Session` (for session support)

### **If you get package reference errors**:
```xml
<PackageReference Include="Microsoft.Extensions.Caching.Distributed" Version="9.0.0" />
<PackageReference Include="Microsoft.AspNetCore.Session" Version="9.0.0" />
```

---

## Migration from v2.0.x to v2.1.0+

### **Breaking Changes**:
1. **CIF Parameter Required**:
   ```csharp
   // Before
   await client.UploadInvoiceAsync(invoice);
   
   // After
   await client.UploadInvoiceAsync(invoice, "123456789");
   ```

2. **Service Registration Updated**:
   ```csharp
   // Before - might cause session issues
   services.AddSession();
   services.AddEFacturaServices(configuration);
   
   // After - includes all dependencies
   services.AddEFacturaServicesWithSessions(configuration);
   ```

---

## Testing & Debugging

### **Check Service Registration**:
```csharp
// In Program.cs - verify services are registered
var serviceProvider = builder.Services.BuildServiceProvider();
var client = serviceProvider.GetService<IEFacturaClient>();
if (client == null) throw new Exception("EFactura services not registered");
```

### **Test Authentication**:
```csharp
try
{
    var token = await _authService.GetValidAccessTokenAsync();
    // Token retrieved successfully
}
catch (AuthenticationException)
{
    // Need to authenticate
    var authUrl = _authService.GetAuthorizationUrl("efactura", "test-state");
    // Redirect to authUrl
}
```

### **Test Token Storage**:
```csharp
// Set a test token
var testToken = new TokenDto 
{ 
    AccessToken = "test", 
    ExpiresIn = 3600, 
    CreatedAt = DateTime.UtcNow 
};
await _tokenStorage.SetTokenAsync("test-user", testToken);

// Retrieve token
var retrieved = await _tokenStorage.GetTokenAsync("test-user");
// Should not be null
```

---

## Contact & Support

- **GitHub Issues**: https://github.com/lucianbumb/RomaniaEFacturaSolution/issues
- **Documentation**: See `IMPLEMENTATION_GUIDE_v2.1.0.md`
- **Examples**: Check `RomaniaEFacturaWebApi` project

---

**Most common solution**: Use `AddEFacturaServicesWithSessions()` instead of separate `AddSession()` and `AddEFacturaServices()` calls.