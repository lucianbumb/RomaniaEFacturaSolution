using RomaniaEFactura.Ubl;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Tests;

/// <summary>
/// Rule-level tests that pin the exact code each defect reports.
/// </summary>
/// <remarks>
/// The oracle comparison in <c>OracleAgreementTests</c> proves the accept/reject verdict matches
/// ANAF's. These tests pin which rule fires, which the oracle deliberately does not assert — the
/// codes are what a user interface shows, so a silent change to them would be a regression even
/// though the verdict stayed correct.
/// </remarks>
public class CiusRoValidatorTests
{
    [Fact]
    public void MinimalInvoice_IsValid()
    {
        var report = CiusRoValidator.Validate(SampleDocuments.MinimalInvoice());

        Assert.True(report.IsValid, report.ToString());
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void MinimalCreditNote_IsValid()
    {
        var report = CiusRoValidator.Validate(SampleDocuments.MinimalCreditNote());

        Assert.True(report.IsValid, report.ToString());
    }

    [Fact]
    public void CreditNote_WithoutDueDateOrTerms_DoesNotTripBrCo25()
    {
        // ANAF applies BR-CO-25 to invoices only; applying it to credit notes was a false reject.
        var report = CiusRoValidator.Validate(SampleDocuments.MinimalCreditNote());

        Assert.DoesNotContain("BR-CO-25", report.ErrorCodes);
    }

    [Fact]
    public void Invoice_WithoutDueDateOrTerms_TripsBrCo25()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.DueDate = null;

        var report = CiusRoValidator.Validate(invoice);

        Assert.Contains("BR-CO-25", report.ErrorCodes);
    }

    [Fact]
    public void Invoice_WithPaymentTermsInsteadOfDueDate_SatisfiesBrCo25()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.DueDate = null;
        invoice.PaymentTerms = new PaymentTerms { Note = "Plata in 30 de zile" };

        var report = CiusRoValidator.Validate(invoice);

        Assert.DoesNotContain("BR-CO-25", report.ErrorCodes);
    }

    [Fact]
    public void WrongCustomizationId_TripsBrRo001()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.CustomizationId = "urn:cen.eu:en16931:2017#compliant#urn:efactura.mfinante.ro:CIUS-RO:1.0.0";

        var report = CiusRoValidator.Validate(invoice);

        Assert.Contains("BR-RO-001", report.ErrorCodes);
    }

    [Fact]
    public void LineTotalNotMatchingLines_TripsBrCo10()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.LegalMonetaryTotal.LineExtensionAmount = new Amount(999.00m);

        var report = CiusRoValidator.Validate(invoice);

        Assert.Contains("BR-CO-10", report.ErrorCodes);
    }

    [Fact]
    public void VatNotMatchingRate_TripsBrS09()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.TaxTotals[0].TaxSubtotals[0].TaxAmount = new Amount(10.00m);

        var report = CiusRoValidator.Validate(invoice);

        Assert.Contains("BR-S-09", report.ErrorCodes);
    }

    [Fact]
    public void BuyerWithBadControlDigit_TripsRoCifInvalid()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.AccountingCustomerParty.Party.PartyLegalEntity!.CompanyId = new Identifier("RO23456784");
        invoice.AccountingCustomerParty.Party.PartyTaxSchemes[0].CompanyId = new Identifier("RO23456784");

        var report = CiusRoValidator.Validate(invoice);

        Assert.Contains("RO-CIF-INVALID", report.ErrorCodes);
    }

    [Fact]
    public void UnidentifiableBuyer_TripsRoCifMissing()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.AccountingCustomerParty.Party.PartyLegalEntity!.CompanyId = null;
        invoice.AccountingCustomerParty.Party.PartyTaxSchemes.Clear();

        var report = CiusRoValidator.Validate(invoice);

        Assert.Contains("RO-CIF-MISSING", report.ErrorCodes);
    }

    [Fact]
    public void ForeignVatNumber_IsNotCheckedAgainstTheRomanianAlgorithm()
    {
        // A Swedish seller's VAT id must not be run through the Romanian control-digit check.
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.AccountingSupplierParty.Party.PostalAddress!.Country!.IdentificationCode = "SE";
        invoice.AccountingSupplierParty.Party.PostalAddress.CountrySubentity = null;
        invoice.AccountingSupplierParty.Party.PartyLegalEntity!.CompanyId = new Identifier("SE123451234501");
        invoice.AccountingSupplierParty.Party.PartyTaxSchemes[0].CompanyId = new Identifier("SE123451234501");

        var report = CiusRoValidator.Validate(invoice);

        Assert.DoesNotContain("RO-CIF-INVALID", report.ErrorCodes);
    }

    [Fact]
    public void RomanianAddressWithoutCountySubdivision_IsReported()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.AccountingSupplierParty.Party.PostalAddress!.CountrySubentity = null;

        var report = CiusRoValidator.Validate(invoice);

        // BR-RO-110, not BR-RO-090: the latter is what ANAF's message text prints but is not the
        // id of any rule, so a finding carrying it could not be looked up.
        Assert.Contains("BR-RO-110", report.ErrorCodes);
    }

    [Fact]
    public void NoLines_TripsBr16()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.InvoiceLines.Clear();

        var report = CiusRoValidator.Validate(invoice);

        Assert.Contains("BR-16", report.ErrorCodes);
    }

    [Fact]
    public void NegativeUnitPrice_TripsBr27()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.InvoiceLines[0].Price.PriceAmount = new Amount(-100.00m);

        var report = CiusRoValidator.Validate(invoice);

        Assert.Contains("BR-27", report.ErrorCodes);
    }

    [Fact]
    public void AmountWithMoreThanTwoDecimals_IsReported()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.LegalMonetaryTotal.PayableAmount = new Amount(238.005m);

        var report = CiusRoValidator.Validate(invoice);

        Assert.Contains("BR-DEC-18", report.ErrorCodes);
    }

    [Fact]
    public void UnitPriceWithFourDecimals_IsAccepted()
    {
        // BR-DEC constrains document and line amounts, not the unit price; ANAF's own example
        // carries four decimal places on BT-146.
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.InvoiceLines[0].Price.PriceAmount = new Amount(7.6453m);
        invoice.InvoiceLines[0].InvoicedQuantity = new Quantity(26.1595m);

        var report = CiusRoValidator.Validate(invoice);

        Assert.DoesNotContain(report.ErrorCodes, c => c.StartsWith("BR-DEC", StringComparison.Ordinal));
    }
}
