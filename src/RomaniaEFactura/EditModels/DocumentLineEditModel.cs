using System.ComponentModel.DataAnnotations;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.EditModels;

/// <summary>
/// One line of an invoice or a credit note (BG-25).
/// </summary>
/// <remarks>
/// <para>
/// Notice what is not here: the line net amount (BT-131). EN16931 requires the document to state
/// it and BR-CO-04 and the <c>BR-*-08</c> family then check it against quantity, price and the
/// line's adjustments. Asking for a figure and then rejecting the document when it disagrees is
/// exactly the kind of arithmetic a library should do — so the amount is
/// <see cref="NetAmount">derived</see>, and a document that disagrees with its own lines becomes
/// impossible to express rather than merely detectable.
/// </para>
/// </remarks>
public sealed class DocumentLineEditModel : IValidatableObject
{
    /// <summary>
    /// Line identifier (BT-126). Left empty, the position in the list is used.
    /// </summary>
    [StringLength(50)]
    [Display(Name = "Line number")]
    public string? Id { get; set; }

    /// <summary>What is being sold (BT-153).</summary>
    [Required(ErrorMessage = "The item name is required.")]
    [StringLength(CiusRoLengths.ItemName, MinimumLength = 1)]
    [Display(Name = "Item")]
    public string Name { get; set; } = string.Empty;

    /// <summary>A longer description of the item (BT-154).</summary>
    [StringLength(CiusRoLengths.ItemDescription)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    /// <summary>The seller's own code for the item (BT-155).</summary>
    [StringLength(100)]
    [Display(Name = "Item code")]
    public string? SellerItemCode { get; set; }

    /// <summary>
    /// How much is being invoiced (BT-129).
    /// </summary>
    /// <remarks>
    /// May be negative on an invoice, which is how a correction line is expressed; a credit note
    /// states positive quantities and is negative by its nature.
    /// </remarks>
    [Display(Name = "Quantity")]
    public decimal Quantity { get; set; } = 1m;

    /// <summary>
    /// UN/ECE Recommendation 20 unit code (BT-130). <c>H87</c> is "piece".
    /// </summary>
    /// <remarks>
    /// Common alternatives: <c>HUR</c> hour, <c>DAY</c> day, <c>KGM</c> kilogram, <c>MTR</c> metre,
    /// <c>LTR</c> litre, <c>MTQ</c> cubic metre, <c>MON</c> month, <c>C62</c> a dimensionless unit.
    /// </remarks>
    [Required(ErrorMessage = "The unit of measure is required.")]
    [StringLength(10, MinimumLength = 1)]
    [Display(Name = "Unit")]
    public string UnitCode { get; set; } = "H87";

    /// <summary>
    /// Net price of one unit, before VAT and before any line discount (BT-146).
    /// </summary>
    /// <remarks>
    /// Unlike the document totals this may carry more than two decimal places — ANAF's own example
    /// uses four — so a unit price of 12.3456 is written as given rather than rounded away.
    /// </remarks>
    [Range(0, double.MaxValue, ErrorMessage = "The unit price must not be negative (BR-27).")]
    [Display(Name = "Unit price")]
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// The number of units the price covers (BT-149), when it is not one.
    /// </summary>
    /// <remarks>
    /// For goods priced per hundred or per thousand. Leaving it null means the price is per unit.
    /// </remarks>
    [Range(0.0000001, double.MaxValue, ErrorMessage = "The price base quantity must be greater than zero.")]
    [Display(Name = "Price per")]
    public decimal? PriceBaseQuantity { get; set; }

    /// <summary>How VAT applies to this line (BT-151).</summary>
    [Display(Name = "VAT category")]
    public VatCategory VatCategory { get; set; } = VatCategory.StandardRate;

    /// <summary>
    /// VAT percentage (BT-152), for the standard-rate category.
    /// </summary>
    /// <remarks>
    /// Ignored for every other category, whose rate the code itself fixes: writing a rate on an
    /// exempt line is one of the mistakes this model is meant to prevent.
    /// </remarks>
    [Range(0, 100, ErrorMessage = "The VAT rate must be between 0 and 100.")]
    [Display(Name = "VAT rate %")]
    public decimal? VatRate { get; set; }

    /// <summary>A discount applied to this line (BG-27).</summary>
    [Range(0, double.MaxValue, ErrorMessage = "A discount must not be negative.")]
    [Display(Name = "Discount")]
    public decimal? DiscountAmount { get; set; }

    /// <summary>Why the discount is given (BT-104).</summary>
    [StringLength(CiusRoLengths.LineAdjustmentReason)]
    [Display(Name = "Discount reason")]
    public string? DiscountReason { get; set; }

    /// <summary>A charge added to this line (BG-28).</summary>
    [Range(0, double.MaxValue, ErrorMessage = "A charge must not be negative.")]
    [Display(Name = "Charge")]
    public decimal? ChargeAmount { get; set; }

    /// <summary>Why the charge is made (BT-105).</summary>
    [StringLength(CiusRoLengths.LineAdjustmentReason)]
    [Display(Name = "Charge reason")]
    public string? ChargeReason { get; set; }

    /// <summary>
    /// Why no VAT is charged on this line (BT-120).
    /// </summary>
    /// <remarks>
    /// Required for every category except standard and zero rated. It is asked here, on the line
    /// whose treatment prompts the question, but written to the document-level VAT breakdown,
    /// which is the only place EN16931 permits it: rules UBL-CR-598 to UBL-CR-604 forbid it on the
    /// line, and ANAF rejects a document that states it in both places.
    /// </remarks>
    [StringLength(CiusRoLengths.VatExemptionReason)]
    [Display(Name = "VAT exemption reason")]
    public string? VatExemptionReason { get; set; }

    /// <summary>A coded exemption reason (BT-121), where one applies.</summary>
    [StringLength(50)]
    [Display(Name = "VAT exemption code")]
    public string? VatExemptionReasonCode { get; set; }

    /// <summary>A free-text note for this line (BT-127).</summary>
    [StringLength(CiusRoLengths.LineNote)]
    [Display(Name = "Note")]
    public string? Note { get; set; }

    /// <summary>Buyer's accounting reference for this line (BT-133).</summary>
    [StringLength(CiusRoLengths.LineAccountingReference)]
    [Display(Name = "Cost centre")]
    public string? AccountingReference { get; set; }

    /// <summary>
    /// The gross value of the line before its own discounts and charges.
    /// </summary>
    /// <remarks>
    /// Rounded to two places at this point, before adjustments, which is the order EN16931's
    /// worked examples follow.
    /// </remarks>
    public decimal GrossAmount => Money.Round(Quantity * UnitPrice / (PriceBaseQuantity ?? 1m));

    /// <summary>
    /// The line net amount (BT-131): gross, less the discount, plus the charge.
    /// </summary>
    public decimal NetAmount => Money.Round(GrossAmount - (DiscountAmount ?? 0m) + (ChargeAmount ?? 0m));

    /// <summary>The VAT rate that will actually be written for this line.</summary>
    public decimal? EffectiveVatRate => VatCategory.EffectiveRate(VatRate);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (VatCategory.RequiresPositiveRate() && VatRate is not > 0)
        {
            yield return new ValidationResult(
                "A standard-rate line must state a VAT rate greater than zero (BT-152).",
                [nameof(VatRate)]);
        }

        // A discount larger than the line's own value produces a negative net amount that no
        // amount of downstream arithmetic can make sensible.
        if (DiscountAmount is > 0 && DiscountAmount > GrossAmount + (ChargeAmount ?? 0m))
        {
            yield return new ValidationResult(
                $"The discount ({DiscountAmount}) is larger than the line value ({GrossAmount}).",
                [nameof(DiscountAmount)]);
        }

        if (DiscountAmount is > 0 && string.IsNullOrWhiteSpace(DiscountReason))
        {
            yield return new ValidationResult(
                "A line discount must state a reason (BT-104).",
                [nameof(DiscountReason)]);
        }

        if (ChargeAmount is > 0 && string.IsNullOrWhiteSpace(ChargeReason))
        {
            yield return new ValidationResult(
                "A line charge must state a reason (BT-105).",
                [nameof(ChargeReason)]);
        }
    }
}
