# Romania EFactura Library - Development Instructions

## Project Overview
This is a C# library for connecting to the Romanian EFactura (SPV - Spatiu Privat Virtual) system. The library enables:
- OAuth2 authentication with digital certificates (browser-based flow)
- JWT token management with automatic refresh
- Uploading invoices in UBL 2.1 XML format
- Downloading invoices from SPV
- XML validation and transformation
- ClientId/ClientSecret based authentication

## Architecture
- **RomaniaEFacturaLibrary**: Core library with API clients, models, and services
- **RomaniaEFacturaConsole**: Console application for testing
- **RomaniaEFacturaLibrary.Tests**: Unit tests

## Key Components
- **AuthenticationService**: OAuth2 flow with Basic Auth and JWT tokens
- **EFacturaApiClient**: ANAF SPV API communication
- **EFacturaClient**: High-level client for invoice operations
- **UBL 2.1 Models**: Complete invoice XML models
- **XmlService**: XML serialization and validation
- **Configuration**: ClientId/ClientSecret/RedirectUri setup

## Authentication Flow
1. Generate authorization URL with `GetAuthorizationUrl()`
2. Redirect user to ANAF OAuth page (certificate prompt)
3. Handle callback with authorization code
4. Exchange code for JWT token with `ExchangeCodeForTokenAsync()`
5. Use token for API operations with automatic refresh

## Configuration Requirements
```json
{
  "EFactura": {
    "Environment": "Test|Production",
    "ClientId": "from-anaf-registration",
    "ClientSecret": "from-anaf-registration", 
    "RedirectUri": "your-oauth-callback-url",
    "Cif": "company-fiscal-code",
    "TimeoutSeconds": 30
  }
}
```

## Development Guidelines
- Use async/await patterns for all HTTP operations
- Use IHttpClientFactory for HTTP clients (not direct HttpClient injection)
- Implement proper error handling with specific exceptions
- Use dependency injection for all services
- Follow OAuth2 Authorization Code flow (not client_credentials)
- Support both test and production ANAF environments
- Handle authentication expiration gracefully
- Use JWT tokens with Basic Auth for token exchange

## Service Registration
```csharp
// Correct way
services.AddEFacturaServices(configuration);

// All services use IHttpClientFactory internally
// No direct HttpClient injection
```

## Common Issues to Avoid
- Don't inject HttpClient directly - use IHttpClientFactory
- Don't use client_credentials flow - use authorization code flow
- Don't forget Basic Auth header for token exchange
- Don't forget token_content_type=jwt parameter
- Ensure RedirectUri matches ANAF registration exactly
