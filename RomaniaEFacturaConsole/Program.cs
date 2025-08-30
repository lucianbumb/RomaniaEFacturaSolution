using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RomaniaEFacturaLibrary.Extensions;
using RomaniaEFacturaLibrary.Models.Ubl;
using RomaniaEFacturaLibrary.Services;

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

            // Show menu
            await ShowMenuAsync(eFacturaClient, logger);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting application: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    private static async Task ShowMenuAsync(IEFacturaClient client, ILogger logger)
    {
        while (true)
        {
            Console.WriteLine("\n=== EFactura Operations ===");
            Console.WriteLine("1. Create and validate sample invoice");
            Console.WriteLine("2. Upload sample invoice");
            Console.WriteLine("3. Check upload status");
            Console.WriteLine("4. List recent invoices");
            Console.WriteLine("5. Download invoice");
            Console.WriteLine("6. Convert invoice to PDF");
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
                    case "0":
                        Console.WriteLine("Goodbye!");
                        return;
                    default:
                        Console.WriteLine("Invalid option. Please try again.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
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
            foreach (var error in validation.Errors)
            {
                Console.WriteLine($"  - {error.Message}");
            }
        }

        if (validation.Warnings.Count > 0)
        {
            Console.WriteLine("⚠️ Warnings:");
            foreach (var warning in validation.Warnings)
            {
                Console.WriteLine($"  - {warning.Message}");
            }
        }
    }

    private static async Task UploadSampleInvoiceAsync(IEFacturaClient client, ILogger logger)
    {
        Console.WriteLine("\n=== Uploading Sample Invoice ===");

        var invoice = CreateSampleInvoice();
        
        Console.WriteLine("Uploading invoice to SPV...");
        var uploadResponse = await client.UploadInvoiceAsync(invoice);

        if (uploadResponse.IsSuccess)
        {
            Console.WriteLine($"✅ Invoice uploaded successfully!");
            Console.WriteLine($"Upload ID: {uploadResponse.UploadId}");
            
            // Wait for processing
            Console.WriteLine("Waiting for processing...");
            var finalStatus = await client.WaitForUploadCompletionAsync(uploadResponse.UploadId);
            
            Console.WriteLine($"Final status: {finalStatus.Status}");
            if (!string.IsNullOrEmpty(finalStatus.Details))
            {
                Console.WriteLine($"Details: {finalStatus.Details}");
            }
        }
        else
        {
            Console.WriteLine($"❌ Upload failed: {uploadResponse.Error}");
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
        
        if (!string.IsNullOrEmpty(status.Details))
        {
            Console.WriteLine($"Details: {status.Details}");
        }

        if (status.Validation != null)
        {
            Console.WriteLine($"Validation success: {status.Validation.Success}");
            
            if (status.Validation.Errors.Count > 0)
            {
                Console.WriteLine("Errors:");
                foreach (var error in status.Validation.Errors)
                {
                    Console.WriteLine($"  - {error.Message}");
                }
            }
        }
    }

    private static async Task ListRecentInvoicesAsync(IEFacturaClient client, ILogger logger)
    {
        Console.WriteLine("\n=== Recent Invoices ===");
        
        var from = DateTime.Now.AddDays(-30);
        var to = DateTime.Now;
        
        Console.WriteLine($"Fetching invoices from {from:yyyy-MM-dd} to {to:yyyy-MM-dd}...");
        
        var invoices = await client.GetInvoicesAsync(from, to);
        
        if (invoices.Count == 0)
        {
            Console.WriteLine("No invoices found for the specified period.");
            return;
        }

        Console.WriteLine($"Found {invoices.Count} invoices:");
        foreach (var invoice in invoices.Take(10)) // Show first 10
        {
            Console.WriteLine($"  {invoice.Id} - {invoice.CreationDate:yyyy-MM-dd HH:mm} - {invoice.Type}");
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
        
        // Save to file
        var fileName = $"invoice_{invoice.Id}_{DateTime.Now:yyyyMMdd_HHmmss}.xml";
        // You could serialize and save here if needed
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

    private static UblInvoice CreateSampleInvoice()
    {
        var invoice = new UblInvoice
        {
            Id = $"DEMO-{DateTime.Now:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}",
            IssueDate = DateTime.Today,
            DueDate = DateTime.Today.AddDays(30),
            DocumentCurrencyCode = "RON"
        };

        // Add notes
        invoice.Notes.Add("This is a sample invoice for testing purposes");

        // Supplier
        invoice.AccountingSupplierParty = new Party
        {
            PartyName = new PartyName { Name = "Demo Supplier SRL" },
            PostalAddress = new PostalAddress
            {
                StreetName = "Strada Demo nr. 1",
                CityName = "Bucuresti",
                CountrySubentity = "RO-B",
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
                CountrySubentity = "RO-CJ",
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

        // Payment means
        invoice.PaymentMeans.Add(new PaymentMeans
        {
            PaymentMeansCode = "31", // Credit transfer
            PayeeFinancialAccount = new FinancialAccount
            {
                Id = "RO49AAAA1B31007593840000", // Sample IBAN
                Name = "Demo Supplier SRL"
            }
        });

        // Invoice lines
        var line1 = new InvoiceLine
        {
            Id = "1",
            InvoicedQuantity = new Quantity { Value = 2, UnitCode = "EA" },
            LineExtensionAmount = new Amount { Value = 200.00m, CurrencyId = "RON" },
            Item = new Item
            {
                Name = "Demo Product 1",
                Description = "Sample product for testing",
                SellersItemIdentification = new ItemIdentification { Id = "DEMO-001" },
                ClassifiedTaxCategories = new List<TaxCategory>
                {
                    new() { Id = "S", Percent = 19, TaxScheme = new TaxScheme { Id = "VAT" } }
                }
            },
            Price = new Price
            {
                PriceAmount = new Amount { Value = 100.00m, CurrencyId = "RON" },
                BaseQuantity = new Quantity { Value = 1, UnitCode = "EA" }
            }
        };

        invoice.InvoiceLines.Add(line1);

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

        invoice.TaxTotals.Add(new TaxTotal
        {
            TaxAmount = new Amount { Value = 38.00m, CurrencyId = "RON" },
            TaxSubtotals = new List<TaxSubtotal> { taxSubtotal }
        });

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
