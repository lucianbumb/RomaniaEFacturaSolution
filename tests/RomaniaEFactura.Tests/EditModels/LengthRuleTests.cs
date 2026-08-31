using RomaniaEFactura.EditModels;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Tests.EditModels;

/// <summary>
/// The CIUS-RO length caps, on both the model path and the UBL path.
/// </summary>
/// <remarks>
/// <para>
/// These matter in two directions, and the first version of the edit models got both wrong. A cap
/// that is too generous lets through a document ANAF then refuses, which breaks the library's
/// central promise. A cap that is too strict refuses a document that is perfectly legal — an
/// invoice number may be 200 characters, and was being rejected at 51.
/// </para>
/// <para>
/// The numbers themselves are checked against ANAF's own Schematron by
/// <c>CiusRoLengthTableTests</c>. What is checked here is that they are actually applied.
/// </para>
/// </remarks>
public class LengthRuleTests
{
    [Fact]
    public void AnOverlongItemNameIsRejected()
    {
        // The most consequential of the corrections: the model used to allow 200 where CIUS-RO
        // allows 100, so a 150-character item name passed Verify and ANAF refused the document.
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Lines[0].Name = new string('x', CiusRoLengths.ItemName + 1);

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "Lines[0].Name");
    }

    [Fact]
    public void AnItemNameAtTheLimitIsAccepted()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Lines[0].Name = new string('x', CiusRoLengths.ItemName);

        Assert.True(EditModelValidator.Validate(invoice).IsValid);
    }

    [Fact]
    public void ALongInvoiceNumberIsAccepted()
    {
        // The correction in the other direction. CIUS-RO allows 200; the model allowed 50, so a
        // perfectly legal series was refused by the library and never reached ANAF.
        var invoice = SampleEditModels.MinimalInvoice();
        // Carries a digit, because BR-RO-010 requires one independently of the length.
        invoice.Number = "FCT-2026-" + new string('N', 110);

        Assert.True(EditModelValidator.Validate(invoice).IsValid);
    }

    [Theory]
    [InlineData("Seller.Address.City")]
    [InlineData("Buyer.Address.City")]
    public void AnOverlongCityIsRejected(string path)
    {
        var invoice = SampleEditModels.MinimalInvoice();
        var party = path.StartsWith("Seller", StringComparison.Ordinal) ? invoice.Seller : invoice.Buyer;
        party.Address.County = "RO-CJ";                       // not Bucharest, so any city name is legal
        party.Address.City = new string('c', CiusRoLengths.City + 1);

        var report = EditModelValidator.Validate(invoice);

        Assert.Contains(report.Errors, f => f.Path == path);
    }

    [Fact]
    public void AnOverlongPaymentTermIsRejected()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.PaymentTerms = new string('t', CiusRoLengths.PaymentTerms + 1);

        Assert.Contains(EditModelValidator.Validate(invoice).Errors, f => f.Path == "PaymentTerms");
    }

    [Fact]
    public void AnOverlongVatExemptionReasonIsRejected()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Lines[0].VatCategory = VatCategory.Exempt;
        invoice.Lines[0].VatRate = null;
        invoice.Lines[0].VatExemptionReason = new string('r', CiusRoLengths.VatExemptionReason + 1);

        Assert.Contains(
            EditModelValidator.Validate(invoice).Errors,
            f => f.Path == "Lines[0].VatExemptionReason");
    }

    [Fact]
    public void AnOverlongNoteIsRejectedEvenThoughNoAttributeCanReachIt()
    {
        // Notes are a plain List<string>, so DataAnnotations cannot see inside them. Without the
        // explicit check they would be the one capped field with no enforcement at all.
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Notes = ["fine", new string('n', CiusRoLengths.Note + 1)];

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Message.Contains("Note 2", StringComparison.Ordinal));
    }

    [Fact]
    public void TooManyNotesAreRejected()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Notes = [.. Enumerable.Range(0, CiusRoLengths.MaxNotes + 1).Select(i => $"note {i}")];

        Assert.Contains("BR-RO-A020", EditModelValidator.Validate(invoice).ErrorCodes);
    }

    // ------------------------------------------------------------- the UBL path

    [Fact]
    public void TheUblPathEnforcesTheSameCaps()
    {
        // The gap this whole issue exists to close: a document built directly as UBL bypassed
        // every one of these, so Verify(UblInvoice) promised more than it delivered.
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.InvoiceLines[0].Item.Name = new string('x', CiusRoLengths.ItemName + 1);

        var report = CiusRoValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains("BR-RO-L1024", report.ErrorCodes);
    }

    [Fact]
    public void TheUblPathCapsTheSellerCity()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.AccountingSupplierParty.Party.PostalAddress!.CountrySubentity = "RO-CJ";
        invoice.AccountingSupplierParty.Party.PostalAddress.CityName =
            new string('c', CiusRoLengths.City + 1);

        Assert.Contains("BR-RO-L0501", CiusRoValidator.Validate(invoice).ErrorCodes);
    }

    [Fact]
    public void TheUblPathCapsTheBuyerCitySeparately()
    {
        // Seller and buyer are separate rules. A single shared check would report the seller's id
        // for the buyer's field, which is worse than useless when searching the specification.
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.AccountingCustomerParty.Party.PostalAddress!.CityName =
            new string('c', CiusRoLengths.City + 1);

        Assert.Contains("BR-RO-L0502", CiusRoValidator.Validate(invoice).ErrorCodes);
    }

    [Fact]
    public void TheUblPathCapsTheNotes()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.Notes = [new string('n', CiusRoLengths.Note + 1)];

        Assert.Contains("BR-RO-L302", CiusRoValidator.Validate(invoice).ErrorCodes);
    }

    [Fact]
    public void AValidDocumentIsUnaffected()
    {
        // A guard against the table being applied too eagerly: every sample must still pass.
        Assert.True(CiusRoValidator.Validate(SampleDocuments.MinimalInvoice()).IsValid);
        Assert.True(CiusRoValidator.Validate(SampleDocuments.MinimalCreditNote()).IsValid);
        Assert.True(EditModelValidator.Validate(SampleEditModels.FullInvoice()).IsValid);
    }
}
