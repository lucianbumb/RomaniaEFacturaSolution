using System.ComponentModel.DataAnnotations;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.EditModels;

/// <summary>
/// What an invoice and a credit note have in common.
/// </summary>
/// <remarks>
/// <para>
/// The totals are absent by design. EN16931 requires seven of them — BT-106 through BT-115 — plus
/// a VAT breakdown per category and rate, and then spends a dozen rules checking that all of them
/// agree with the lines. Every one of those figures follows from the lines and the adjustments, so
/// the model computes them rather than asking. That removes BR-CO-10, BR-CO-13, BR-CO-14,
/// BR-CO-15, BR-CO-16 and the whole <c>BR-*-08</c> and <c>BR-*-09</c> families from the set of
/// mistakes a caller can make: not caught, but unrepresentable.
/// </para>
/// <para>
/// The derived properties are public so a form can show a running total, and are computed on each
/// access rather than cached because a form mutates the lines underneath them.
/// </para>
/// </remarks>
public abstract class DocumentEditModel : IValidatableObject
{
    /// <summary>Document number (BT-1). Must be unique for the seller.</summary>
    [Required(ErrorMessage = "The document number is required.")]
    [StringLength(CiusRoLengths.DocumentNumber, MinimumLength = 1)]
    [Display(Name = "Number")]
    public string Number { get; set; } = string.Empty;

    /// <summary>Issue date (BT-2).</summary>
    [Required(ErrorMessage = "The issue date is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "Issue date")]
    public DateOnly IssueDate { get; set; }

    /// <summary>
    /// Currency (BT-5), as an ISO 4217 code.
    /// </summary>
    /// <remarks>
    /// Only <c>RON</c> is accepted for now. BR-RO-030 requires a document in any other currency to
    /// state its VAT in RON as well (BT-6 and BT-111), which this model does not yet produce — so
    /// rather than build a document ANAF would refuse, the model refuses it first. Foreign-currency
    /// invoicing goes through <c>SendRawXmlAsync</c> until that is built.
    /// </remarks>
    [Required(ErrorMessage = "The currency is required.")]
    [RegularExpression("^[A-Z]{3}$", ErrorMessage = "The currency must be a three-letter ISO code, such as RON or EUR.")]
    [Display(Name = "Currency")]
    public string Currency { get; set; } = "RON";

    /// <summary>The seller (BG-4).</summary>
    [Required]
    public PartyEditModel Seller { get; set; } = new();

    /// <summary>The buyer (BG-7).</summary>
    [Required]
    public PartyEditModel Buyer { get; set; } = new();

    /// <summary>
    /// The seller's fiscal representative in Romania (BG-11), where one is appointed.
    /// </summary>
    /// <remarks>
    /// Needed when the seller is not established in Romania. Leaving it null is the ordinary case.
    /// </remarks>
    public TaxRepresentativeEditModel? TaxRepresentative { get; set; }

    /// <summary>The document lines (BG-25). At least one is required by BR-16.</summary>
    public List<DocumentLineEditModel> Lines { get; set; } = [];

    /// <summary>Whole-document discounts and charges (BG-20 / BG-21).</summary>
    public List<DocumentAllowanceChargeEditModel> AllowancesAndCharges { get; set; } = [];

    /// <summary>Free-text notes (BT-22).</summary>
    public List<string> Notes { get; set; } = [];

    /// <summary>
    /// Buyer reference (BT-10), typically a cost centre or contract code the buyer supplied.
    /// </summary>
    /// <remarks>
    /// Public-sector buyers routinely require it and reject invoices without one, though EN16931
    /// itself does not.
    /// </remarks>
    [Display(Name = "Buyer reference")]
    public string? BuyerReference { get; set; }

    /// <summary>Purchase order number (BT-13).</summary>
    [StringLength(CiusRoLengths.OrderReference)]
    [Display(Name = "Purchase order")]
    public string? OrderReference { get; set; }

    /// <summary>Buyer's accounting reference for the document (BT-19).</summary>
    [StringLength(CiusRoLengths.AccountingReference)]
    [Display(Name = "Cost centre")]
    public string? AccountingReference { get; set; }

    /// <summary>When the goods or services were delivered (BT-72).</summary>
    [DataType(DataType.Date)]
    [Display(Name = "Delivery date")]
    public DateOnly? DeliveryDate { get; set; }

    /// <summary>Where they were delivered (BG-15), when it is not the buyer's address.</summary>
    public AddressEditModel? DeliveryAddress { get; set; }

    /// <summary>Start of the period the document covers (BT-73).</summary>
    [DataType(DataType.Date)]
    [Display(Name = "Period from")]
    public DateOnly? PeriodStart { get; set; }

    /// <summary>End of the period the document covers (BT-74).</summary>
    [DataType(DataType.Date)]
    [Display(Name = "Period to")]
    public DateOnly? PeriodEnd { get; set; }

    /// <summary>How payment should be made (BG-16).</summary>
    public PaymentEditModel? Payment { get; set; }

    /// <summary>Payment terms in words (BT-20).</summary>
    [StringLength(CiusRoLengths.PaymentTerms)]
    [Display(Name = "Payment terms")]
    public string? PaymentTerms { get; set; }

    /// <summary>Amount already paid (BT-113).</summary>
    [Range(0, double.MaxValue, ErrorMessage = "The amount already paid must not be negative.")]
    [Display(Name = "Already paid")]
    public decimal? AmountAlreadyPaid { get; set; }

    /// <summary>Documents this one refers to (BG-3), such as the invoice being corrected.</summary>
    public List<DocumentReferenceEditModel> PrecedingDocuments { get; set; } = [];

    /// <summary>
    /// Why no VAT is charged, where no line says so itself (BT-120).
    /// </summary>
    /// <remarks>
    /// A fallback for the common case of a document that is wholly exempt or wholly under reverse
    /// charge. A line's own <see cref="DocumentLineEditModel.VatExemptionReason"/> takes
    /// precedence, which is what makes a mixed document expressible.
    /// </remarks>
    [StringLength(CiusRoLengths.VatExemptionReason)]
    [Display(Name = "VAT exemption reason")]
    public string? VatExemptionReason { get; set; }

    /// <summary>A coded exemption reason applied the same way (BT-121).</summary>
    [StringLength(50)]
    [Display(Name = "VAT exemption code")]
    public string? VatExemptionReasonCode { get; set; }

    // ------------------------------------------------------------------ derived

    /// <summary>Sum of the line net amounts (BT-106).</summary>
    public decimal LineTotal => Money.Round(Lines.Sum(line => line.NetAmount));

    /// <summary>Sum of the document-level discounts (BT-107).</summary>
    public decimal AllowanceTotal =>
        Money.Round(AllowancesAndCharges.Where(a => !a.IsCharge).Sum(a => a.Amount));

    /// <summary>Sum of the document-level charges (BT-108).</summary>
    public decimal ChargeTotal =>
        Money.Round(AllowancesAndCharges.Where(a => a.IsCharge).Sum(a => a.Amount));

    /// <summary>Total before VAT (BT-109).</summary>
    public decimal TaxExclusiveTotal => Money.Round(LineTotal - AllowanceTotal + ChargeTotal);

    /// <summary>
    /// The VAT breakdown (BG-23), one entry per category and rate actually used.
    /// </summary>
    /// <remarks>
    /// Grouped by category and rate together, not by category alone: a document carrying both 21%
    /// and 11% standard-rate lines needs an entry for each, and merging them produces a VAT figure
    /// that fails BR-S-09.
    /// </remarks>
    public IReadOnlyList<VatBreakdownEntry> VatBreakdown
    {
        get
        {
            var groups = new Dictionary<(VatCategory Category, decimal? Rate), VatBreakdownBuilder>();

            foreach (var line in Lines)
            {
                var key = (line.VatCategory, line.EffectiveVatRate);
                if (!groups.TryGetValue(key, out var group))
                {
                    groups[key] = group = new VatBreakdownBuilder();
                }

                group.Taxable += line.NetAmount;
                group.Reason ??= NullIfBlank(line.VatExemptionReason);
                group.ReasonCode ??= NullIfBlank(line.VatExemptionReasonCode);
            }

            // A document-level adjustment lands in the group it names, creating one if it carries
            // a category no line used — unusual, but legitimate.
            foreach (var adjustment in AllowancesAndCharges)
            {
                var key = (adjustment.VatCategory, adjustment.EffectiveVatRate);
                if (!groups.TryGetValue(key, out var group))
                {
                    groups[key] = group = new VatBreakdownBuilder();
                }

                group.Taxable += adjustment.IsCharge ? adjustment.Amount : -adjustment.Amount;
            }

            return
            [
                .. groups
                    .OrderBy(g => g.Key.Category)
                    .ThenBy(g => g.Key.Rate ?? -1m)
                    .Select(g =>
                    {
                        var taxable = Money.Round(g.Value.Taxable);
                        return new VatBreakdownEntry(
                            g.Key.Category,
                            g.Key.Rate,
                            taxable,
                            Money.Vat(taxable, g.Key.Rate ?? 0m),
                            g.Value.Reason ?? NullIfBlank(VatExemptionReason),
                            g.Value.ReasonCode ?? NullIfBlank(VatExemptionReasonCode));
                    })
            ];
        }
    }

    /// <summary>Total VAT (BT-110).</summary>
    public decimal VatTotal => Money.Round(VatBreakdown.Sum(entry => entry.VatAmount));

    /// <summary>Total with VAT (BT-112).</summary>
    public decimal TaxInclusiveTotal => Money.Round(TaxExclusiveTotal + VatTotal);

    /// <summary>Amount due for payment (BT-115).</summary>
    public decimal PayableAmount => Money.Round(TaxInclusiveTotal - (AmountAlreadyPaid ?? 0m));

    // ------------------------------------------------------------- cross-field

    /// <summary>The word to use for this document in a validation message.</summary>
    protected abstract string DocumentNoun { get; }

    /// <inheritdoc />
    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Lines.Count == 0)
        {
            yield return new ValidationResult(
                $"A {DocumentNoun} must have at least one line (BR-16).", [nameof(Lines)]);
        }

        // BR-RO-010. Romania adds this to EN16931: a document number with no digit in it is
        // refused, so a series like "FACTURA/A" cannot be sent.
        if (!string.IsNullOrWhiteSpace(Number) && !Number.Any(char.IsAsciiDigit))
        {
            yield return new ValidationResult(
                $"The document number '{Number}' must contain at least one digit (BR-RO-010).",
                [nameof(Number)]);
        }

        // BR-RO-030. Stated as a limit of this model rather than as a rule of the standard: a
        // non-RON document is legal, but needs a VAT-in-RON breakdown the mapper cannot yet build.
        if (!string.Equals(Currency, "RON", StringComparison.Ordinal))
        {
            yield return new ValidationResult(
                $"Only RON is supported for now. A document in {Currency} must also state its VAT "
                + "in RON (BR-RO-030), which this model cannot yet produce — use SendRawXmlAsync.",
                [nameof(Currency)]);
        }

        // BR-RO-A020 and BR-RO-A500 cap two repeating groups ANAF would otherwise refuse silently.
        if (Notes.Count > CiusRoLengths.MaxNotes)
        {
            yield return new ValidationResult(
                $"A document may carry at most {CiusRoLengths.MaxNotes} notes (BR-RO-A020); "
                + $"this one has {Notes.Count}.",
                [nameof(Notes)]);
        }

        // BR-RO-L302. The notes are a plain string list, so no attribute can reach them — the
        // only place their length can be checked is here.
        var overlongNote = Notes.FindIndex(note => note is { Length: > CiusRoLengths.Note });
        if (overlongNote >= 0)
        {
            yield return new ValidationResult(
                $"Note {overlongNote + 1} is {Notes[overlongNote].Length} characters; "
                + $"CIUS-RO allows {CiusRoLengths.Note} (BR-RO-L302).",
                [nameof(Notes)]);
        }

        // BR-RO-A052. Checked here rather than on the line, because a count is a property of the
        // list and DataAnnotations has no way to express one.
        var overloadedLine = Lines.FindIndex(
            line => line.ItemAttributes.Count > CiusRoLengths.MaxItemAttributes);
        if (overloadedLine >= 0)
        {
            yield return new ValidationResult(
                $"Line {overloadedLine + 1} carries {Lines[overloadedLine].ItemAttributes.Count} item "
                + $"attributes; CIUS-RO allows {CiusRoLengths.MaxItemAttributes} (BR-RO-A052).",
                [nameof(Lines)]);
        }

        if (PrecedingDocuments.Count > CiusRoLengths.MaxPrecedingDocuments)
        {
            yield return new ValidationResult(
                $"A document may reference at most {CiusRoLengths.MaxPrecedingDocuments} preceding "
                + $"documents (BR-RO-A500); this one references {PrecedingDocuments.Count}.",
                [nameof(PrecedingDocuments)]);
        }

        foreach (var result in ValidateCounty(Seller.Address, $"{nameof(Seller)}.{nameof(PartyEditModel.Address)}"))
        {
            yield return result;
        }

        foreach (var result in ValidateCounty(Buyer.Address, $"{nameof(Buyer)}.{nameof(PartyEditModel.Address)}"))
        {
            yield return result;
        }

        if (TaxRepresentative is not null)
        {
            foreach (var result in ValidateCounty(
                         TaxRepresentative.Address,
                         $"{nameof(TaxRepresentative)}.{nameof(TaxRepresentativeEditModel.Address)}"))
            {
                yield return result;
            }
        }

        if (DeliveryAddress is not null)
        {
            foreach (var result in ValidateCounty(DeliveryAddress, nameof(DeliveryAddress)))
            {
                yield return result;
            }

            // BR-RO-210, and stricter than it looks: a delivery address must name its subdivision
            // whatever the country, where an ISO 3166-2:RO code is demanded only for Romanian
            // ones. A German delivery address therefore needs its region in words.
            if (string.IsNullOrWhiteSpace(DeliveryAddress.County)
                && string.IsNullOrWhiteSpace(DeliveryAddress.Region))
            {
                yield return new ValidationResult(
                    "A delivery address must state its country subdivision — the county for a "
                    + "Romanian address, the region for any other (BR-RO-210).",
                    [$"{nameof(DeliveryAddress)}.{nameof(AddressEditModel.Region)}"]);
            }
        }

        // Reverse charge moves the VAT liability to the buyer, so both parties have to be
        // VAT-identified for it to mean anything (BR-AE-02, BR-AE-03).
        var usesReverseCharge =
            Lines.Exists(line => line.VatCategory == VatCategory.ReverseCharge)
            || AllowancesAndCharges.Exists(a => a.VatCategory == VatCategory.ReverseCharge);

        if (usesReverseCharge)
        {
            if (string.IsNullOrWhiteSpace(Seller.VatNumber))
            {
                yield return new ValidationResult(
                    "Reverse charge requires the seller to have a VAT number (BR-AE-02).",
                    [$"{nameof(Seller)}.{nameof(PartyEditModel.VatNumber)}"]);
            }

            if (string.IsNullOrWhiteSpace(Buyer.VatNumber))
            {
                yield return new ValidationResult(
                    "Reverse charge requires the buyer to have a VAT number (BR-AE-03).",
                    [$"{nameof(Buyer)}.{nameof(PartyEditModel.VatNumber)}"]);
            }
        }

        // Every category that charges no VAT has to say why, and the answer may come from the line
        // or from the document. Checking the assembled breakdown catches the only case that
        // matters: neither supplied one.
        foreach (var entry in VatBreakdown.Where(e => e.Category.RequiresExemptionReason()))
        {
            if (string.IsNullOrWhiteSpace(entry.ExemptionReason)
                && string.IsNullOrWhiteSpace(entry.ExemptionReasonCode))
            {
                yield return new ValidationResult(
                    $"Lines with VAT category '{entry.Category.ToCode()}' must state why no VAT is "
                    + "charged — set it on the line or on the document (BT-120).",
                    [nameof(VatExemptionReason)]);
            }
        }

        // BR-IC-11 and BR-IC-12. An intra-community supply is zero-rated only if it can be shown
        // the goods left the country, so EN16931 insists the document says when and where. A
        // caller choosing the category has no reason to know that; the model asks on their behalf.
        var usesIntraCommunity =
            Lines.Exists(line => line.VatCategory == VatCategory.IntraCommunitySupply)
            || AllowancesAndCharges.Exists(a => a.VatCategory == VatCategory.IntraCommunitySupply);

        if (usesIntraCommunity)
        {
            if (DeliveryDate is null && PeriodStart is null && PeriodEnd is null)
            {
                yield return new ValidationResult(
                    "An intra-community supply must state the delivery date or the invoicing "
                    + "period (BR-IC-11).",
                    [nameof(DeliveryDate)]);
            }

            if (DeliveryAddress is null || string.IsNullOrWhiteSpace(DeliveryAddress.CountryCode))
            {
                yield return new ValidationResult(
                    "An intra-community supply must state the country the goods were delivered to "
                    + "(BR-IC-12).",
                    [nameof(DeliveryAddress)]);
            }
        }

        if (PeriodStart is not null && PeriodEnd is not null && PeriodEnd < PeriodStart)
        {
            yield return new ValidationResult(
                "The period end is before the period start (BR-29).", [nameof(PeriodEnd)]);
        }

        if (AmountAlreadyPaid is > 0 && AmountAlreadyPaid > TaxInclusiveTotal)
        {
            yield return new ValidationResult(
                $"The amount already paid ({AmountAlreadyPaid}) is more than the total ({TaxInclusiveTotal}).",
                [nameof(AmountAlreadyPaid)]);
        }

        var duplicateLineIds = Lines
            .Where(line => !string.IsNullOrWhiteSpace(line.Id))
            .GroupBy(line => line.Id!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateLineIds.Count > 0)
        {
            yield return new ValidationResult(
                $"Line numbers must be unique; {string.Join(", ", duplicateLineIds)} repeats.",
                [nameof(Lines)]);
        }
    }

    private static IEnumerable<ValidationResult> ValidateCounty(AddressEditModel address, string path)
    {
        // CIUS-RO narrows EN16931 here: a Romanian address must carry an ISO 3166-2:RO code, and
        // ANAF refuses the document without one. Elsewhere the field is free text and optional.
        if (address.IsRomanian && string.IsNullOrWhiteSpace(address.County))
        {
            yield return new ValidationResult(
                "A Romanian address must state its county as an ISO 3166-2:RO code such as RO-B (BT-39).",
                [$"{path}.{nameof(AddressEditModel.County)}"]);
        }
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed class VatBreakdownBuilder
    {
        public decimal Taxable { get; set; }

        public string? Reason { get; set; }

        public string? ReasonCode { get; set; }
    }
}

/// <summary>One entry of the VAT breakdown (BG-23), derived from the document.</summary>
/// <param name="Category">The VAT category (BT-118).</param>
/// <param name="Rate">The rate (BT-119), absent for the out-of-scope category.</param>
/// <param name="TaxableAmount">The net amount taxed at this category and rate (BT-116).</param>
/// <param name="VatAmount">The VAT that follows from it (BT-117).</param>
/// <param name="ExemptionReason">Why no VAT is charged (BT-120), where that applies.</param>
/// <param name="ExemptionReasonCode">The coded form of the same (BT-121).</param>
public sealed record VatBreakdownEntry(
    VatCategory Category,
    decimal? Rate,
    decimal TaxableAmount,
    decimal VatAmount,
    string? ExemptionReason,
    string? ExemptionReasonCode);
