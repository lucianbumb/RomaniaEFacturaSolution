using RomaniaEFactura.EditModels;
using RomaniaEFactura.Ubl;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Tests.EditModels;

/// <summary>
/// Item attributes (BG-32) — the name and value pairs that describe what was sold.
/// </summary>
/// <remarks>
/// Distinct from the item description: an attribute is structured, so a buyer's system can act on
/// it, where a description is prose a person reads. Common wherever the same product varies —
/// colour, size, a serial number.
/// </remarks>
public class ItemAttributeTests
{
    [Fact]
    public void AnInvoiceWithAttributesIsValid()
    {
        var report = EditModelValidator.Validate(WithAttributes());

        Assert.True(report.IsValid, report.ToString());
    }

    [Fact]
    public void TheAttributesReachTheDocument()
    {
        var line = WithAttributes().ToUbl().InvoiceLines[0];

        Assert.Collection(
            line.Item.AdditionalItemProperties,
            first =>
            {
                Assert.Equal("Culoare", first.Name);
                Assert.Equal("Albastru", first.Value);
            },
            second =>
            {
                Assert.Equal("Serie", second.Name);
                Assert.Equal("SN-4417", second.Value);
            });
    }

    [Fact]
    public void ALineWithoutAttributesEmitsNone()
    {
        Assert.Empty(SampleEditModels.MinimalInvoice().ToUbl().InvoiceLines[0].Item.AdditionalItemProperties);
    }

    [Fact]
    public void AnAttributeWithoutANameIsRejected()
    {
        var invoice = WithAttributes();
        invoice.Lines[0].ItemAttributes[0].Name = string.Empty;

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "Lines[0].ItemAttributes[0].Name");
    }

    [Fact]
    public void AnAttributeWithoutAValueIsRejected()
    {
        var invoice = WithAttributes();
        invoice.Lines[0].ItemAttributes[1].Value = string.Empty;

        Assert.Contains(
            EditModelValidator.Validate(invoice).Errors,
            f => f.Path == "Lines[0].ItemAttributes[1].Value");
    }

    [Fact]
    public void AnOverlongAttributeNameIsRejected()
    {
        // 50 for the name, 100 for the value — an asymmetry easy to get backwards.
        var invoice = WithAttributes();
        invoice.Lines[0].ItemAttributes[0].Name = new string('n', CiusRoLengths.ItemAttributeName + 1);

        Assert.Contains(
            EditModelValidator.Validate(invoice).Errors,
            f => f.Path == "Lines[0].ItemAttributes[0].Name");
    }

    [Fact]
    public void AValueLongerThanTheNameLimitIsStillFine()
    {
        var invoice = WithAttributes();
        invoice.Lines[0].ItemAttributes[0].Value = new string('v', CiusRoLengths.ItemAttributeName + 10);

        Assert.True(EditModelValidator.Validate(invoice).IsValid);
    }

    [Fact]
    public void TooManyAttributesOnOneLineAreRejected()
    {
        var invoice = WithAttributes();
        invoice.Lines[0].ItemAttributes =
        [
            .. Enumerable.Range(0, CiusRoLengths.MaxItemAttributes + 1)
                .Select(i => new ItemAttributeEditModel { Name = $"Attr {i}", Value = $"{i}" }),
        ];

        Assert.Contains("BR-RO-A052", EditModelValidator.Validate(invoice).ErrorCodes);
    }

    [Fact]
    public void FiftyAttributesAreAccepted()
    {
        var invoice = WithAttributes();
        invoice.Lines[0].ItemAttributes =
        [
            .. Enumerable.Range(0, CiusRoLengths.MaxItemAttributes)
                .Select(i => new ItemAttributeEditModel { Name = $"Attr {i}", Value = $"{i}" }),
        ];

        Assert.True(EditModelValidator.Validate(invoice).IsValid);
    }

    // ------------------------------------------------------------- the UBL path

    [Fact]
    public void TheUblPathRequiresBothHalves()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.InvoiceLines[0].Item.AdditionalItemProperties.Add(
            new ItemProperty { Name = "Culoare" });

        Assert.Contains("BR-54", CiusRoValidator.Validate(invoice).ErrorCodes);
    }

    [Fact]
    public void TheUblPathCapsTheNameAndTheValueSeparately()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.InvoiceLines[0].Item.AdditionalItemProperties.Add(new ItemProperty
        {
            Name = new string('n', CiusRoLengths.ItemAttributeName + 1),
            Value = new string('v', CiusRoLengths.ItemAttributeValue + 1),
        });

        var codes = CiusRoValidator.Validate(invoice).ErrorCodes;

        Assert.Contains("BR-RO-L0505", codes);
        Assert.Contains("BR-RO-L1025", codes);
    }

    [Fact]
    public void TheUblPathCapsTheCount()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        for (var i = 0; i <= CiusRoLengths.MaxItemAttributes; i++)
        {
            invoice.InvoiceLines[0].Item.AdditionalItemProperties.Add(
                new ItemProperty { Name = $"Attr {i}", Value = $"{i}" });
        }

        Assert.Contains("BR-RO-A052", CiusRoValidator.Validate(invoice).ErrorCodes);
    }

    [Fact]
    public void TheAttributesSurviveSerialization()
    {
        // AdditionalItemProperty sits after ClassifiedTaxCategory in the Item sequence. Emitting it
        // anywhere else is schema-invalid, and only a round trip catches it.
        var xml = UblSerializer.Serialize(WithAttributes().ToUbl());
        var parsed = UblSerializer.DeserializeInvoice(xml);

        Assert.Equal(2, parsed.InvoiceLines[0].Item.AdditionalItemProperties.Count);
        Assert.True(CiusRoValidator.Validate(parsed).IsValid);
    }

    private static InvoiceEditModel WithAttributes()
    {
        var invoice = SampleEditModels.MinimalInvoice();

        invoice.Lines[0].ItemAttributes =
        [
            new ItemAttributeEditModel { Name = "Culoare", Value = "Albastru" },
            new ItemAttributeEditModel { Name = "Serie", Value = "SN-4417" },
        ];

        return invoice;
    }
}
