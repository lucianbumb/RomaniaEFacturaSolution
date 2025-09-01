using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFacturaLibrary.Configuration;
using RomaniaEFacturaLibrary.Models.Ubl;
using RomaniaEFacturaLibrary.Services.Xml;
using Moq;

namespace RomaniaEFacturaLibrary.Tests;

public class XmlServiceTests
{
    private readonly IXmlService _xmlService;
    private readonly ILogger<XmlService> _logger;

    public XmlServiceTests()
    {
        _logger = Mock.Of<ILogger<XmlService>>();
        _xmlService = new XmlService(_logger);
    }

    [Fact]
    public async Task SerializeInvoiceAsync_ValidInvoice_ReturnsValidXml()
    {
        // Arrange
        var invoice = CreateSampleInvoice();

        // Act
        var xml = await _xmlService.SerializeInvoiceAsync(invoice);

        // Assert
        Assert.NotNull(xml);
        Assert.Contains("<?xml", xml);
        Assert.Contains("<Invoice", xml);
        Assert.Contains(invoice.Id, xml);
        Assert.Contains("urn:oasis:names:specification:ubl:schema:xsd:Invoice-2", xml);
    }

    [Fact]
    public async Task DeserializeInvoiceAsync_ValidXml_ReturnsInvoice()
    {
        // Arrange
        var originalInvoice = CreateSampleInvoice();
        var xml = await _xmlService.SerializeInvoiceAsync(originalInvoice);

        // Act
        var deserializedInvoice = await _xmlService.DeserializeInvoiceAsync(xml);

        // Assert
        Assert.NotNull(deserializedInvoice);
        Assert.Equal(originalInvoice.Id, deserializedInvoice.Id);
        Assert.Equal(originalInvoice.IssueDate, deserializedInvoice.IssueDate);
        Assert.Equal(originalInvoice.DocumentCurrencyCode, deserializedInvoice.DocumentCurrencyCode);
    }

    [Fact]
    public async Task ValidateXmlAsync_ValidXml_ReturnsSuccessfulValidation()
    {
        // Arrange
        var invoice = CreateSampleInvoice();
        var xml = await _xmlService.SerializeInvoiceAsync(invoice);

        // Act
        var validation = await _xmlService.ValidateXmlAsync(xml);

        // Assert
        Assert.NotNull(validation);
        Assert.True(validation.IsWellFormed);
        Assert.True(validation.IsValid);
        Assert.Empty(validation.Errors);
    }

    [Fact]
    public async Task ValidateXmlAsync_InvalidXml_ReturnsFailedValidation()
    {
        // Arrange
        var invalidXml = "<Invalid>XML</Invalid>";

        // Act
        var validation = await _xmlService.ValidateXmlAsync(invalidXml);

        // Assert
        Assert.NotNull(validation);
        Assert.False(validation.IsValid);
        Assert.NotEmpty(validation.Errors);
    }

    [Fact]
    public void CleanXml_XmlWithBom_RemovesBom()
    {
        // Arrange
        var xmlWithBom = "\uFEFF<?xml version=\"1.0\"?><test/>";
        var expectedLength = xmlWithBom.Length - 1; // Should be one character shorter

        // Act
        var cleanedXml = _xmlService.CleanXml(xmlWithBom);

        // Assert
        // Check length first to verify BOM was removed
        Assert.Equal(expectedLength, cleanedXml.Length);
        
        // Check that it starts with XML declaration, not BOM
        Assert.StartsWith("<?xml", cleanedXml);
        
        // Verify the first character is not BOM
        if (cleanedXml.Length > 0)
        {
            Assert.NotEqual(0xFEFF, (int)cleanedXml[0]);
        }
    }

    [Fact]
    public void FormatXml_UnformattedXml_ReturnsFormattedXml()
    {
        // Arrange
        var unformattedXml = "<?xml version=\"1.0\"?><root><child>value</child></root>";

        // Act
        var formattedXml = _xmlService.FormatXml(unformattedXml);

        // Assert
        Assert.NotNull(formattedXml);
        Assert.True(formattedXml.Contains("\n") || formattedXml.Contains("\r"));
    }

    private static UblInvoice CreateSampleInvoice()
    {
        return new UblInvoice
        {
            Id = "TEST-001",
            IssueDate = new DateTime(2024, 1, 15),
            DueDate = new DateTime(2024, 2, 15),
            DocumentCurrencyCode = "RON",
            AccountingSupplierParty = new Party
            {
                PartyLegalEntity = new PartyLegalEntity
                {
                    RegistrationName = "Test Supplier SRL",
                    CompanyId = "J40/12345/2020"
                },
                PartyTaxSchemes = new List<PartyTaxScheme>
                {
                    new() { CompanyId = "RO12345678", TaxScheme = new TaxScheme { Id = "VAT" } }
                },
                PostalAddress = new PostalAddress
                {
                    StreetName = "Test Street 1",
                    CityName = "Bucharest",
                    Country = new Country { IdentificationCode = "RO" }
                }
            },
            AccountingCustomerParty = new Party
            {
                PartyLegalEntity = new PartyLegalEntity
                {
                    RegistrationName = "Test Customer SRL",
                    CompanyId = "J12/54321/2019"
                },
                PartyTaxSchemes = new List<PartyTaxScheme>
                {
                    new() { CompanyId = "RO87654321", TaxScheme = new TaxScheme { Id = "VAT" } }
                },
                PostalAddress = new PostalAddress
                {
                    StreetName = "Customer Street 2",
                    CityName = "Cluj-Napoca",
                    Country = new Country { IdentificationCode = "RO" }
                }
            },
            InvoiceLines = new List<InvoiceLine>
            {
                new()
                {
                    Id = "1",
                    InvoicedQuantity = new Quantity { Value = 1, UnitCode = "EA" },
                    LineExtensionAmount = new Amount { Value = 100, CurrencyId = "RON" },
                    Item = new Item
                    {
                        Name = "Test Product",
                        ClassifiedTaxCategories = new List<TaxCategory>
                        {
                            new() { Id = "S", Percent = 19, TaxScheme = new TaxScheme { Id = "VAT" } }
                        }
                    },
                    Price = new Price
                    {
                        PriceAmount = new Amount { Value = 100, CurrencyId = "RON" }
                    }
                }
            },
            TaxTotals = new List<TaxTotal>
            {
                new()
                {
                    TaxAmount = new Amount { Value = 19, CurrencyId = "RON" },
                    TaxSubtotals = new List<TaxSubtotal>
                    {
                        new()
                        {
                            TaxableAmount = new Amount { Value = 100, CurrencyId = "RON" },
                            TaxAmount = new Amount { Value = 19, CurrencyId = "RON" },
                            TaxCategory = new TaxCategory
                            {
                                Id = "S",
                                Percent = 19,
                                TaxScheme = new TaxScheme { Id = "VAT" }
                            }
                        }
                    }
                }
            },
            LegalMonetaryTotal = new MonetaryTotal
            {
                LineExtensionAmount = new Amount { Value = 100, CurrencyId = "RON" },
                TaxExclusiveAmount = new Amount { Value = 100, CurrencyId = "RON" },
                TaxInclusiveAmount = new Amount { Value = 119, CurrencyId = "RON" },
                PayableAmount = new Amount { Value = 119, CurrencyId = "RON" }
            }
        };
    }
}
