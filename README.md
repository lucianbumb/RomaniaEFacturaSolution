# Romania EFactura Library

A comprehensive C# library for integrating with the Romanian EFactura (SPV - Spatiu Privat Virtual) system from ANAF.

## Overview

This library provides a complete solution for:
- **Authentication** with ANAF using OAuth2 and digital certificates
- **UBL 2.1 XML** invoice creation and validation
- **API Integration** with ANAF test and production environments
- **Invoice Management** (upload, download, status tracking)
- **XML Processing** with proper namespace handling and validation

## Projects Structure

- **`RomaniaEFacturaLibrary`** - Main library with all EFactura functionality

## Features

### 🔐 Authentication
- OAuth2 authentication with digital certificates via browser
- JWT token support with automatic refresh
- Support for test and production environments
- ClientId/ClientSecret configuration from ANAF registration

### 📄 UBL 2.1 XML Support
- Complete UBL 2.1 invoice models
- Proper XML serialization/deserialization
- Romanian EFactura-specific customizations
- XML validation and formatting

### 🌐 ANAF API Integration
- Upload invoices to ANAF SPV
- Check upload status and validation results
- Download invoices and attachments
- List recent invoices with filtering

### 🏗️ ASP.NET Core Ready
- Dependency injection support
- Configuration-based setup
- Logging integration
- Easy integration with web applications


## Contributing

This library follows Romanian EFactura specifications and OAuth2 standards.

## License

This project is provided as-is for educational and development purposes. Please ensure compliance with ANAF regulations and Romanian law when using in production.
