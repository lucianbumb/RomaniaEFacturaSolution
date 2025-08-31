# Romania EFactura Library v2.0.0 Release Notes

## 🎉 Major Release - September 1, 2025

### 🆕 New Features

#### Flexible Token Storage System
- **MemoryCache Token Storage**: In-memory token caching for development and testing
- **Cookie Token Storage**: Browser-based token storage for web applications
- **Extensible Interface**: Easy to implement custom token storage providers

#### CIF Parameter Support
- **Method-level CIF**: CIF now passed as parameter to methods instead of configuration
- **Multiple CIF Support**: Same client instance can work with multiple companies
- **Better Flexibility**: No need to reconfigure client for different companies

#### Enhanced Architecture
- **Internal API Client**: `EFacturaApiClient` is now internal for better encapsulation
- **Public Interface**: `IEFacturaClient` remains public for dependency injection
- **Improved Separation**: Clear separation between public API and internal implementation

### 📚 Comprehensive Examples

#### Authentication Controller
```csharp
// OAuth2 flow management
[ApiController]
[Route("api/[controller]")]
public class AuthenticationController : ControllerBase
{
    // Complete OAuth2 implementation examples
}
```

#### Invoice Controller
```csharp
// Invoice validation and upload
[ApiController]
[Route("api/[controller]")]
public class InvoiceController : ControllerBase
{
    // Full invoice lifecycle examples
}
```

#### Invoice Management Controller
```csharp
// Download, search, and message retrieval
[ApiController]
[Route("api/[controller]")]
public class InvoiceManagementController : ControllerBase
{
    // Complete invoice management examples
}
```

### 🧪 Testing Framework

#### Unit Tests
- **Token Storage Tests**: Comprehensive testing for both MemoryCache and Cookie storage
- **EFactura Client Tests**: Full coverage of client functionality
- **Mock Implementations**: Proper mocking for external dependencies
- **xUnit Framework**: Modern testing framework with better .NET integration

#### Test Coverage
- ✅ Authentication flows
- ✅ Token storage and retrieval
- ✅ Invoice validation and upload
- ✅ Message and invoice downloads
- ✅ Error handling scenarios

### 📖 Documentation Updates

#### Updated README
- **Installation Guide**: Clear NuGet package installation instructions
- **Quick Start**: Step-by-step setup guide
- **Feature Overview**: Comprehensive feature documentation
- **Examples**: Links to example controllers

#### API Documentation
- **Method Documentation**: All public methods fully documented
- **Parameter Descriptions**: Clear parameter usage guidelines
- **Return Types**: Detailed return type documentation

### 🔄 Breaking Changes

#### CIF Parameter Migration
```csharp
// OLD (v1.x)
await client.ValidateInvoiceAsync(xmlContent);

// NEW (v2.0)
await client.ValidateInvoiceAsync(cif, xmlContent);
```

#### Service Registration Changes
```csharp
// Updated registration methods
services.AddEFacturaLibrary(config);
services.AddEFacturaMemoryCacheTokenStorage();
// or
services.AddEFacturaCookieTokenStorage();
```

#### Internal API Changes
- `EFacturaApiClient` is now internal
- Use `IEFacturaClient` for dependency injection
- Updated constructor signatures

### 🏗️ Infrastructure Improvements

#### NuGet Package
- **Version**: 2.0.0
- **Enhanced Metadata**: Better package description and tags
- **Release Notes**: Comprehensive release documentation
- **Dependencies**: Updated to latest stable versions

#### GitHub Integration
- **Tagged Release**: v2.0.0 tag created
- **Commit History**: Clean commit history with detailed messages
- **Branch Protection**: Master branch with proper release workflow

### 📦 Package Information

- **Package Name**: RomaniaEFacturaLibrary
- **Version**: 2.0.0
- **Target Framework**: .NET 9.0
- **Package Size**: 46 KB
- **Location**: Available on local NuGet feed and ready for NuGet.org

### 🚀 Migration Guide

#### From v1.x to v2.0

1. **Update NuGet Package**
   ```bash
   dotnet add package RomaniaEFacturaLibrary --version 2.0.0
   ```

2. **Update Service Registration**
   ```csharp
   // Add token storage service
   services.AddEFacturaMemoryCacheTokenStorage();
   // or
   services.AddEFacturaCookieTokenStorage();
   ```

3. **Update Method Calls**
   ```csharp
   // Add CIF parameter to all invoice methods
   await client.ValidateInvoiceAsync(cif, xmlContent);
   await client.UploadInvoiceAsync(cif, xmlContent);
   await client.GetInvoicesAsync(cif, days);
   ```

4. **Update Dependency Injection**
   ```csharp
   // Use interface instead of concrete class
   public class MyService
   {
       private readonly IEFacturaClient _client;
       
       public MyService(IEFacturaClient client)
       {
           _client = client;
       }
   }
   ```

### 🎯 Next Steps

- Deploy to NuGet.org (manual step)
- Create GitHub release page with binaries
- Update project documentation
- Consider additional token storage providers (Redis, SQL Server)

### 🙏 Acknowledgments

This release represents a significant enhancement to the Romania EFactura Library, making it more flexible, testable, and production-ready for enterprise applications.
