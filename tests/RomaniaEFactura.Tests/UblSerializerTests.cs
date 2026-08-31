using System.Xml.Linq;
using RomaniaEFactura.Ubl;

namespace RomaniaEFactura.Tests;

/// <summary>
/// Serialization tests, several of which are regression guards for defects that made v2's output
/// schema-invalid. Each of those fails against the v2 models and passes against these.
/// </summary>
public class UblSerializerTests
{
    private static readonly XNamespace Cbc = UblNamespaces.Cbc;
    private static readonly XNamespace Cac = UblNamespaces.Cac;

    [Fact]
    public void Serialize_IssueDate_EmitsPlainDateNotDateTime()
    {
        // v2 emitted "2026-08-31T00:00:00" because the member lacked DataType="date",
        // which the UBL schema rejects.
        var xml = XDocument.Parse(UblSerializer.Serialize(SampleDocuments.MinimalInvoice()));

        var issueDate = xml.Root!.Element(Cbc + "IssueDate")!.Value;

        Assert.Equal("2026-08-31", issueDate);
        Assert.DoesNotContain("T00:00:00", xml.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_AccountingSupplierParty_WrapsPartyElement()
    {
        // v2 mapped AccountingSupplierParty straight onto a party, omitting the mandatory
        // cac:Party child. UBL types it as SupplierParty, which contains a Party.
        var xml = XDocument.Parse(UblSerializer.Serialize(SampleDocuments.MinimalInvoice()));

        var supplier = xml.Root!.Element(Cac + "AccountingSupplierParty")!;
        var party = supplier.Element(Cac + "Party");

        Assert.NotNull(party);
        Assert.NotNull(party!.Element(Cac + "PartyName"));
        Assert.Null(supplier.Element(Cac + "PartyName"));
    }

    [Fact]
    public void Serialize_EmitsNoSchemaLocation()
    {
        // An xsi:schemaLocation on the root is a common cause of ANAF rejecting a valid document.
        var xml = UblSerializer.Serialize(SampleDocuments.MinimalInvoice());

        Assert.DoesNotContain("schemaLocation", xml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_EmitsNoByteOrderMark()
    {
        var xml = UblSerializer.Serialize(SampleDocuments.MinimalInvoice());

        Assert.False(xml.StartsWith('﻿'));
        Assert.StartsWith("<?xml", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_AmountsAreCultureInvariant()
    {
        // Guards against a decimal separator leaking in from the current culture.
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ro-RO");
        try
        {
            var xml = XDocument.Parse(UblSerializer.Serialize(SampleDocuments.MinimalInvoice()));
            var payable = xml.Root!
                .Element(Cac + "LegalMonetaryTotal")!
                .Element(Cbc + "PayableAmount")!;

            Assert.Equal("238.00", payable.Value);
            Assert.Equal("RON", payable.Attribute("currencyID")!.Value);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Serialize_CustomizationId_IsTheCiusRoValueRuleBrRo001Requires()
    {
        var xml = XDocument.Parse(UblSerializer.Serialize(SampleDocuments.MinimalInvoice()));

        Assert.Equal(
            "urn:cen.eu:en16931:2017#compliant#urn:efactura.mfinante.ro:CIUS-RO:1.0.1",
            xml.Root!.Element(Cbc + "CustomizationID")!.Value);
    }

    [Fact]
    public void Serialize_CreditNote_UsesCreditedQuantityAndItsOwnNamespace()
    {
        var xml = XDocument.Parse(UblSerializer.Serialize(SampleDocuments.MinimalCreditNote()));

        Assert.Equal(UblNamespaces.CreditNote, xml.Root!.Name.NamespaceName);
        Assert.Equal("CreditNote", xml.Root.Name.LocalName);

        var line = xml.Root.Element(Cac + "CreditNoteLine")!;
        Assert.NotNull(line.Element(Cbc + "CreditedQuantity"));
        Assert.Null(line.Element(Cbc + "InvoicedQuantity"));
    }

    [Fact]
    public void RoundTrip_Invoice_PreservesValues()
    {
        var original = SampleDocuments.MinimalInvoice();

        var restored = UblSerializer.DeserializeInvoice(UblSerializer.Serialize(original));

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.IssueDate, restored.IssueDate);
        Assert.Equal(original.DueDate, restored.DueDate);
        Assert.Equal(
            original.LegalMonetaryTotal.PayableAmount.Value,
            restored.LegalMonetaryTotal.PayableAmount.Value);
        Assert.Equal(
            original.AccountingSupplierParty.Party.PartyName!.Name,
            restored.AccountingSupplierParty.Party.PartyName!.Name);
    }

    [Theory]
    [InlineData("Invoice")]
    [InlineData("CreditNote")]
    public void ReadDocumentType_IdentifiesTheDocument(string expected)
    {
        var xml = expected == "Invoice"
            ? UblSerializer.Serialize(SampleDocuments.MinimalInvoice())
            : UblSerializer.Serialize(SampleDocuments.MinimalCreditNote());

        Assert.Equal(expected, UblSerializer.ReadDocumentType(xml));
    }

    [Fact]
    public void ReadDocumentType_ReturnsNullForNonXml()
    {
        // A downloaded archive can contain a JSON error body where a document was expected.
        Assert.Null(UblSerializer.ReadDocumentType("{\"eroare\":\"nu aveti drept\"}"));
    }
}
