using RomaniaEFactura.EditModels;
using RomaniaEFactura.Ubl;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Tests.EditModels;

/// <summary>
/// The mapping from a flat edit model to the UBL shape ANAF expects.
/// </summary>
/// <remarks>
/// Each of these asserts a decision the mapper makes on the caller's behalf — where an identifier
/// lands, which prefix survives, which element must be absent. They are the places where "obvious"
/// and "correct" differ, and where a rejection would otherwise be traced back from ANAF's error
/// text rather than from a test.
/// </remarks>
public class EditModelMappingTests
{
    [Fact]
    public void TheFiscalCodeLosesItsRomanianPrefixButTheVatNumberKeepsIt()
    {
        // BT-30 is a national identifier and ANAF's API refuses the prefixed form; BT-31 is an
        // international one and is wrong without the prefix. Same digits, opposite treatment.
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Seller.TaxId = "RO" + SampleEditModels.SellerCif;

        var ubl = invoice.ToUbl();
        var seller = ubl.AccountingSupplierParty.Party;

        Assert.Equal(SampleEditModels.SellerCif, seller.PartyLegalEntity!.CompanyId!.Value);
        Assert.Equal("RO" + SampleEditModels.SellerCif, seller.PartyTaxSchemes[0].CompanyId.Value);
    }

    [Fact]
    public void APartyWithNoVatNumberGetsNoTaxScheme()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Buyer.VatNumber = null;

        var buyer = invoice.ToUbl().AccountingCustomerParty.Party;

        Assert.Empty(buyer.PartyTaxSchemes);
    }

    [Fact]
    public void TheCountyCodeBecomesTheCountrySubentityForARomanianAddress()
    {
        var address = SampleEditModels.MinimalInvoice().ToUbl().AccountingSupplierParty.Party.PostalAddress;

        Assert.Equal("RO-B", address!.CountrySubentity);
        Assert.Equal("RO", address.Country!.IdentificationCode);
    }

    [Fact]
    public void TheFreeTextRegionIsUsedForAForeignAddress()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Buyer.Address = new AddressEditModel
        {
            Street = "Hauptstrasse 5",
            City = "Berlin",
            Region = "Berlin",
            CountryCode = "DE",
        };

        var address = invoice.ToUbl().AccountingCustomerParty.Party.PostalAddress;

        Assert.Equal("Berlin", address!.CountrySubentity);
        Assert.Equal("DE", address.Country!.IdentificationCode);
    }

    [Fact]
    public void ALineDiscountBecomesAnAllowanceWithNoTaxCategory()
    {
        // UBL-CR-599: a line-level allowance must not carry one, because the line's own category
        // already settles how it is taxed. Setting it is a natural thing to try, and is rejected.
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Lines[0].DiscountAmount = 25.00m;
        invoice.Lines[0].DiscountReason = "Reducere";

        var adjustment = Assert.Single(invoice.ToUbl().InvoiceLines[0].AllowanceCharges);

        Assert.False(adjustment.ChargeIndicator);
        Assert.Equal(25.00m, adjustment.Amount.Value);
        Assert.Null(adjustment.TaxCategory);
    }

    [Fact]
    public void ADocumentAdjustmentDoesCarryATaxCategory()
    {
        // The opposite of the line case: required here, forbidden there.
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.AllowancesAndCharges.Add(new DocumentAllowanceChargeEditModel
        {
            IsCharge = true,
            Amount = 35.00m,
            Reason = "Transport",
            VatRate = 19m,
        });

        var adjustment = Assert.Single(invoice.ToUbl().AllowanceCharges);

        Assert.True(adjustment.ChargeIndicator);
        Assert.Equal("S", adjustment.TaxCategory!.Id.Value);
        Assert.Equal(19m, adjustment.TaxCategory.Percent);
    }

    [Fact]
    public void AnExemptionReasonGoesToTheBreakdownAndNotToTheLine()
    {
        // UBL-CR-601 rejects it on the line outright, despite the Schematron calling it a warning.
        // The line type cannot express it at all, so this asserts it reaches the right place.
        var ubl = SampleEditModels.ReverseChargeInvoice().ToUbl();

        var subtotal = Assert.Single(ubl.TaxTotals[0].TaxSubtotals);
        Assert.Equal("AE", subtotal.TaxCategory.Id.Value);
        Assert.Equal("Taxare inversa conform art. 331 Cod Fiscal", subtotal.TaxCategory.TaxExemptionReason);
        Assert.Equal("AE", ubl.InvoiceLines[0].Item.ClassifiedTaxCategory.Id.Value);
    }

    [Fact]
    public void LinesAreNumberedByPositionWhenTheCallerDoesNotNumberThem()
    {
        // BR-21 requires an identifier, and an empty one fails it.
        var ubl = SampleEditModels.FullInvoice().ToUbl();

        Assert.Equal(["1", "2", "3"], ubl.InvoiceLines.Select(line => line.Id.Value));
    }

    [Fact]
    public void ACallerWhoNumbersTheLinesKeepsTheirNumbering()
    {
        var invoice = SampleEditModels.FullInvoice();
        invoice.Lines[0].Id = "10";
        invoice.Lines[1].Id = "20";
        invoice.Lines[2].Id = "30";

        var ubl = invoice.ToUbl();

        Assert.Equal(["10", "20", "30"], ubl.InvoiceLines.Select(line => line.Id.Value));
    }

    [Fact]
    public void TotalsThatAreZeroAreOmittedRatherThanWrittenAsZero()
    {
        var totals = SampleEditModels.MinimalInvoice().ToUbl().LegalMonetaryTotal;

        Assert.Null(totals.AllowanceTotalAmount);
        Assert.Null(totals.ChargeTotalAmount);
        Assert.Null(totals.PrepaidAmount);
    }

    [Fact]
    public void EveryAmountCarriesTheDocumentCurrency()
    {
        var invoice = SampleEditModels.FullInvoice();
        invoice.Currency = "EUR";

        var ubl = invoice.ToUbl();

        Assert.Equal("EUR", ubl.LegalMonetaryTotal.PayableAmount.CurrencyId);
        Assert.Equal("EUR", ubl.TaxTotals[0].TaxAmount.CurrencyId);
        Assert.Equal("EUR", ubl.InvoiceLines[0].LineExtensionAmount.CurrencyId);
        Assert.Equal("EUR", ubl.InvoiceLines[0].Price.PriceAmount.CurrencyId);
        Assert.Equal("EUR", ubl.AllowanceCharges[0].Amount.CurrencyId);
    }

    [Fact]
    public void TheIbanIsNormalisedBeforeItIsWritten()
    {
        var invoice = SampleEditModels.FullInvoice();
        invoice.Payment!.Iban = "ro49 aaaa 1b31 0075 9384 0000";

        var account = invoice.ToUbl().PaymentMeans[0].PayeeFinancialAccount;

        Assert.Equal("RO49AAAA1B31007593840000", account!.Id.Value);
    }

    [Fact]
    public void ACreditNoteReferencesTheInvoiceItCorrects()
    {
        var ubl = SampleEditModels.MinimalCreditNote().ToUbl();

        var reference = Assert.Single(ubl.BillingReferences);
        Assert.Equal("FCT-2026-001", reference.InvoiceDocumentReference.Id.Value);
        Assert.Equal(new DateTime(2026, 8, 31), reference.InvoiceDocumentReference.IssueDate);
    }

    [Fact]
    public void ACreditNoteMapsQuantitiesToCreditedQuantity()
    {
        // UBL names the element differently on a credit note; the edit model does not.
        var ubl = SampleEditModels.MinimalCreditNote().ToUbl();

        Assert.Equal(2m, Assert.Single(ubl.CreditNoteLines).CreditedQuantity.Value);
    }

    [Fact]
    public void TheMappedDocumentSatisfiesTheCiusRoEngine()
    {
        // The second stage of Verify, run directly: the model's own rules should leave nothing for
        // the rule engine to find.
        foreach (var invoice in new[]
                 {
                     SampleEditModels.MinimalInvoice(),
                     SampleEditModels.FullInvoice(),
                     SampleEditModels.ReverseChargeInvoice(),
                     SampleEditModels.MixedVatInvoice(),
                     SampleEditModels.PrepaidInvoice(),
                 })
        {
            var report = CiusRoValidator.Validate(invoice.ToUbl());
            Assert.True(report.IsValid, $"{invoice.Number}: {report}");
        }
    }

    [Fact]
    public void TheMappedDocumentSurvivesSerialization()
    {
        var xml = UblSerializer.Serialize(SampleEditModels.FullInvoice().ToUbl());

        // Two failures that only appear at this point: a byte-order mark ahead of the declaration,
        // and an xsi:schemaLocation on the root. Both make ANAF refuse the document.
        Assert.StartsWith("<?xml", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("schemaLocation", xml, StringComparison.Ordinal);
        Assert.True(CiusRoValidator.Validate(UblSerializer.DeserializeInvoice(xml)).IsValid);
    }
}
