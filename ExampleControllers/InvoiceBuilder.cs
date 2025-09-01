using RomaniaEFacturaLibrary.Models.Ubl;

namespace RomaniaEFacturaLibrary.Examples.Utilities;

/// <summary>
/// Utility class for building sample UBL invoices for testing and demonstration
/// </summary>
public static class InvoiceBuilder
{
    /// <summary>
    /// Creates a sample Romanian invoice with standard VAT
    /// </summary>
    /// <param name="invoiceNumber">Invoice number (optional, will generate if null)</param>
    /// <returns>Complete UBL invoice ready for validation/upload</returns>
    public static UblInvoice CreateSampleRomanianInvoice(string? invoiceNumber = null)
    {
        var invoice = new UblInvoice
        {
            Id = invoiceNumber ?? $"FACT-{DateTime.Now:yyyyMMdd}-{DateTime.Now.Ticks % 10000:D4}",
            IssueDate = DateTime.Today,
            DueDate = DateTime.Today.AddDays(30),
            DocumentCurrencyCode = "RON",
            InvoiceTypeCode = "380", // Commercial invoice
            Note = "Factura comercial? emis? conform legii române?ti"
        };

        // Supplier (your company)
        invoice.AccountingSupplierParty = CreateSupplier();

        // Customer
        invoice.AccountingCustomerParty = CreateCustomer();

        // Payment terms
        invoice.PaymentMeans = new List<PaymentMeans>
        {
            new()
            {
                PaymentMeansCode = "31", // Credit transfer
                PaymentId = invoice.Id,
                PayeeFinancialAccount = new FinancialAccount
                {
                    Id = "RO49AAAA1B31007593840000",
                    Name = "Cont bancar principal",
                    FinancialInstitutionBranch = new FinancialInstitutionBranch
                    {
                        Id = "AAAAROBB",
                        Name = "Banca Român? de Exemplu"
                    }
                }
            }
        };

        // Invoice lines
        invoice.InvoiceLines = CreateSampleInvoiceLines();

        // Calculate totals
        CalculateTotals(invoice);

        return invoice;
    }

    /// <summary>
    /// Creates a sample invoice with multiple products and different VAT rates
    /// </summary>
    /// <returns>UBL invoice with complex line items</returns>
    public static UblInvoice CreateComplexSampleInvoice()
    {
        var invoice = CreateSampleRomanianInvoice();
        invoice.Id = $"COMPLEX-{DateTime.Now:yyyyMMdd}-{DateTime.Now.Ticks % 10000:D4}";

        // Add more complex invoice lines
        invoice.InvoiceLines = new List<InvoiceLine>
        {
            // Standard VAT product
            CreateInvoiceLine("1", "Laptop Dell Latitude 5520", 1, "EA", 2500.00m, 19),
            
            // Standard VAT service
            CreateInvoiceLine("2", "Servicii consultan?? IT", 40, "HUR", 150.00m, 19),
            
            // Reduced VAT product (books)
            CreateInvoiceLine("3", "C?r?i tehnice software", 5, "EA", 50.00m, 5),
            
            // Zero VAT (exports)
            CreateInvoiceLine("4", "Export servicii UE", 1, "EA", 1000.00m, 0),
        };

        // Recalculate totals for complex invoice
        CalculateTotals(invoice);

        return invoice;
    }

    /// <summary>
    /// Creates a minimal invoice for testing basic functionality
    /// </summary>
    /// <returns>Simple UBL invoice</returns>
    public static UblInvoice CreateMinimalInvoice()
    {
        var invoice = new UblInvoice
        {
            Id = $"MIN-{DateTime.Now:yyyyMMdd}-{DateTime.Now.Ticks % 10000:D4}",
            IssueDate = DateTime.Today,
            DocumentCurrencyCode = "RON"
        };

        // Minimal supplier
        invoice.AccountingSupplierParty = new Party
        {
            PartyLegalEntity = new PartyLegalEntity
            {
                RegistrationName = "SC Test Minimal SRL",
                CompanyId = "J40/12345/2020"
            },
            PartyTaxSchemes = new List<PartyTaxScheme>
            {
                new() { CompanyId = "RO12345678", TaxScheme = new TaxScheme { Id = "VAT" } }
            }
        };

        // Minimal customer
        invoice.AccountingCustomerParty = new Party
        {
            PartyLegalEntity = new PartyLegalEntity
            {
                RegistrationName = "SC Client Test SRL",
                CompanyId = "J12/54321/2019"
            }
        };

        // Single invoice line
        invoice.InvoiceLines = new List<InvoiceLine>
        {
            CreateInvoiceLine("1", "Produs test", 1, "EA", 100.00m, 19)
        };

        CalculateTotals(invoice);

        return invoice;
    }

    private static Party CreateSupplier()
    {
        return new Party
        {
            PartyName = new PartyName
            {
                Name = "SC Example Software SRL"
            },
            PartyLegalEntity = new PartyLegalEntity
            {
                RegistrationName = "SC Example Software SRL",
                CompanyId = "J40/12345/2020"
            },
            PartyTaxSchemes = new List<PartyTaxScheme>
            {
                new()
                {
                    CompanyId = "RO12345678",
                    TaxScheme = new TaxScheme { Id = "VAT" }
                }
            },
            PostalAddress = new PostalAddress
            {
                StreetName = "Strada Exemplu nr. 123",
                CityName = "Bucure?ti",
                PostalZone = "012345",
                Country = new Country { IdentificationCode = "RO" }
            },
            Contact = new Contact
            {
                Name = "Ion Popescu",
                Telephone = "+40721234567",
                ElectronicMail = "contact@example-software.ro"
            }
        };
    }

    private static Party CreateCustomer()
    {
        return new Party
        {
            PartyName = new PartyName
            {
                Name = "SC Client Example SRL"
            },
            PartyLegalEntity = new PartyLegalEntity
            {
                RegistrationName = "SC Client Example SRL",
                CompanyId = "J12/54321/2019"
            },
            PartyTaxSchemes = new List<PartyTaxScheme>
            {
                new()
                {
                    CompanyId = "RO87654321",
                    TaxScheme = new TaxScheme { Id = "VAT" }
                }
            },
            PostalAddress = new PostalAddress
            {
                StreetName = "Strada Client nr. 456",
                CityName = "Cluj-Napoca",
                PostalZone = "400000",
                Country = new Country { IdentificationCode = "RO" }
            },
            Contact = new Contact
            {
                Name = "Maria Ionescu",
                Telephone = "+40734567890",
                ElectronicMail = "maria@client-example.ro"
            }
        };
    }

    private static List<InvoiceLine> CreateSampleInvoiceLines()
    {
        return new List<InvoiceLine>
        {
            CreateInvoiceLine("1", "Licen?? software ERP", 1, "EA", 1500.00m, 19),
            CreateInvoiceLine("2", "Servicii implementare", 20, "HUR", 200.00m, 19),
            CreateInvoiceLine("3", "Suport tehnic anual", 1, "EA", 800.00m, 19)
        };
    }

    private static InvoiceLine CreateInvoiceLine(
        string id, 
        string itemName, 
        decimal quantity, 
        string unitCode, 
        decimal unitPrice, 
        decimal vatPercent)
    {
        var lineAmount = Math.Round(quantity * unitPrice, 2);
        
        return new InvoiceLine
        {
            Id = id,
            InvoicedQuantity = new Quantity
            {
                Value = quantity,
                UnitCode = unitCode
            },
            LineExtensionAmount = new Amount
            {
                Value = lineAmount,
                CurrencyId = "RON"
            },
            Item = new Item
            {
                Name = itemName,
                ClassifiedTaxCategories = new List<TaxCategory>
                {
                    new()
                    {
                        Id = vatPercent > 0 ? "S" : (vatPercent == 0 ? "Z" : "E"),
                        Percent = vatPercent,
                        TaxScheme = new TaxScheme { Id = "VAT" }
                    }
                }
            },
            Price = new Price
            {
                PriceAmount = new Amount
                {
                    Value = unitPrice,
                    CurrencyId = "RON"
                }
            }
        };
    }

    private static void CalculateTotals(UblInvoice invoice)
    {
        if (invoice.InvoiceLines == null || !invoice.InvoiceLines.Any())
        {
            return;
        }

        var lineTotal = invoice.InvoiceLines.Sum(l => l.LineExtensionAmount?.Value ?? 0);
        
        // Group by VAT rate
        var vatGroups = invoice.InvoiceLines
            .GroupBy(l => l.Item?.ClassifiedTaxCategories?.FirstOrDefault()?.Percent ?? 0)
            .ToList();

        var taxSubtotals = new List<TaxSubtotal>();
        var totalTaxAmount = 0m;

        foreach (var vatGroup in vatGroups)
        {
            var vatRate = vatGroup.Key;
            var taxableAmount = vatGroup.Sum(l => l.LineExtensionAmount?.Value ?? 0);
            var taxAmount = Math.Round(taxableAmount * vatRate / 100, 2);
            totalTaxAmount += taxAmount;

            taxSubtotals.Add(new TaxSubtotal
            {
                TaxableAmount = new Amount { Value = taxableAmount, CurrencyId = "RON" },
                TaxAmount = new Amount { Value = taxAmount, CurrencyId = "RON" },
                TaxCategory = new TaxCategory
                {
                    Id = vatRate > 0 ? "S" : (vatRate == 0 ? "Z" : "E"),
                    Percent = vatRate,
                    TaxScheme = new TaxScheme { Id = "VAT" }
                }
            });
        }

        // Tax totals
        invoice.TaxTotals = new List<TaxTotal>
        {
            new()
            {
                TaxAmount = new Amount { Value = totalTaxAmount, CurrencyId = "RON" },
                TaxSubtotals = taxSubtotals
            }
        };

        // Monetary totals
        invoice.LegalMonetaryTotal = new MonetaryTotal
        {
            LineExtensionAmount = new Amount { Value = lineTotal, CurrencyId = "RON" },
            TaxExclusiveAmount = new Amount { Value = lineTotal, CurrencyId = "RON" },
            TaxInclusiveAmount = new Amount { Value = lineTotal + totalTaxAmount, CurrencyId = "RON" },
            PayableAmount = new Amount { Value = lineTotal + totalTaxAmount, CurrencyId = "RON" }
        };
    }

    /// <summary>
    /// Creates an invoice builder with fluent interface for custom invoices
    /// </summary>
    /// <param name="invoiceId">Invoice ID</param>
    /// <returns>Fluent invoice builder</returns>
    public static FluentInvoiceBuilder Create(string invoiceId)
    {
        return new FluentInvoiceBuilder(invoiceId);
    }
}

/// <summary>
/// Fluent builder for creating custom invoices
/// </summary>
public class FluentInvoiceBuilder
{
    private readonly UblInvoice _invoice;

    internal FluentInvoiceBuilder(string invoiceId)
    {
        _invoice = new UblInvoice
        {
            Id = invoiceId,
            IssueDate = DateTime.Today,
            DocumentCurrencyCode = "RON"
        };
    }

    public FluentInvoiceBuilder WithIssueDate(DateTime issueDate)
    {
        _invoice.IssueDate = issueDate;
        return this;
    }

    public FluentInvoiceBuilder WithDueDate(DateTime dueDate)
    {
        _invoice.DueDate = dueDate;
        return this;
    }

    public FluentInvoiceBuilder WithCurrency(string currencyCode)
    {
        _invoice.DocumentCurrencyCode = currencyCode;
        return this;
    }

    public FluentInvoiceBuilder WithSupplier(string name, string registrationName, string companyId, string vatId)
    {
        _invoice.AccountingSupplierParty = new Party
        {
            PartyName = new PartyName { Name = name },
            PartyLegalEntity = new PartyLegalEntity
            {
                RegistrationName = registrationName,
                CompanyId = companyId
            },
            PartyTaxSchemes = new List<PartyTaxScheme>
            {
                new() { CompanyId = vatId, TaxScheme = new TaxScheme { Id = "VAT" } }
            }
        };
        return this;
    }

    public FluentInvoiceBuilder WithCustomer(string name, string registrationName, string companyId, string vatId)
    {
        _invoice.AccountingCustomerParty = new Party
        {
            PartyName = new PartyName { Name = name },
            PartyLegalEntity = new PartyLegalEntity
            {
                RegistrationName = registrationName,
                CompanyId = companyId
            },
            PartyTaxSchemes = new List<PartyTaxScheme>
            {
                new() { CompanyId = vatId, TaxScheme = new TaxScheme { Id = "VAT" } }
            }
        };
        return this;
    }

    public FluentInvoiceBuilder AddLine(string itemName, decimal quantity, string unitCode, decimal unitPrice, decimal vatPercent)
    {
        _invoice.InvoiceLines ??= new List<InvoiceLine>();
        
        var lineNumber = (_invoice.InvoiceLines.Count + 1).ToString();
        var lineAmount = Math.Round(quantity * unitPrice, 2);

        _invoice.InvoiceLines.Add(new InvoiceLine
        {
            Id = lineNumber,
            InvoicedQuantity = new Quantity { Value = quantity, UnitCode = unitCode },
            LineExtensionAmount = new Amount { Value = lineAmount, CurrencyId = _invoice.DocumentCurrencyCode },
            Item = new Item
            {
                Name = itemName,
                ClassifiedTaxCategories = new List<TaxCategory>
                {
                    new()
                    {
                        Id = vatPercent > 0 ? "S" : "Z",
                        Percent = vatPercent,
                        TaxScheme = new TaxScheme { Id = "VAT" }
                    }
                }
            },
            Price = new Price
            {
                PriceAmount = new Amount { Value = unitPrice, CurrencyId = _invoice.DocumentCurrencyCode }
            }
        });

        return this;
    }

    public UblInvoice Build()
    {
        // Calculate totals before returning
        var lineTotal = _invoice.InvoiceLines?.Sum(l => l.LineExtensionAmount?.Value ?? 0) ?? 0;
        var vatGroups = _invoice.InvoiceLines?
            .GroupBy(l => l.Item?.ClassifiedTaxCategories?.FirstOrDefault()?.Percent ?? 0) 
            ?? new List<IGrouping<decimal, InvoiceLine>>();

        var taxSubtotals = new List<TaxSubtotal>();
        var totalTaxAmount = 0m;

        foreach (var vatGroup in vatGroups)
        {
            var vatRate = vatGroup.Key;
            var taxableAmount = vatGroup.Sum(l => l.LineExtensionAmount?.Value ?? 0);
            var taxAmount = Math.Round(taxableAmount * vatRate / 100, 2);
            totalTaxAmount += taxAmount;

            taxSubtotals.Add(new TaxSubtotal
            {
                TaxableAmount = new Amount { Value = taxableAmount, CurrencyId = _invoice.DocumentCurrencyCode },
                TaxAmount = new Amount { Value = taxAmount, CurrencyId = _invoice.DocumentCurrencyCode },
                TaxCategory = new TaxCategory
                {
                    Id = vatRate > 0 ? "S" : "Z",
                    Percent = vatRate,
                    TaxScheme = new TaxScheme { Id = "VAT" }
                }
            });
        }

        _invoice.TaxTotals = new List<TaxTotal>
        {
            new()
            {
                TaxAmount = new Amount { Value = totalTaxAmount, CurrencyId = _invoice.DocumentCurrencyCode },
                TaxSubtotals = taxSubtotals
            }
        };

        _invoice.LegalMonetaryTotal = new MonetaryTotal
        {
            LineExtensionAmount = new Amount { Value = lineTotal, CurrencyId = _invoice.DocumentCurrencyCode },
            TaxExclusiveAmount = new Amount { Value = lineTotal, CurrencyId = _invoice.DocumentCurrencyCode },
            TaxInclusiveAmount = new Amount { Value = lineTotal + totalTaxAmount, CurrencyId = _invoice.DocumentCurrencyCode },
            PayableAmount = new Amount { Value = lineTotal + totalTaxAmount, CurrencyId = _invoice.DocumentCurrencyCode }
        };

        return _invoice;
    }
}