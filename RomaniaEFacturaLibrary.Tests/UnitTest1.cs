using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFacturaLibrary.Configuration;
using RomaniaEFacturaLibrary.Models.Ubl;
using RomaniaEFacturaLibrary.Services.Xml;
using Moq;

namespace RomaniaEFacturaLibrary.Tests;

[TestFixture]
public class XmlServiceTests
{
    private IXmlService _xmlService;
    private ILogger<XmlService> _logger;

    [SetUp]
    public void Setup()
    {
        _logger = Mock.Of<ILogger<XmlService>>();
        _xmlService = new XmlService(_logger);
    }

    [Test]
    public async Task SerializeInvoiceAsync_ValidInvoice_ReturnsValidXml()
    {
        // Arrange
        var invoice = CreateSampleInvoice();

        // Act
        var xml = await _xmlService.SerializeInvoiceAsync(invoice);

        // Assert
        Assert.That(xml, Is.Not.Null);
        Assert.That(xml, Does.Contain("<?xml"));
        Assert.That(xml, Does.Contain("<Invoice"));
        Assert.That(xml, Does.Contain(invoice.Id));
        Assert.That(xml, Does.Contain("urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"));
    }

    [Test]
    public async Task DeserializeInvoiceAsync_ValidXml_ReturnsInvoice()
    {
        // Arrange
        var originalInvoice = CreateSampleInvoice();
        var xml = await _xmlService.SerializeInvoiceAsync(originalInvoice);

        // Act
        var deserializedInvoice = await _xmlService.DeserializeInvoiceAsync(xml);

        // Assert
        Assert.That(deserializedInvoice, Is.Not.Null);
        Assert.That(deserializedInvoice.Id, Is.EqualTo(originalInvoice.Id));
        Assert.That(deserializedInvoice.IssueDate, Is.EqualTo(originalInvoice.IssueDate));
        Assert.That(deserializedInvoice.DocumentCurrencyCode, Is.EqualTo(originalInvoice.DocumentCurrencyCode));
    }

    [Test]
    public async Task ValidateXmlAsync_ValidXml_ReturnsSuccessfulValidation()
    {
        // Arrange
        var invoice = CreateSampleInvoice();
        var xml = await _xmlService.SerializeInvoiceAsync(invoice);

        // Act
        var validation = await _xmlService.ValidateXmlAsync(xml);

        // Assert
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation.IsWellFormed, Is.True);
        Assert.That(validation.IsValid, Is.True);
        Assert.That(validation.Errors, Is.Empty);
    }

    [Test]
    public async Task ValidateXmlAsync_InvalidXml_ReturnsFailedValidation()
    {
        // Arrange
        var invalidXml = "<Invalid>XML</Invalid>";

        // Act
        var validation = await _xmlService.ValidateXmlAsync(invalidXml);

        // Assert
        Assert.That(validation, Is.Not.Null);
        Assert.That(validation.IsValid, Is.False);
        Assert.That(validation.Errors, Is.Not.Empty);
    }

    [Test]
    public void CleanXml_XmlWithBom_RemovesBom()
    {
        // Arrange
        var xmlWithBom = "\uFEFF<?xml version=\"1.0\"?><test/>";
        var expectedLength = xmlWithBom.Length - 1; // Should be one character shorter

        // Act
        var cleanedXml = _xmlService.CleanXml(xmlWithBom);

        // Assert
        // Check length first to verify BOM was removed
        Assert.That(cleanedXml.Length, Is.EqualTo(expectedLength), 
            $"Expected length {expectedLength}, but got {cleanedXml.Length}. Input length: {xmlWithBom.Length}");
        
        // Check that it starts with XML declaration, not BOM
        Assert.That(cleanedXml, Does.StartWith("<?xml"), "XML should start with declaration");
        
        // Verify the first character is not BOM
        if (cleanedXml.Length > 0)
        {
            Assert.That((int)cleanedXml[0], Is.Not.EqualTo(0xFEFF), "First character should not be BOM");
        }
    }

    [Test]
    public void FormatXml_UnformattedXml_ReturnsFormattedXml()
    {
        // Arrange
        var unformattedXml = "<?xml version=\"1.0\"?><root><child>value</child></root>";

        // Act
        var formattedXml = _xmlService.FormatXml(unformattedXml);

        // Assert
        Assert.That(formattedXml, Is.Not.Null);
        Assert.That(formattedXml, Does.Contain("\n") | Does.Contain("\r"));
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
