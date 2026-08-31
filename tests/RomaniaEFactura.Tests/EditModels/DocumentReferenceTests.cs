using RomaniaEFactura.EditModels;
using RomaniaEFactura.Ubl;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Tests.EditModels;

/// <summary>
/// The document references EN16931 defines beyond the purchase order, and the third address line.
/// </summary>
/// <remarks>
/// Both are small. They are here together because they share the same failure mode: UBL puts each
/// in a different element at a different position in the sequence, and the only thing that catches
/// a wrong position is a round trip.
/// </remarks>
public class DocumentReferenceTests
{
    [Fact]
    public void AnInvoiceCarryingEveryReferenceIsValid()
    {
        var report = EditModelValidator.Validate(WithReferences());

        Assert.True(report.IsValid, report.ToString());
    }

    [Fact]
    public void EachReferenceReachesItsOwnElement()
    {
        var ubl = WithReferences().ToUbl();

        Assert.Equal("CTR-2026-1", ubl.ContractDocumentReference!.Id.Value);
        Assert.Equal("RCV-2026-1", ubl.ReceiptDocumentReference!.Id.Value);
        Assert.Equal("DSP-2026-1", ubl.DespatchDocumentReference!.Id.Value);
        Assert.Equal("TND-2026-1", ubl.OriginatorDocumentReference!.Id.Value);
    }

    [Fact]
    public void TheBuyersOrderAndTheSellersShareOneElement()
    {
        // BT-13 and BT-14 are both children of cac:OrderReference, which is why they cannot be
        // mapped independently.
        var ubl = WithReferences().ToUbl();

        Assert.Equal("PO-2026-1", ubl.OrderReference!.Id.Value);
        Assert.Equal("SO-2026-1", ubl.OrderReference.SalesOrderId);
    }

    [Fact]
    public void ASalesOrderAloneStillProducesAnOrderReference()
    {
        // The consequence of sharing an element: stating only BT-14 still needs the wrapper, with
        // an empty cbc:ID.
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.SalesOrderReference = "SO-2026-9";

        var ubl = invoice.ToUbl();

        Assert.NotNull(ubl.OrderReference);
        Assert.Equal("SO-2026-9", ubl.OrderReference.SalesOrderId);
        Assert.True(EditModelValidator.Validate(invoice).IsValid);
    }

    [Fact]
    public void NoReferencesMeansNoElements()
    {
        var ubl = SampleEditModels.MinimalInvoice().ToUbl();

        Assert.Null(ubl.OrderReference);
        Assert.Null(ubl.ContractDocumentReference);
        Assert.Null(ubl.ReceiptDocumentReference);
        Assert.Null(ubl.DespatchDocumentReference);
        Assert.Null(ubl.OriginatorDocumentReference);
    }

    [Theory]
    [InlineData("ContractReference")]
    [InlineData("SalesOrderReference")]
    [InlineData("ReceivingAdviceReference")]
    [InlineData("DespatchAdviceReference")]
    [InlineData("TenderOrLotReference")]
    public void AnOverlongReferenceIsRejected(string property)
    {
        var invoice = SampleEditModels.MinimalInvoice();
        var value = new string('r', CiusRoLengths.ContractReference + 1);
        typeof(InvoiceEditModel).GetProperty(property)!.SetValue(invoice, value);

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == property);
    }

    [Fact]
    public void TheUblPathCapsEachReferenceUnderItsOwnRule()
    {
        // Five rules, five fields. Reporting one id for all of them would send a reader to the
        // wrong line of the Schematron four times out of five.
        var invoice = WithReferences().ToUbl();
        var overlong = new string('r', CiusRoLengths.ContractReference + 1);

        invoice.ContractDocumentReference!.Id = overlong;
        invoice.OrderReference!.SalesOrderId = overlong;
        invoice.ReceiptDocumentReference!.Id = overlong;
        invoice.DespatchDocumentReference!.Id = overlong;
        invoice.OriginatorDocumentReference!.Id = overlong;

        var codes = CiusRoValidator.Validate(invoice).ErrorCodes;

        Assert.Contains("BR-RO-L0302", codes);
        Assert.Contains("BR-RO-L0304", codes);
        Assert.Contains("BR-RO-L0305", codes);
        Assert.Contains("BR-RO-L0306", codes);
        Assert.Contains("BR-RO-L0307", codes);
    }

    // ------------------------------------------------------- third address line

    [Fact]
    public void AThirdAddressLineReachesItsNestedElement()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Seller.Address.AddressLine3 = "Etaj 3, birou 12";

        var address = invoice.ToUbl().AccountingSupplierParty.Party.PostalAddress;

        Assert.Equal("Etaj 3, birou 12", address!.AddressLine!.Line);
    }

    [Fact]
    public void NoThirdLineMeansNoElement()
    {
        var address = SampleEditModels.MinimalInvoice().ToUbl().AccountingSupplierParty.Party.PostalAddress;

        Assert.Null(address!.AddressLine);
    }

    [Fact]
    public void AnOverlongThirdLineIsRejected()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Buyer.Address.AddressLine3 = new string('l', CiusRoLengths.AddressLine2 + 1);

        Assert.Contains(
            EditModelValidator.Validate(invoice).Errors,
            f => f.Path == "Buyer.Address.AddressLine3");
    }

    [Fact]
    public void TheUblPathCapsEachRolesThirdLineSeparately()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        var overlong = new AddressLine { Line = new string('l', CiusRoLengths.AddressLine2 + 1) };

        invoice.AccountingSupplierParty.Party.PostalAddress!.AddressLine = overlong;
        invoice.AccountingCustomerParty.Party.PostalAddress!.AddressLine = overlong;

        var codes = CiusRoValidator.Validate(invoice).ErrorCodes;

        Assert.Contains("BR-RO-L1003", codes);
        Assert.Contains("BR-RO-L1008", codes);
    }

    [Fact]
    public void EverythingSurvivesSerialization()
    {
        // The point of this test. Each of these sits at a specific position in its sequence —
        // AddressLine between CountrySubentity and Country, the four references between
        // BillingReference and AdditionalDocumentReference — and a wrong position produces a
        // schema-invalid document that no other assertion here would notice.
        var invoice = WithReferences();
        invoice.Seller.Address.AddressLine3 = "Etaj 3";

        var xml = UblSerializer.Serialize(invoice.ToUbl());
        var parsed = UblSerializer.DeserializeInvoice(xml);

        Assert.Equal("CTR-2026-1", parsed.ContractDocumentReference!.Id.Value);
        Assert.Equal("SO-2026-1", parsed.OrderReference!.SalesOrderId);
        Assert.Equal("Etaj 3", parsed.AccountingSupplierParty.Party.PostalAddress!.AddressLine!.Line);
        Assert.True(CiusRoValidator.Validate(parsed).IsValid);
    }

    private static InvoiceEditModel WithReferences()
    {
        var invoice = SampleEditModels.MinimalInvoice();

        invoice.OrderReference = "PO-2026-1";
        invoice.SalesOrderReference = "SO-2026-1";
        invoice.ContractReference = "CTR-2026-1";
        invoice.ReceivingAdviceReference = "RCV-2026-1";
        invoice.DespatchAdviceReference = "DSP-2026-1";
        invoice.TenderOrLotReference = "TND-2026-1";

        return invoice;
    }
}
