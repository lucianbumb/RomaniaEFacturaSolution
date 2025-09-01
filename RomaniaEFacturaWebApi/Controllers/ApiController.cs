using Microsoft.AspNetCore.Mvc;

namespace RomaniaEFacturaWebApi.Controllers;

/// <summary>
/// API Documentation and help controller
/// </summary>
[ApiController]
[Route("api")]
public class ApiController : ControllerBase
{
    /// <summary>
    /// Get API documentation and available endpoints
    /// </summary>
    [HttpGet("")]
    [HttpGet("help")]
    public IActionResult GetApiDocumentation()
    {
        return Ok(new
        {
            title = "Romania EFactura Web API",
            version = "2.1.0",
            description = "API for interacting with Romanian ANAF EFactura system",
            defaultCif = "123456789",
            baseUrl = $"{Request.Scheme}://{Request.Host}",
            
            endpoints = new
            {
                authentication = new
                {
                    login = new
                    {
                        method = "GET",
                        url = "/api/efactura/login",
                        description = "Initiate OAuth2 login with ANAF",
                        response = "Returns authorization URL for browser redirect"
                    },
                    callback = new
                    {
                        method = "GET",
                        url = "/api/efactura/callback",
                        description = "OAuth2 callback endpoint (used by ANAF)",
                        parameters = "code, state (handled automatically)"
                    },
                    status = new
                    {
                        method = "GET",
                        url = "/api/efactura/status",
                        description = "Check current authentication status"
                    },
                    logout = new
                    {
                        method = "POST",
                        url = "/api/efactura/logout",
                        description = "Logout and clear stored tokens"
                    }
                },
                
                invoices = new
                {
                    download = new
                    {
                        method = "GET",
                        url = "/api/efactura/download",
                        description = "Download invoices for CIF 123456789",
                        parameters = "from, to (optional date range), cif (optional)"
                    },
                    upload = new
                    {
                        method = "POST",
                        url = "/api/invoice/upload",
                        description = "Upload an invoice to ANAF SPV",
                        body = "UBL Invoice JSON",
                        parameters = "cif (optional, defaults to 123456789)"
                    },
                    validate = new
                    {
                        method = "POST",
                        url = "/api/invoice/validate",
                        description = "Validate an invoice without uploading",
                        body = "UBL Invoice JSON"
                    },
                    status = new
                    {
                        method = "GET",
                        url = "/api/invoice/status/{uploadId}",
                        description = "Check upload status by upload ID"
                    },
                    wait = new
                    {
                        method = "POST",
                        url = "/api/invoice/wait/{uploadId}",
                        description = "Wait for upload to complete (up to 30 minutes)",
                        parameters = "timeoutMinutes (optional, default 10)"
                    },
                    sample = new
                    {
                        method = "GET",
                        url = "/api/invoice/sample",
                        description = "Get a sample invoice for testing"
                    }
                }
            },
            
            quickStart = new
            {
                step1 = "GET /api/efactura/login - Get authorization URL",
                step2 = "Navigate to authUrl in browser with digital certificate",
                step3 = "After authentication, you'll be redirected to callback",
                step4 = "GET /api/efactura/download - Download invoices",
                step5 = "GET /api/invoice/sample - Get sample invoice data",
                step6 = "POST /api/invoice/upload - Upload invoice"
            },
            
            configuration = new
            {
                defaultCif = "123456789",
                environment = "Test",
                note = "Update appsettings.json with your ANAF ClientId and ClientSecret"
            },
            
            documentation = new
            {
                swagger = "/swagger",
                github = "https://github.com/lucianbumb/RomaniaEFacturaSolution"
            }
        });
    }
}