# Romania EFactura Library - Development Instructions

## Project Overview
This is a C# library for connecting to the Romanian EFactura (SPV - Spatiu Privat Virtual) system. The library enables:
- OAuth2 authentication with digital certificates
- Uploading invoices in UBL 2.1 XML format
- Downloading invoices from SPV
- XML validation and transformation
- Digital signature support

## Architecture
- **RomaniaEFacturaLibrary**: Core library with API clients, models, and services
- **RomaniaEFacturaConsole**: Console application for testing
- **RomaniaEFacturaLibrary.Tests**: Unit tests

## Key Components
- Authentication service with OAuth2 and certificate support
- UBL 2.1 XML invoice models
- API client for ANAF SPV endpoints
- Digital signature verification
- Invoice validation services

## Development Guidelines
- Use async/await patterns for all HTTP operations
- Implement proper error handling and logging
- Follow SOLID principles
- Include comprehensive unit tests
- Use dependency injection for services
- Support both test and production ANAF environments
