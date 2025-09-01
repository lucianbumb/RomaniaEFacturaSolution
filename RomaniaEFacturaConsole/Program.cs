using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RomaniaEFacturaLibrary.Extensions;
using RomaniaEFacturaLibrary.Models.Ubl;
using RomaniaEFacturaLibrary.Services;
using RomaniaEFacturaLibrary.Services.Authentication;

namespace RomaniaEFacturaConsole;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Romania EFactura Library - Console Test Application");
        Console.WriteLine("===================================================");

        try
        {
            // Build configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            // Build host with services
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    services.AddEFacturaServices(context.Configuration);
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddDebug();
                })
                .Build();

            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Application started");

            // Get the EFactura client
            var eFacturaClient = host.Services.GetRequiredService<IEFacturaClient>();
            var authService = host.Services.GetRequiredService<IAuthenticationService>();

            Console.WriteLine("\n⚠️  AUTHENTICATION REQUIRED");
            Console.WriteLine("This console app demonstrates the library functionality.");
            Console.WriteLine("For OAuth2 authentication, please use the web controllers.");
            Console.WriteLine("Some operations will fail without proper authentication.\n");

            // Show menu
            await ShowMenuAsync(eFacturaClient, authService, logger);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting application: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    private static async Task ShowMenuAsync(IEFacturaClient client, IAuthenticationService authService, ILogger logger)
    {
        while (true)
        {
            Console.WriteLine("\n=== EFactura Operations ===");
            Console.WriteLine("1. Create and validate sample invoice (requires auth)");
            Console.WriteLine("2. Upload sample invoice (requires auth)");
            Console.WriteLine("3. Check upload status (requires auth)");
            Console.WriteLine("4. List recent invoices (requires auth)");
            Console.WriteLine("5. Download invoice (requires auth)");
            Console.WriteLine("6. Convert invoice to PDF (requires auth)");
            Console.WriteLine("7. Show OAuth URLs");
            Console.WriteLine("8. Create sample invoice (offline)");
            Console.WriteLine("0. Exit");
            Console.Write("\nSelect option: ");

            var input = Console.ReadLine();

            try
            {
                switch (input)
                {
                    case "1":
                        await CreateAndValidateSampleInvoiceAsync(client, logger);
                        break;
                    case "2":
                        await UploadSampleInvoiceAsync(client, logger);
                        break;
                    case "3":
                        await CheckUploadStatusAsync(client, logger);
                        break;
                    case "4":
                        await ListRecentInvoicesAsync(client, logger);
                        break;
                    case "5":
                        await DownloadInvoiceAsync(client, logger);
                        break;
                    case "6":
                        await ConvertToPdfAsync(client, logger);
                        break;
                    case "7":
                        ShowOAuthUrls(authService);
                        break;
                    case "8":
                        CreateSampleInvoiceOffline();
                        break;
                    case "0":
                        Console.WriteLine("Goodbye!");
                        return;
                    default:
                        Console.WriteLine("Invalid option. Please try again.");
                        break;
                }
            }
            catch (AuthenticationException ex)
            {
                Console.WriteLine($"❌ Authentication Error: {ex.Message}");
                Console.WriteLine("💡 Use option 7 to see OAuth authentication URLs");
                logger.LogWarning("Authentication required for operation");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error: {ex.Message}");
                logger.LogError(ex, "Operation failed");
            }
        }
    }

    private static async Task CreateAndValidateSampleInvoiceAsync(IEFacturaClient client, ILogger logger)
    {
        Console.WriteLine("\n=== Creating Sample Invoice ===");

        var invoice = CreateSampleInvoice();
        
        Console.WriteLine($"Created invoice: {invoice.Id}");
        Console.WriteLine($"Issue date: {invoice.IssueDate:yyyy-MM-dd}");
        Console.WriteLine($"Supplier: {invoice.AccountingSupplierParty?.PartyLegalEntity?.RegistrationName}");
        Console.WriteLine($"Customer: {invoice.AccountingCustomerParty?.PartyLegalEntity?.RegistrationName}");

        Console.WriteLine("\nValidating invoice...");
        var validation = await client.ValidateInvoiceAsync(invoice);

        if (validation.Success)
        {
            Console.WriteLine("✅ Invoice is valid!");
        }
        else
        {
            Console.WriteLine("❌ Invoice validation failed:");
            foreach (var error in validation.Errors ?? new List<RomaniaEFacturaLibrary.Models.Api.ValidationError>())
            {
                Console.WriteLine($"  - {error.Message}");
            }
        }
    }

    private static async Task UploadSampleInvoiceAsync(IEFacturaClient client, ILogger logger)
    {
        Console.WriteLine("\n=== Uploading Sample Invoice ===");

        var invoice = CreateSampleInvoice();
        
        Console.WriteLine("Uploading invoice to SPV...");
        var uploadResponse = await client.UploadInvoiceAsync(invoice);

        Console.WriteLine($"✅ Invoice uploaded successfully!");
        Console.WriteLine($"Upload ID: {uploadResponse.UploadId}");
        
        // Wait for processing
        Console.WriteLine("Waiting for processing...");
        var finalStatus = await client.WaitForUploadCompletionAsync(uploadResponse.UploadId);
        
        Console.WriteLine($"Final status: {finalStatus.Status}");
        if (!string.IsNullOrEmpty(finalStatus.Message))
        {
            Console.WriteLine($"Details: {finalStatus.Message}");
        }
    }

    private static async Task CheckUploadStatusAsync(IEFacturaClient client, ILogger logger)
    {
        Console.WriteLine("\n=== Check Upload Status ===");
        Console.Write("Enter upload ID: ");
        var uploadId = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(uploadId))
        {
            Console.WriteLine("Invalid upload ID");
            return;
        }

        var status = await client.GetUploadStatusAsync(uploadId);
        
        Console.WriteLine($"Status: {status.Status}");
        Console.WriteLine($"Message: {status.Message}");
    }

    private static async Task ListRecentInvoicesAsync(IEFacturaClient client, ILogger logger)
    {
        Console.WriteLine("\n=== Recent Invoices ===");
        
        Console.Write("Enter CIF (company fiscal code): ");
        var cif = Console.ReadLine();
        
        if (string.IsNullOrWhiteSpace(cif))
        {
            Console.WriteLine("❌ CIF is required to list invoices");
            return;
        }
        
        var from = DateTime.Now.AddDays(-30);
        var to = DateTime.Now;
        
        Console.WriteLine($"Fetching invoices for CIF {cif} from {from:yyyy-MM-dd} to {to:yyyy-MM-dd}...");
        
        var invoices = await client.GetInvoicesAsync(cif, from, to);
        
        if (invoices.Count == 0)
        {
            Console.WriteLine("No invoices found for the specified period.");
            return;
        }

        Console.WriteLine($"Found {invoices.Count} invoices:");
        foreach (var invoice in invoices.Take(10)) // Show first 10
        {
            Console.WriteLine($"  {invoice.Id} - {invoice.IssueDate:yyyy-MM-dd} - {invoice.DocumentCurrencyCode}");
        }
        
        if (invoices.Count > 10)
        {
            Console.WriteLine($"  ... and {invoices.Count - 10} more");
        }
    }

    private static async Task DownloadInvoiceAsync(IEFacturaClient client, ILogger logger)
    {
        Console.WriteLine("\n=== Download Invoice ===");
        Console.Write("Enter message/invoice ID: ");
        var messageId = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(messageId))
        {
            Console.WriteLine("Invalid message ID");
            return;
        }

        Console.WriteLine("Downloading invoice...");
        var invoice = await client.DownloadInvoiceAsync(messageId);
        
        Console.WriteLine($"✅ Downloaded invoice: {invoice.Id}");
        Console.WriteLine($"Issue date: {invoice.IssueDate:yyyy-MM-dd}");
        Console.WriteLine($"Supplier: {invoice.AccountingSupplierParty?.PartyLegalEntity?.RegistrationName}");
        Console.WriteLine($"Customer: {invoice.AccountingCustomerParty?.PartyLegalEntity?.RegistrationName}");
        Console.WriteLine($"Total amount: {invoice.LegalMonetaryTotal?.PayableAmount?.Value} {invoice.LegalMonetaryTotal?.PayableAmount?.CurrencyId}");
        
        Console.WriteLine($"Invoice data retrieved successfully.");
    }

    private static async Task ConvertToPdfAsync(IEFacturaClient client, ILogger logger)
    {
        Console.WriteLine("\n=== Convert Invoice to PDF ===");
        
        var invoice = CreateSampleInvoice();
        
        Console.WriteLine("Converting invoice to PDF...");
        var pdfData = await client.ConvertToPdfAsync(invoice);
        
        var fileName = $"invoice_{invoice.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
        await File.WriteAllBytesAsync(fileName, pdfData);
        
        Console.WriteLine($"✅ PDF created: {fileName} ({pdfData.Length} bytes)");
    }

    private static void ShowOAuthUrls(IAuthenticationService authService)
    {
        Console.WriteLine("\n=== OAuth2 Authentication URLs ===");
        
        var redirectUri = "https://localhost:7000/api/auth/callback";
        var authUrl = authService.GetAuthorizationUrl("efactura", "sample-state-123");
        
        Console.WriteLine("For web applications, use these URLs:");
        Console.WriteLine($"1. Authorization URL: {authUrl}");
        Console.WriteLine($"2. Redirect URI: {redirectUri}");
        Console.WriteLine();
        Console.WriteLine("Authentication Flow:");
        Console.WriteLine("1. Redirect user browser to Authorization URL");
        Console.WriteLine("2. User selects USB certificate and confirms");
        Console.WriteLine("3. ANAF redirects to your Redirect URI with code");
        Console.WriteLine("4. Exchange code for JWT token using ExchangeCodeForTokenAsync()");
        Console.WriteLine("5. Use token for subsequent API calls");
        Console.WriteLine();
        Console.WriteLine("See ExampleControllers for complete implementation.");
    }

    private static void CreateSampleInvoiceOffline()
    {
        Console.WriteLine("\n=== Creating Sample Invoice (Offline) ===");

        var invoice = CreateSampleInvoice();
        
        Console.WriteLine($"✅ Created invoice: {invoice.Id}");
        Console.WriteLine($"Issue date: {invoice.IssueDate:yyyy-MM-dd}");
        Console.WriteLine($"Due date: {invoice.DueDate:yyyy-MM-dd}");
        Console.WriteLine($"Currency: {invoice.DocumentCurrencyCode}");
        Console.WriteLine($"Supplier: {invoice.AccountingSupplierParty?.PartyLegalEntity?.RegistrationName}");
        Console.WriteLine($"Customer: {invoice.AccountingCustomerParty?.PartyLegalEntity?.RegistrationName}");
        Console.WriteLine($"Lines: {invoice.InvoiceLines?.Count ?? 0}");
        Console.WriteLine($"Total (excl VAT): {invoice.LegalMonetaryTotal?.TaxExclusiveAmount?.Value} {invoice.LegalMonetaryTotal?.TaxExclusiveAmount?.CurrencyId}");
        Console.WriteLine($"VAT Amount: {invoice.TaxTotals?.FirstOrDefault()?.TaxAmount?.Value} {invoice.TaxTotals?.FirstOrDefault()?.TaxAmount?.CurrencyId}");
        Console.WriteLine($"Total (incl VAT): {invoice.LegalMonetaryTotal?.TaxInclusiveAmount?.Value} {invoice.LegalMonetaryTotal?.TaxInclusiveAmount?.CurrencyId}");
        
        Console.WriteLine("\nThis invoice object can be:");
        Console.WriteLine("- Validated using ValidateInvoiceAsync()");
        Console.WriteLine("- Uploaded using UploadInvoiceAsync()");
        Console.WriteLine("- Converted to XML for inspection");
        Console.WriteLine("- Used as a template for real invoices");
    }

    private static UblInvoice CreateSampleInvoice()
    {
        var invoice = new UblInvoice
        {
            Id = $"DEMO-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}",
            IssueDate = DateTime.Today,
            DueDate = DateTime.Today.AddDays(30),
            DocumentCurrencyCode = "RON"
        };

        // Supplier
        invoice.AccountingSupplierParty = new Party
        {
            PartyName = new PartyName { Name = "Demo Supplier SRL" },
            PostalAddress = new PostalAddress
            {
                StreetName = "Strada Demo nr. 1",
                CityName = "Bucuresti",
                PostalZone = "010101",
                Country = new Country { IdentificationCode = "RO" }
            },
            PartyTaxSchemes = new List<PartyTaxScheme>
            {
                new() { CompanyId = "RO12345678", TaxScheme = new TaxScheme { Id = "VAT" } }
            },
            PartyLegalEntity = new PartyLegalEntity
            {
                RegistrationName = "Demo Supplier SRL",
                CompanyId = "J40/12345/2020"
            },
            Contact = new Contact
            {
                Telephone = "+40721123456",
                ElectronicMail = "contact@demo-supplier.ro"
            }
        };

        // Customer
        invoice.AccountingCustomerParty = new Party
        {
            PartyName = new PartyName { Name = "Demo Customer SRL" },
            PostalAddress = new PostalAddress
            {
                StreetName = "Strada Client nr. 2",
                CityName = "Cluj-Napoca",
                PostalZone = "400001",
                Country = new Country { IdentificationCode = "RO" }
            },
            PartyTaxSchemes = new List<PartyTaxScheme>
            {
                new() { CompanyId = "RO87654321", TaxScheme = new TaxScheme { Id = "VAT" } }
            },
            PartyLegalEntity = new PartyLegalEntity
            {
                RegistrationName = "Demo Customer SRL",
                CompanyId = "J12/54321/2019"
            }
        };

        // Invoice lines
        var line1 = new InvoiceLine
        {
            Id = "1",
            InvoicedQuantity = new Quantity { Value = 2, UnitCode = "EA" },
            LineExtensionAmount = new Amount { Value = 200.00m, CurrencyId = "RON" },
            Item = new Item
            {
                Name = "Demo Product 1",
                ClassifiedTaxCategories = new List<TaxCategory>
                {
                    new() { Id = "S", Percent = 19, TaxScheme = new TaxScheme { Id = "VAT" } }
                }
            },
            Price = new Price
            {
                PriceAmount = new Amount { Value = 100.00m, CurrencyId = "RON" }
            }
        };

        invoice.InvoiceLines = new List<InvoiceLine> { line1 };

        // Tax totals
        var taxSubtotal = new TaxSubtotal
        {
            TaxableAmount = new Amount { Value = 200.00m, CurrencyId = "RON" },
            TaxAmount = new Amount { Value = 38.00m, CurrencyId = "RON" },
            TaxCategory = new TaxCategory
            {
                Id = "S",
                Percent = 19,
                TaxScheme = new TaxScheme { Id = "VAT" }
            }
        };

        invoice.TaxTotals = new List<TaxTotal>
        {
            new()
            {
                TaxAmount = new Amount { Value = 38.00m, CurrencyId = "RON" },
                TaxSubtotals = new List<TaxSubtotal> { taxSubtotal }
            }
        };

        // Monetary totals
        invoice.LegalMonetaryTotal = new MonetaryTotal
        {
            LineExtensionAmount = new Amount { Value = 200.00m, CurrencyId = "RON" },
            TaxExclusiveAmount = new Amount { Value = 200.00m, CurrencyId = "RON" },
            TaxInclusiveAmount = new Amount { Value = 238.00m, CurrencyId = "RON" },
            PayableAmount = new Amount { Value = 238.00m, CurrencyId = "RON" }
        };

        return invoice;
    }
}
