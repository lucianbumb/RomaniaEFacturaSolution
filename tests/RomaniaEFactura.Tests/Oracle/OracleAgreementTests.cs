using RomaniaEFactura.Ubl;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Tests.Oracle;

/// <summary>
/// The milestone's acceptance criterion: our C# rule engine and ANAF's own validator must reach
/// the same verdict on every document in the corpus.
/// </summary>
/// <remarks>
/// Agreement is checked on the accept/reject verdict rather than on the exact set of rule codes.
/// The two engines legitimately differ in how many findings they report for one underlying defect
/// — ANAF stops at the first schema failure, and one broken total can trip several arithmetic
/// rules — but they must never disagree on whether ANAF will take the document. A disagreement in
/// either direction is a defect: a false accept ships an invoice ANAF will refuse, and a false
/// reject blocks a valid one.
/// </remarks>
public class OracleAgreementTests
{
    public static TheoryData<string> Corpus =>
    [
        "valid-invoice",
        "wrong-customization-id",
        "buyer-cif-bad-control-digit",
        "seller-cif-bad-control-digit",
        "buyer-not-identifiable",
        "missing-country-subentity",
        "line-total-does-not-match-lines",
        "tax-inclusive-does-not-match",
        "vat-amount-does-not-match-rate",
        "no-lines",
        "missing-item-name",
        "negative-unit-price",
        // VAT categories beyond standard rate; reverse charge is routine in Romania.
        "reverse-charge-valid",
        "reverse-charge-without-exemption-reason",
        "exempt-valid",
        "exempt-without-exemption-reason",
        "vat-total-does-not-match-breakdown",
        "vat-id-without-country-prefix",
        "period-end-before-start",
        "unknown-vat-category",
        // The CIUS-RO caps. Each is a rule the library enforces and had not proved ANAF agrees on
        // — and each names a limit the first version of the edit models got wrong.
        "item-name-too-long",
        "item-description-too-long",
        "seller-city-too-long",
        "buyer-city-too-long",
        "payment-terms-too-long",
        "note-too-long",
        "too-many-notes",
        "line-note-too-long",
        "document-number-without-a-digit",
        // The other direction: values at or near a cap that ANAF must still accept, so the
        // library's limits cannot quietly become stricter than the specification.
        "long-but-legal-document-number",
        "item-name-at-the-limit",
        "note-at-the-limit",
    ];

    [RequiresAnafValidatorTheory]
    [MemberData(nameof(Corpus))]
    public void OurVerdictMatchesAnaf(string scenario)
    {
        var invoice = Mutate(SampleDocuments.MinimalInvoice(), scenario);
        var xml = UblSerializer.Serialize(invoice);

        var ours = CiusRoValidator.Validate(invoice);
        var theirs = AnafValidator.Validate(xml, "FACT1");

        Assert.True(
            ours.IsValid == theirs.IsValid,
            $"""
             Verdicts disagree for '{scenario}'.
               ours   : {ours}
               ANAF   : {theirs}
             """);
    }

    [RequiresAnafValidatorFact]
    public void ValidCreditNote_AgreesWithAnaf()
    {
        var creditNote = SampleDocuments.MinimalCreditNote();

        var ours = CiusRoValidator.Validate(creditNote);
        var theirs = AnafValidator.Validate(UblSerializer.Serialize(creditNote), "FCN");

        Assert.True(
            ours.IsValid == theirs.IsValid,
            $"Verdicts disagree for the credit note.{Environment.NewLine}  ours: {ours}{Environment.NewLine}  ANAF: {theirs}");
    }

    private static UblInvoice Mutate(UblInvoice invoice, string scenario)
    {
        switch (scenario)
        {
            case "valid-invoice":
                break;

            case "wrong-customization-id":
                invoice.CustomizationId =
                    "urn:cen.eu:en16931:2017#compliant#urn:efactura.mfinante.ro:CIUS-RO:1.0.0";
                break;

            case "buyer-cif-bad-control-digit":
                SetCif(invoice.AccountingCustomerParty.Party, "RO23456784");
                break;

            case "seller-cif-bad-control-digit":
                SetCif(invoice.AccountingSupplierParty.Party, "RO12345675");
                break;

            case "buyer-not-identifiable":
                invoice.AccountingCustomerParty.Party.PartyLegalEntity!.CompanyId = null;
                invoice.AccountingCustomerParty.Party.PartyTaxSchemes.Clear();
                break;

            case "missing-country-subentity":
                invoice.AccountingSupplierParty.Party.PostalAddress!.CountrySubentity = null;
                break;

            case "line-total-does-not-match-lines":
                invoice.LegalMonetaryTotal.LineExtensionAmount = new Amount(999.00m);
                break;

            case "tax-inclusive-does-not-match":
                invoice.LegalMonetaryTotal.TaxInclusiveAmount = new Amount(500.00m);
                break;

            case "vat-amount-does-not-match-rate":
                invoice.TaxTotals[0].TaxSubtotals[0].TaxAmount = new Amount(10.00m);
                break;

            case "no-lines":
                invoice.InvoiceLines.Clear();
                break;

            case "missing-item-name":
                invoice.InvoiceLines[0].Item.Name = string.Empty;
                break;

            case "negative-unit-price":
                invoice.InvoiceLines[0].Price.PriceAmount = new Amount(-100.00m);
                break;

            case "item-name-too-long":
                invoice.InvoiceLines[0].Item.Name = new string('x', CiusRoLengths.ItemName + 1);
                break;

            case "item-description-too-long":
                invoice.InvoiceLines[0].Item.Description =
                    new string('x', CiusRoLengths.ItemDescription + 1);
                break;

            case "seller-city-too-long":
                invoice.AccountingSupplierParty.Party.PostalAddress!.CountrySubentity = "RO-CJ";
                invoice.AccountingSupplierParty.Party.PostalAddress.CityName =
                    new string('c', CiusRoLengths.City + 1);
                break;

            case "buyer-city-too-long":
                invoice.AccountingCustomerParty.Party.PostalAddress!.CityName =
                    new string('c', CiusRoLengths.City + 1);
                break;

            case "payment-terms-too-long":
                invoice.PaymentTerms = new PaymentTerms
                {
                    Note = new string('t', CiusRoLengths.PaymentTerms + 1),
                };
                break;

            case "note-too-long":
                invoice.Notes = [new string('n', CiusRoLengths.Note + 1)];
                break;

            case "too-many-notes":
                invoice.Notes =
                    [.. Enumerable.Range(0, CiusRoLengths.MaxNotes + 1).Select(i => $"note {i}")];
                break;

            case "line-note-too-long":
                invoice.InvoiceLines[0].Note = new string('n', CiusRoLengths.LineNote + 1);
                break;

            case "document-number-without-a-digit":
                invoice.Id = "FACTURA-SERIE-A";
                break;

            case "long-but-legal-document-number":
                invoice.Id = "FCT-2026-" + new string('N', CiusRoLengths.DocumentNumber - 9);
                break;

            case "item-name-at-the-limit":
                invoice.InvoiceLines[0].Item.Name = new string('x', CiusRoLengths.ItemName);
                break;

            case "note-at-the-limit":
                invoice.Notes = [new string('n', CiusRoLengths.Note)];
                break;

            case "reverse-charge-valid":
                return SampleDocuments.ReverseChargeInvoice();

            case "reverse-charge-without-exemption-reason":
            {
                var ae = SampleDocuments.ReverseChargeInvoice();
                ae.TaxTotals[0].TaxSubtotals[0].TaxCategory.TaxExemptionReason = null;
                return ae;
            }

            case "exempt-valid":
            {
                var e = SampleDocuments.ReverseChargeInvoice();
                SetCategory(e, "E", "Scutit conform art. 292");
                return e;
            }

            case "exempt-without-exemption-reason":
            {
                var e = SampleDocuments.ReverseChargeInvoice();
                SetCategory(e, "E", null);
                return e;
            }

            case "vat-total-does-not-match-breakdown":
                // BT-110 no longer equals the sum of BT-117.
                invoice.TaxTotals[0].TaxAmount = new Amount(50.00m);
                break;

            case "vat-id-without-country-prefix":
                invoice.AccountingSupplierParty.Party.PartyTaxSchemes[0].CompanyId =
                    new Identifier(SampleDocuments.SellerCif);
                break;

            case "period-end-before-start":
                invoice.InvoicePeriod = new Period
                {
                    StartDate = new DateTime(2026, 8, 31),
                    EndDate = new DateTime(2026, 8, 1),
                };
                break;

            case "unknown-vat-category":
                invoice.TaxTotals[0].TaxSubtotals[0].TaxCategory.Id = new Identifier("X");
                invoice.InvoiceLines[0].Item.ClassifiedTaxCategory.Id = new Identifier("X");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown corpus scenario.");
        }

        return invoice;
    }

    private static void SetCategory(UblInvoice invoice, string code, string? exemptionReason)
    {
        foreach (var category in invoice.TaxTotals.SelectMany(t => t.TaxSubtotals).Select(st => st.TaxCategory))
        {
            category.Id = new Identifier(code);
            category.TaxExemptionReason = exemptionReason;
        }

        foreach (var line in invoice.InvoiceLines)
        {
            line.Item.ClassifiedTaxCategory.Id = new Identifier(code);
        }
    }

    private static void SetCif(Party party, string cif)
    {
        party.PartyLegalEntity!.CompanyId = new Identifier(cif);
        foreach (var scheme in party.PartyTaxSchemes)
        {
            scheme.CompanyId = new Identifier(cif);
        }
    }
}
