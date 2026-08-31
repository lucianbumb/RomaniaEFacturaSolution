using RomaniaEFactura.EditModels;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Tests.EditModels;

/// <summary>
/// What the edit model refuses, and how clearly it says so.
/// </summary>
/// <remarks>
/// The point of these rules is that a caller learns about a problem while the form is on screen
/// rather than hours later from ANAF, so each test asserts the path as well as the verdict: a
/// finding a form cannot attach to a field is only half useful.
/// </remarks>
public class EditModelValidationTests
{
    [Fact]
    public void AFilledInInvoiceIsValid()
    {
        var report = EditModelValidator.Validate(SampleEditModels.MinimalInvoice());

        Assert.True(report.IsValid, report.ToString());
    }

    [Fact]
    public void AFullyPopulatedInvoiceIsValid()
    {
        var report = EditModelValidator.Validate(SampleEditModels.FullInvoice());

        Assert.True(report.IsValid, report.ToString());
    }

    [Fact]
    public void AFilledInCreditNoteIsValid()
    {
        var report = EditModelValidator.Validate(SampleEditModels.MinimalCreditNote());

        Assert.True(report.IsValid, report.ToString());
    }

    // ------------------------------------------------------ rules on nested models

    [Fact]
    public void TheFrameworkValidatorAloneWouldMissEveryRuleOnEveryLine()
    {
        // A regression guard rather than a rule: it cannot go red against unfixed code, because
        // it asserts what the framework does, not what we do. It is here because the recursion in
        // EditModelValidator looks like redundant machinery until you see that removing it makes
        // an invoice with a nameless line pass silently.
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Lines[0].Name = string.Empty;

        var frameworkResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var frameworkVerdict = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            invoice,
            new System.ComponentModel.DataAnnotations.ValidationContext(invoice),
            frameworkResults,
            validateAllProperties: true);

        Assert.True(frameworkVerdict, "The framework validator has started recursing; the note above is stale.");
        Assert.Empty(frameworkResults);

        // Ours does not miss it.
        Assert.False(EditModelValidator.Validate(invoice).IsValid);
    }

    [Fact]
    public void ARuleBrokenOnALineIsReportedAgainstThatLine()
    {
        // The case a non-recursive validator misses entirely: Validator.TryValidateObject on the
        // invoice checks the invoice's own properties and never looks inside Lines.
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Lines[0].Name = string.Empty;

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "Lines[0].Name");
    }

    [Fact]
    public void ARuleBrokenTwoLevelsDownIsStillReported()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Buyer.Address.City = string.Empty;

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "Buyer.Address.City");
    }

    [Fact]
    public void TheOffendingLineIsIdentifiedByPositionWhenThereAreSeveral()
    {
        var invoice = SampleEditModels.FullInvoice();
        invoice.Lines[2].UnitCode = string.Empty;

        var report = EditModelValidator.Validate(invoice);

        Assert.Contains(report.Errors, f => f.Path == "Lines[2].UnitCode");
    }

    // ------------------------------------------------------------- field-level

    [Fact]
    public void AFiscalCodeWithABadControlDigitIsRejected()
    {
        // 12345675 differs from the valid 12345674 only in the control digit — the exact mistake
        // the algorithm exists to catch, and one ANAF rejects on.
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Buyer.TaxId = "12345675";

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "Buyer.TaxId");
    }

    [Fact]
    public void AnIbanWithTransposedDigitsIsRejected()
    {
        var invoice = SampleEditModels.FullInvoice();
        invoice.Payment!.Iban = "RO49AAAA1B31007593840001";

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "Payment.Iban");
    }

    [Fact]
    public void ACountyNameIsRejectedWhereACodeIsRequired()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Seller.Address.County = "Bucuresti";

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "Seller.Address.County");
    }

    [Fact]
    public void ARomanianAddressWithoutACountyIsRejected()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Seller.Address.County = null;

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "Seller.Address.County");
    }

    [Fact]
    public void AForeignAddressNeedsNoCounty()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Buyer.TaxId = "DE123456789";
        invoice.Buyer.Address = new AddressEditModel
        {
            Street = "Hauptstrasse 5",
            City = "Berlin",
            CountryCode = "DE",
        };
        invoice.Lines[0].VatCategory = VatCategory.IntraCommunitySupply;
        invoice.Lines[0].VatRate = null;
        invoice.Lines[0].VatExemptionReason = "Livrare intracomunitara";
        invoice.Buyer.VatNumber = "DE123456789";
        invoice.DeliveryDate = new DateOnly(2026, 8, 30);
        invoice.DeliveryAddress = new AddressEditModel
        {
            Street = "Hauptstrasse 5",
            City = "Berlin",
            Region = "Berlin",
            CountryCode = "DE",
        };

        var report = EditModelValidator.Validate(invoice);

        Assert.True(report.IsValid, report.ToString());
    }

    // -------------------------------------------------------------- cross-field

    [Fact]
    public void AnInvoiceWithNoLinesIsRejected()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Lines.Clear();

        var report = EditModelValidator.Validate(invoice);

        Assert.Contains("BR-16", report.ErrorCodes);
    }

    [Fact]
    public void AnAmountDueWithNoDueDateAndNoTermsIsRejected()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.DueDate = null;

        var report = EditModelValidator.Validate(invoice);

        Assert.Contains("BR-CO-25", report.ErrorCodes);
    }

    [Fact]
    public void PaymentTermsSatisfyTheSameRuleAsADueDate()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.DueDate = null;
        invoice.PaymentTerms = "Plata in 30 de zile.";

        var report = EditModelValidator.Validate(invoice);

        Assert.True(report.IsValid, report.ToString());
    }

    [Fact]
    public void AnInvoiceWithNothingLeftToPayNeedsNeither()
    {
        var report = EditModelValidator.Validate(SampleEditModels.PrepaidInvoice());

        Assert.True(report.IsValid, report.ToString());
    }

    [Fact]
    public void ADueDateBeforeTheIssueDateIsRejected()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.DueDate = invoice.IssueDate.AddDays(-1);

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "DueDate");
    }

    [Fact]
    public void ReverseChargeWithoutASellerVatNumberIsRejected()
    {
        var invoice = SampleEditModels.ReverseChargeInvoice();
        invoice.Seller.VatNumber = null;

        var report = EditModelValidator.Validate(invoice);

        Assert.Contains("BR-AE-02", report.ErrorCodes);
    }

    [Fact]
    public void ReverseChargeWithoutABuyerVatNumberIsRejected()
    {
        var invoice = SampleEditModels.ReverseChargeInvoice();
        invoice.Buyer.VatNumber = null;

        var report = EditModelValidator.Validate(invoice);

        Assert.Contains("BR-AE-03", report.ErrorCodes);
    }

    [Fact]
    public void AnExemptLineWithNoReasonAnywhereIsRejected()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Lines[0].VatCategory = VatCategory.Exempt;
        invoice.Lines[0].VatRate = null;

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Message.Contains("no VAT is", StringComparison.Ordinal));
    }

    [Fact]
    public void ADocumentLevelReasonCoversALineThatGivesNone()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Lines[0].VatCategory = VatCategory.Exempt;
        invoice.Lines[0].VatRate = null;
        invoice.VatExemptionReason = "Scutit conform art. 292 Cod Fiscal";

        var report = EditModelValidator.Validate(invoice);

        Assert.True(report.IsValid, report.ToString());
    }

    [Fact]
    public void AStandardRateLineWithNoRateIsRejected()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Lines[0].VatRate = null;

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "Lines[0].VatRate");
    }

    [Fact]
    public void ADiscountLargerThanTheLineIsRejected()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Lines[0].DiscountAmount = 500.00m;
        invoice.Lines[0].DiscountReason = "Reducere";

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "Lines[0].DiscountAmount");
    }

    [Fact]
    public void ADiscountWithoutAReasonIsRejected()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Lines[0].DiscountAmount = 10.00m;

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "Lines[0].DiscountReason");
    }

    [Fact]
    public void ACreditTransferWithoutAnAccountIsRejected()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Payment = new PaymentEditModel { MeansCode = "31" };

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "Payment.Iban");
    }

    [Fact]
    public void APaymentInCashNeedsNoAccount()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Payment = new PaymentEditModel { MeansCode = "10" };

        var report = EditModelValidator.Validate(invoice);

        Assert.True(report.IsValid, report.ToString());
    }

    [Fact]
    public void RepeatedLineNumbersAreRejected()
    {
        var invoice = SampleEditModels.FullInvoice();
        invoice.Lines[0].Id = "1";
        invoice.Lines[1].Id = "1";

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "Lines");
    }

    [Fact]
    public void PayingMoreThanTheTotalIsRejected()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.AmountAlreadyPaid = 500.00m;

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "AmountAlreadyPaid");
    }

    [Fact]
    public void APeriodEndingBeforeItStartsIsRejected()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.PeriodStart = new DateOnly(2026, 8, 31);
        invoice.PeriodEnd = new DateOnly(2026, 8, 1);

        var report = EditModelValidator.Validate(invoice);

        Assert.Contains("BR-29", report.ErrorCodes);
    }

    // ----------------------------------------------------------- credit notes

    [Fact]
    public void ACreditNoteReferencingNothingIsRejected()
    {
        var creditNote = SampleEditModels.MinimalCreditNote();
        creditNote.PrecedingDocuments.Clear();

        var report = EditModelValidator.Validate(creditNote);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "PrecedingDocuments");
    }

    [Fact]
    public void ACreditNoteWithNegativeQuantitiesIsRejected()
    {
        // Entering negatives credits the wrong way round: the document is already a credit.
        var creditNote = SampleEditModels.MinimalCreditNote();
        creditNote.Lines[0].Quantity = -2m;

        var report = EditModelValidator.Validate(creditNote);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "Lines");
    }

    // ------------------------------------------------------------- reporting

    [Fact]
    public void AFindingCarriesTheRuleCodeItsMessageNames()
    {
        // So a caller can branch on BR-CO-25 whether it came from here or from the CIUS-RO engine.
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.DueDate = null;

        var finding = Assert.Single(
            EditModelValidator.Validate(invoice).Errors,
            f => f.Code == "BR-CO-25");

        Assert.Equal(ValidationSeverity.Error, finding.Severity);
    }

    [Fact]
    public void EveryProblemIsReportedAtOnceRatherThanOneAtATime()
    {
        // A form shows all its red fields together; stopping at the first would make filling one
        // in a matter of repeated round trips.
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Number = string.Empty;
        invoice.Buyer.Name = string.Empty;
        invoice.Lines[0].Name = string.Empty;

        var report = EditModelValidator.Validate(invoice);

        Assert.Equal(3, report.Errors.Count());
    }
}
