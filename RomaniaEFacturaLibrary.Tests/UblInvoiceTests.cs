using RomaniaEFacturaLibrary.Models.Ubl;

namespace RomaniaEFacturaLibrary.Tests;

[TestFixture]
public class UblInvoiceTests
{
    [Test]
    public void UblInvoice_DefaultConstructor_InitializesNamespaces()
    {
        // Act
        var invoice = new UblInvoice();

        // Assert
        Assert.That(invoice.Namespaces, Is.Not.Null);
        Assert.That(invoice.UblVersionId, Is.EqualTo("2.1"));
        Assert.That(invoice.CustomizationId, Does.Contain("urn:efactura.mfinante.ro:CIUS-RO:1.0.1"));
        Assert.That(invoice.DocumentCurrencyCode, Is.EqualTo("RON"));
        Assert.That(invoice.InvoiceTypeCode, Is.EqualTo("380"));
    }

    [Test]
    public void UblInvoice_SetProperties_PropertiesAreSet()
    {
        // Arrange
        var invoice = new UblInvoice();
        var testId = "TEST-001";
        var testDate = new DateTime(2024, 1, 15);

        // Act
        invoice.Id = testId;
        invoice.IssueDate = testDate;

        // Assert
        Assert.That(invoice.Id, Is.EqualTo(testId));
        Assert.That(invoice.IssueDate, Is.EqualTo(testDate));
    }

    [Test]
    public void Amount_SetValue_ValueIsSet()
    {
        // Arrange
        var amount = new Amount();
        const decimal testValue = 123.45m;
        const string testCurrency = "EUR";

        // Act
        amount.Value = testValue;
        amount.CurrencyId = testCurrency;

        // Assert
        Assert.That(amount.Value, Is.EqualTo(testValue));
        Assert.That(amount.CurrencyId, Is.EqualTo(testCurrency));
    }

    [Test]
    public void Quantity_SetValue_ValueIsSet()
    {
        // Arrange
        var quantity = new Quantity();
        const decimal testValue = 5.0m;
        const string testUnit = "KG";

        // Act
        quantity.Value = testValue;
        quantity.UnitCode = testUnit;

        // Assert
        Assert.That(quantity.Value, Is.EqualTo(testValue));
        Assert.That(quantity.UnitCode, Is.EqualTo(testUnit));
    }

    [Test]
    public void Party_SetProperties_PropertiesAreSet()
    {
        // Arrange
        var party = new Party();
        var partyName = new PartyName { Name = "Test Company" };
        var legalEntity = new PartyLegalEntity 
        { 
            RegistrationName = "Test Company SRL",
            CompanyId = "J40/12345/2020"
        };

        // Act
        party.PartyName = partyName;
        party.PartyLegalEntity = legalEntity;

        // Assert
        Assert.That(party.PartyName, Is.EqualTo(partyName));
        Assert.That(party.PartyLegalEntity, Is.EqualTo(legalEntity));
        Assert.That(party.PartyName.Name, Is.EqualTo("Test Company"));
        Assert.That(party.PartyLegalEntity.RegistrationName, Is.EqualTo("Test Company SRL"));
    }

    [Test]
    public void InvoiceLine_SetProperties_PropertiesAreSet()
    {
        // Arrange
        var line = new InvoiceLine();
        var quantity = new Quantity { Value = 2, UnitCode = "EA" };
        var amount = new Amount { Value = 200, CurrencyId = "RON" };
        var item = new Item { Name = "Test Product" };

        // Act
        line.Id = "1";
        line.InvoicedQuantity = quantity;
        line.LineExtensionAmount = amount;
        line.Item = item;

        // Assert
        Assert.That(line.Id, Is.EqualTo("1"));
        Assert.That(line.InvoicedQuantity, Is.EqualTo(quantity));
        Assert.That(line.LineExtensionAmount, Is.EqualTo(amount));
        Assert.That(line.Item, Is.EqualTo(item));
    }

    [Test]
    public void TaxCategory_DefaultValues_AreCorrect()
    {
        // Act
        var taxCategory = new TaxCategory();

        // Assert
        Assert.That(taxCategory.Id, Is.EqualTo("S")); // Standard rate
    }

    [Test]
    public void TaxScheme_DefaultValues_AreCorrect()
    {
        // Act
        var taxScheme = new TaxScheme();

        // Assert
        Assert.That(taxScheme.Id, Is.EqualTo("VAT"));
    }

    [Test]
    public void Country_DefaultValues_AreCorrect()
    {
        // Act
        var country = new Country();

        // Assert
        Assert.That(country.IdentificationCode, Is.EqualTo("RO"));
    }

    [Test]
    public void PaymentMeans_DefaultValues_AreCorrect()
    {
        // Act
        var paymentMeans = new PaymentMeans();

        // Assert
        Assert.That(paymentMeans.PaymentMeansCode, Is.EqualTo("31")); // Credit transfer
    }
}
