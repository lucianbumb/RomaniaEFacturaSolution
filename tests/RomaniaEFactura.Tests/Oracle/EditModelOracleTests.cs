using RomaniaEFactura.EditModels;
using RomaniaEFactura.Tests.EditModels;
using RomaniaEFactura.Ubl;

namespace RomaniaEFactura.Tests.Oracle;

/// <summary>
/// The milestone's real acceptance criterion, put to ANAF's own validator.
/// </summary>
/// <remarks>
/// <para>
/// The library's promise is that filling in an edit model correctly is enough: no UBL knowledge,
/// no totals arithmetic, no VAT breakdown. These tests are what makes that promise checkable
/// rather than merely stated. Each fills in a model the way an application would, maps it, and
/// hands the XML to <c>ROeFacturaValidator.jar</c> — the same tool ANAF publishes for suppliers to
/// check documents before sending them.
/// </para>
/// <para>
/// Our own <c>Verify</c> agreeing would prove much less: the mapper and the rule engine were
/// written together and share every assumption. The Java validator shares none of them.
/// </para>
/// </remarks>
public class EditModelOracleTests
{
    public static TheoryData<string> ValidScenarios =>
    [
        "minimal-invoice",
        "full-invoice",
        "reverse-charge",
        "mixed-vat",
        "prepaid",
        "line-discount",
        "document-discount-and-charge",
        "fractional-unit-price",
        "price-base-quantity",
    ];

    [RequiresAnafValidatorTheory]
    [MemberData(nameof(ValidScenarios))]
    public void AFilledInModelProducesADocumentAnafAccepts(string scenario)
    {
        var model = Build(scenario);

        // Whatever a form would have shown the user before the send button was enabled.
        var ours = EditModelValidator.Validate(model);
        Assert.True(ours.IsValid, $"Our own validation rejected '{scenario}': {ours}");

        var theirs = AnafValidator.Validate(UblSerializer.Serialize(model.ToUbl()), "FACT1");

        Assert.True(
            theirs.IsValid,
            $"""
             ANAF's validator rejected '{scenario}', which the model reported as valid.
             This is a broken promise, not a caller error.
               ANAF: {theirs}
             """);
    }

    [RequiresAnafValidatorFact]
    public void AForeignBuyerIsBeyondWhatTheOfflineValidatorCanCheck()
    {
        // ANAF's offline validator demands a Romanian buyer CUI unconditionally, so it refuses
        // every export and intra-community invoice — a case the live API handles through the
        // extern=DA upload parameter, which a local file cannot carry. The scenario is therefore
        // absent from the accept list above, and pinned here instead: if a later validator version
        // learns about foreign buyers, this test fails and the scenario can rejoin the corpus.
        // Whether the live service accepts it is settled by the real-environment milestone.
        var model = Build("foreign-buyer");

        Assert.True(EditModelValidator.Validate(model).IsValid);

        var theirs = AnafValidator.Validate(UblSerializer.Serialize(model.ToUbl()), "FACT1");

        Assert.False(theirs.IsValid);
        Assert.Contains(
            theirs.Findings,
            f => f.Message.Contains("cui cumparator", StringComparison.OrdinalIgnoreCase));
    }

    [RequiresAnafValidatorFact]
    public void AFilledInCreditNoteProducesADocumentAnafAccepts()
    {
        var model = SampleEditModels.MinimalCreditNote();

        var ours = EditModelValidator.Validate(model);
        Assert.True(ours.IsValid, $"Our own validation rejected the credit note: {ours}");

        var theirs = AnafValidator.Validate(UblSerializer.Serialize(model.ToUbl()), "FCN");

        Assert.True(theirs.IsValid, $"ANAF's validator rejected the credit note: {theirs}");
    }

    private static InvoiceEditModel Build(string scenario)
    {
        switch (scenario)
        {
            case "minimal-invoice":
                return SampleEditModels.MinimalInvoice();

            case "full-invoice":
                return SampleEditModels.FullInvoice();

            case "reverse-charge":
                return SampleEditModels.ReverseChargeInvoice();

            case "mixed-vat":
                return SampleEditModels.MixedVatInvoice();

            case "prepaid":
                return SampleEditModels.PrepaidInvoice();

            case "line-discount":
            {
                var invoice = SampleEditModels.MinimalInvoice();
                invoice.Lines[0].DiscountAmount = 25.50m;
                invoice.Lines[0].DiscountReason = "Reducere comerciala";
                return invoice;
            }

            case "document-discount-and-charge":
            {
                var invoice = SampleEditModels.MinimalInvoice();
                invoice.AllowancesAndCharges.Add(new DocumentAllowanceChargeEditModel
                {
                    IsCharge = false,
                    Amount = 20.00m,
                    Reason = "Discount comercial",
                    VatCategory = VatCategory.StandardRate,
                    VatRate = 19m,
                });
                invoice.AllowancesAndCharges.Add(new DocumentAllowanceChargeEditModel
                {
                    IsCharge = true,
                    Amount = 35.00m,
                    Reason = "Transport",
                    VatCategory = VatCategory.StandardRate,
                    VatRate = 19m,
                });
                return invoice;
            }

            case "foreign-buyer":
            {
                var invoice = SampleEditModels.MinimalInvoice();
                invoice.Buyer.Name = "Kunde GmbH";
                invoice.Buyer.TaxId = "DE123456789";
                invoice.Buyer.VatNumber = "DE123456789";
                invoice.Buyer.Address = new AddressEditModel
                {
                    Street = "Hauptstrasse 5",
                    City = "Berlin",
                    Region = "Berlin",
                    PostalCode = "10115",
                    CountryCode = "DE",
                };
                invoice.Lines[0].VatCategory = VatCategory.IntraCommunitySupply;
                invoice.Lines[0].VatRate = null;
                invoice.Lines[0].VatExemptionReason = "Livrare intracomunitara scutita";
                // BR-IC-11 and BR-IC-12: zero rating depends on showing the goods left Romania.
                invoice.DeliveryDate = new DateOnly(2026, 8, 30);
                invoice.DeliveryAddress = new AddressEditModel
                {
                    Street = "Hauptstrasse 5",
                    City = "Berlin",
                    Region = "Berlin",
                    CountryCode = "DE",
                };
                return invoice;
            }

            case "fractional-unit-price":
            {
                // BT-146 permits more than two decimals; the line net amount still may not.
                var invoice = SampleEditModels.MinimalInvoice();
                invoice.Lines[0].Quantity = 7m;
                invoice.Lines[0].UnitPrice = 12.3456m;
                return invoice;
            }

            case "price-base-quantity":
            {
                var invoice = SampleEditModels.MinimalInvoice();
                invoice.Lines[0].Quantity = 250m;
                invoice.Lines[0].UnitPrice = 42.00m;
                invoice.Lines[0].PriceBaseQuantity = 100m;
                invoice.Lines[0].UnitCode = "KGM";
                return invoice;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario.");
        }
    }
}
