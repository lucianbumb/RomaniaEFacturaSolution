using RomaniaEFacturaLibrary.Models.Ubl;

namespace RomaniaEFacturaLibrary.Tests;

public class UblInvoiceTests
{
    [Fact]
    public void UblInvoice_DefaultConstructor_InitializesNamespaces()
    {
        // Act
        var invoice = new UblInvoice();

        // Assert
        Assert.NotNull(invoice.Namespaces);
        Assert.Equal("2.1", invoice.UblVersionId);
        Assert.Contains("urn:efactura.mfinante.ro:CIUS-RO:1.0.1", invoice.CustomizationId);
        Assert.Equal("RON", invoice.DocumentCurrencyCode);
        Assert.Equal("380", invoice.InvoiceTypeCode);
    }

    [Fact]
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
        Assert.Equal(testId, invoice.Id);
        Assert.Equal(testDate, invoice.IssueDate);
    }

    [Fact]
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
        Assert.Equal(testValue, amount.Value);
        Assert.Equal(testCurrency, amount.CurrencyId);
    }

    [Fact]
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
        Assert.Equal(testValue, quantity.Value);
        Assert.Equal(testUnit, quantity.UnitCode);
    }

    [Fact]
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
        Assert.Equal(partyName, party.PartyName);
        Assert.Equal(legalEntity, party.PartyLegalEntity);
        Assert.Equal("Test Company", party.PartyName.Name);
        Assert.Equal("Test Company SRL", party.PartyLegalEntity.RegistrationName);
    }

    [Fact]
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
        Assert.Equal("1", line.Id);
        Assert.Equal(quantity, line.InvoicedQuantity);
        Assert.Equal(amount, line.LineExtensionAmount);
        Assert.Equal(item, line.Item);
    }

    [Fact]
    public void TaxCategory_DefaultValues_AreCorrect()
    {
        // Act
        var taxCategory = new TaxCategory();

        // Assert
        Assert.Equal("S", taxCategory.Id); // Standard rate
    }

    [Fact]
    public void TaxScheme_DefaultValues_AreCorrect()
    {
        // Act
        var taxScheme = new TaxScheme();

        // Assert
        Assert.Equal("VAT", taxScheme.Id);
    }

    [Fact]
    public void Country_DefaultValues_AreCorrect()
    {
        // Act
        var country = new Country();

        // Assert
        Assert.Equal("RO", country.IdentificationCode);
    }

    [Fact]
    public void PaymentMeans_DefaultValues_AreCorrect()
    {
        // Act
        var paymentMeans = new PaymentMeans();

        // Assert
        Assert.Equal("31", paymentMeans.PaymentMeansCode); // Credit transfer
    }
}
