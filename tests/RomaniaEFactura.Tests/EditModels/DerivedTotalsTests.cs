using RomaniaEFactura.EditModels;

namespace RomaniaEFactura.Tests.EditModels;

/// <summary>
/// The arithmetic the edit model does so that a caller does not have to.
/// </summary>
/// <remarks>
/// These figures are the reason the model exists. EN16931 states each of them as a business term
/// and then checks it against the lines, so getting any one of them wrong is a rejection — and
/// they are exactly the sums a person filling in a form gets wrong.
/// </remarks>
public class DerivedTotalsTests
{
    [Fact]
    public void ALineNetAmountIsQuantityTimesPrice()
    {
        var line = new DocumentLineEditModel { Quantity = 2m, UnitPrice = 100.00m };

        Assert.Equal(200.00m, line.NetAmount);
    }

    [Fact]
    public void ALineNetAmountIsRoundedToTwoPlacesEvenWhenThePriceIsNot()
    {
        // BT-146 may carry four decimals; BT-131 may not. 7 x 12.3456 = 86.4192.
        var line = new DocumentLineEditModel { Quantity = 7m, UnitPrice = 12.3456m };

        Assert.Equal(86.42m, line.NetAmount);
    }

    [Fact]
    public void RoundingGoesAwayFromZeroRatherThanToEven()
    {
        // .NET's default would give 0.12, which is not how an invoice is added up.
        var line = new DocumentLineEditModel { Quantity = 1m, UnitPrice = 0.125m };

        Assert.Equal(0.13m, line.NetAmount);
    }

    [Fact]
    public void APriceBaseQuantityDividesTheUnitPrice()
    {
        // 250 kg priced at 42.00 per 100 kg.
        var line = new DocumentLineEditModel
        {
            Quantity = 250m,
            UnitPrice = 42.00m,
            PriceBaseQuantity = 100m,
        };

        Assert.Equal(105.00m, line.NetAmount);
    }

    [Fact]
    public void ALineDiscountReducesTheNetAmountAndACharheIncreasesIt()
    {
        var line = new DocumentLineEditModel
        {
            Quantity = 2m,
            UnitPrice = 100.00m,
            DiscountAmount = 25.50m,
            DiscountReason = "Reducere",
            ChargeAmount = 5.00m,
            ChargeReason = "Ambalare",
        };

        Assert.Equal(179.50m, line.NetAmount);
    }

    [Fact]
    public void TheDocumentTotalsFollowFromTheLines()
    {
        var invoice = SampleEditModels.MinimalInvoice();

        Assert.Equal(200.00m, invoice.LineTotal);
        Assert.Equal(200.00m, invoice.TaxExclusiveTotal);
        Assert.Equal(38.00m, invoice.VatTotal);
        Assert.Equal(238.00m, invoice.TaxInclusiveTotal);
        Assert.Equal(238.00m, invoice.PayableAmount);
    }

    [Fact]
    public void DocumentAdjustmentsMoveTheTotalWithoutTouchingTheLineTotal()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.AllowancesAndCharges.Add(new DocumentAllowanceChargeEditModel
        {
            IsCharge = true,
            Amount = 35.00m,
            Reason = "Transport",
            VatRate = 19m,
        });
        invoice.AllowancesAndCharges.Add(new DocumentAllowanceChargeEditModel
        {
            IsCharge = false,
            Amount = 20.00m,
            Reason = "Discount",
            VatRate = 19m,
        });

        // BT-106 is the lines alone; BT-109 is the lines after adjustments.
        Assert.Equal(200.00m, invoice.LineTotal);
        Assert.Equal(35.00m, invoice.ChargeTotal);
        Assert.Equal(20.00m, invoice.AllowanceTotal);
        Assert.Equal(215.00m, invoice.TaxExclusiveTotal);
        Assert.Equal(40.85m, invoice.VatTotal);
        Assert.Equal(255.85m, invoice.TaxInclusiveTotal);
    }

    [Fact]
    public void AmountAlreadyPaidReducesWhatIsDueButNotTheTotal()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.AmountAlreadyPaid = 100.00m;

        Assert.Equal(238.00m, invoice.TaxInclusiveTotal);
        Assert.Equal(138.00m, invoice.PayableAmount);
    }

    [Fact]
    public void TwoRatesInTheSameCategoryProduceSeparateBreakdownEntries()
    {
        // Merging 19% and 5% into one 'S' entry would produce a VAT figure matching neither rate,
        // which is BR-S-09.
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Lines.Add(new DocumentLineEditModel
        {
            Name = "Manual tiparit",
            Quantity = 5m,
            UnitPrice = 20.00m,
            VatRate = 5m,
        });

        var breakdown = invoice.VatBreakdown;

        Assert.Equal(2, breakdown.Count);
        Assert.Contains(breakdown, e => e.Rate == 19m && e.TaxableAmount == 200.00m && e.VatAmount == 38.00m);
        Assert.Contains(breakdown, e => e.Rate == 5m && e.TaxableAmount == 100.00m && e.VatAmount == 5.00m);
        Assert.Equal(43.00m, invoice.VatTotal);
    }

    [Fact]
    public void ExemptAndStandardLinesEachGetTheirOwnEntry()
    {
        var invoice = SampleEditModels.MixedVatInvoice();

        var exempt = Assert.Single(invoice.VatBreakdown, e => e.Category == VatCategory.Exempt);
        Assert.Equal(300.00m, exempt.TaxableAmount);
        Assert.Equal(0m, exempt.VatAmount);
        Assert.Equal("Scutit conform art. 292 Cod Fiscal", exempt.ExemptionReason);

        // Only the standard-rate portion is taxed.
        Assert.Equal(38.00m, invoice.VatTotal);
        Assert.Equal(500.00m, invoice.TaxExclusiveTotal);
    }

    [Fact]
    public void AnOutOfScopeLineCarriesNoRateAtAll()
    {
        // BR-O-08 rejects a rate of zero here; the correct statement is that VAT does not apply.
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Lines[0].VatCategory = VatCategory.OutsideScope;
        invoice.Lines[0].VatRate = 19m;
        invoice.Lines[0].VatExemptionReason = "Neimpozabil";

        Assert.Null(invoice.Lines[0].EffectiveVatRate);
        Assert.Null(Assert.Single(invoice.VatBreakdown).Rate);
    }

    [Fact]
    public void ARateWrittenOnAnExemptLineIsIgnoredRatherThanEmitted()
    {
        // A caller who switches a line from standard to exempt without clearing the rate would
        // otherwise produce a document that fails BR-E-05.
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Lines[0].VatCategory = VatCategory.Exempt;
        invoice.Lines[0].VatExemptionReason = "Scutit";

        Assert.Equal(0m, invoice.Lines[0].EffectiveVatRate);
        Assert.Equal(0m, invoice.VatTotal);
    }

    [Fact]
    public void ADocumentLevelAdjustmentJoinsTheBreakdownEntryItNames()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.AllowancesAndCharges.Add(new DocumentAllowanceChargeEditModel
        {
            IsCharge = false,
            Amount = 50.00m,
            Reason = "Discount",
            VatRate = 19m,
        });

        var entry = Assert.Single(invoice.VatBreakdown);
        Assert.Equal(150.00m, entry.TaxableAmount);
        Assert.Equal(28.50m, entry.VatAmount);
    }
}
