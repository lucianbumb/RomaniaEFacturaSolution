using RomaniaEFactura.Ubl;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Tests;

/// <summary>
/// The VAT categories beyond standard rate, and the rules that only apply to them.
/// </summary>
/// <remarks>
/// Reverse charge (AE) and exempt (E) are routine on Romanian invoices, so these paths matter as
/// much as the standard-rate one.
/// </remarks>
public class VatCategoryRuleTests
{
    [Fact]
    public void ReverseChargeInvoice_IsValid()
    {
        var report = CiusRoValidator.Validate(SampleDocuments.ReverseChargeInvoice());

        Assert.True(report.IsValid, report.ToString());
    }

    [Fact]
    public void ReverseCharge_WithoutExemptionReason_TripsBrAe10()
    {
        var invoice = SampleDocuments.ReverseChargeInvoice();
        invoice.TaxTotals[0].TaxSubtotals[0].TaxCategory.TaxExemptionReason = null;

        var report = CiusRoValidator.Validate(invoice);

        Assert.Contains("BR-AE-10", report.ErrorCodes);
    }

    [Fact]
    public void ReverseCharge_WithExemptionReasonCodeInsteadOfText_IsAccepted()
    {
        var invoice = SampleDocuments.ReverseChargeInvoice();
        invoice.TaxTotals[0].TaxSubtotals[0].TaxCategory.TaxExemptionReason = null;
        invoice.TaxTotals[0].TaxSubtotals[0].TaxCategory.TaxExemptionReasonCode = "VATEX-EU-AE";

        var report = CiusRoValidator.Validate(invoice);

        Assert.DoesNotContain("BR-AE-10", report.ErrorCodes);
    }

    [Fact]
    public void ReverseCharge_WithoutBuyerVatIdentifier_TripsBrAe03()
    {
        // Under reverse charge the liability moves to the buyer, so the buyer must be VAT-identified.
        var invoice = SampleDocuments.ReverseChargeInvoice();
        invoice.AccountingCustomerParty.Party.PartyTaxSchemes.Clear();

        var report = CiusRoValidator.Validate(invoice);

        Assert.Contains("BR-AE-03", report.ErrorCodes);
    }

    [Fact]
    public void ReverseCharge_WithNonZeroRate_IsReported()
    {
        var invoice = SampleDocuments.ReverseChargeInvoice();
        invoice.TaxTotals[0].TaxSubtotals[0].TaxCategory.Percent = 19m;

        var report = CiusRoValidator.Validate(invoice);

        Assert.Contains("BR-AE-05", report.ErrorCodes);
    }

    [Fact]
    public void StandardRate_WithZeroRate_IsReported()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.TaxTotals[0].TaxSubtotals[0].TaxCategory.Percent = 0m;
        invoice.TaxTotals[0].TaxSubtotals[0].TaxAmount = new Amount(0.00m);

        var report = CiusRoValidator.Validate(invoice);

        Assert.Contains("BR-S-05", report.ErrorCodes);
    }

    [Fact]
    public void UnknownVatCategory_IsReported()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.TaxTotals[0].TaxSubtotals[0].TaxCategory.Id = new Identifier("X");

        var report = CiusRoValidator.Validate(invoice);

        Assert.Contains("BR-CO-17", report.ErrorCodes);
    }

    [Fact]
    public void VatTotalNotMatchingBreakdown_TripsBrCo14()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.TaxTotals[0].TaxAmount = new Amount(50.00m);

        var report = CiusRoValidator.Validate(invoice);

        Assert.Contains("BR-CO-14", report.ErrorCodes);
    }

    [Fact]
    public void VatIdentifierWithoutCountryPrefix_TripsBrCo09()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.AccountingSupplierParty.Party.PartyTaxSchemes[0].CompanyId =
            new Identifier(SampleDocuments.SellerCif);

        var report = CiusRoValidator.Validate(invoice);

        Assert.Contains("BR-CO-09", report.ErrorCodes);
    }

    [Fact]
    public void InvoicePeriodEndingBeforeItStarts_TripsBr29()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.InvoicePeriod = new Period
        {
            StartDate = new DateTime(2026, 8, 31),
            EndDate = new DateTime(2026, 8, 1),
        };

        var report = CiusRoValidator.Validate(invoice);

        Assert.Contains("BR-29", report.ErrorCodes);
    }

    [Fact]
    public void EmptyInvoicePeriod_TripsBrCo19()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.InvoicePeriod = new Period();

        var report = CiusRoValidator.Validate(invoice);

        Assert.Contains("BR-CO-19", report.ErrorCodes);
    }

    [Fact]
    public void LineTaxCategory_CannotCarryAnExemptionReason()
    {
        // UBL-CR-601 forbids an exemption reason on a line's ClassifiedTaxCategory; ANAF rejects
        // the document outright even though the Schematron marks the rule a warning. The reason is
        // absent from LineTaxCategory entirely, so this cannot be expressed rather than merely
        // being detected. This test documents that intent — it is a guard, and cannot go red
        // without the type changing.
        var members = typeof(LineTaxCategory).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain("TaxExemptionReason", members);
        Assert.DoesNotContain("TaxExemptionReasonCode", members);
    }
}
