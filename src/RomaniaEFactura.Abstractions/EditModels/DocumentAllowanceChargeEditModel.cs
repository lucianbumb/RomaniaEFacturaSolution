using System.ComponentModel.DataAnnotations;
using RomaniaEFactura.Validation;
using RomaniaEFactura.EditModels.Attributes;

namespace RomaniaEFactura.EditModels;

/// <summary>
/// A discount or a charge applied to the whole document (BG-20 / BG-21).
/// </summary>
/// <remarks>
/// UBL expresses both with one element distinguished by a boolean, which reads badly and is easy
/// to invert. Here the amount is always positive and <see cref="IsCharge"/> says which it is, so a
/// negative discount — a thing UBL will happily serialize — cannot be written.
/// </remarks>
public sealed class DocumentAllowanceChargeEditModel : IValidatableObject
{
    /// <summary>
    /// <see langword="false"/> for a discount, <see langword="true"/> for a charge.
    /// </summary>
    [Display(Name = "Is a charge")]
    public bool IsCharge { get; set; }

    /// <summary>The amount, always positive (BT-92 / BT-99).</summary>
    [Range(0.01, double.MaxValue, ErrorMessage = "The amount must be greater than zero.")]
    [Display(Name = "Amount")]
    public decimal Amount { get; set; }

    /// <summary>Why it applies (BT-97 / BT-104).</summary>
    [Required(ErrorMessage = "A document-level discount or charge must state a reason.")]
    [StringLength(CiusRoLengths.DocumentAdjustmentReason, MinimumLength = 1)]
    [Display(Name = "Reason")]
    public string Reason { get; set; } = string.Empty;

    /// <summary>A coded reason (BT-98 / BT-105), where the buyer expects one.</summary>
    [StringLength(50)]
    [Display(Name = "Reason code")]
    public string? ReasonCode { get; set; }

    /// <summary>
    /// Which VAT category the adjustment belongs to (BT-95 / BT-102).
    /// </summary>
    /// <remarks>
    /// Required because the adjustment changes a taxable amount, and EN16931 has to know which
    /// one. Naming a category and rate that no line uses creates a breakdown entry of its own,
    /// which is legitimate but rarely intended — worth checking on screen.
    /// </remarks>
    [Display(Name = "VAT category")]
    public VatCategory VatCategory { get; set; } = VatCategory.StandardRate;

    /// <summary>The VAT rate it is taxed at (BT-96 / BT-103), for the standard category.</summary>
    [Range(0, 100, ErrorMessage = "The VAT rate must be between 0 and 100.")]
    [Display(Name = "VAT rate %")]
    public decimal? VatRate { get; set; }

    /// <summary>The rate that will actually be written.</summary>
    public decimal? EffectiveVatRate => VatCategory.EffectiveRate(VatRate);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (VatCategory.RequiresPositiveRate() && VatRate is not > 0)
        {
            yield return new ValidationResult(
                "A standard-rate discount or charge must state a VAT rate greater than zero.",
                [nameof(VatRate)]);
        }
    }
}

/// <summary>How the document is to be paid (BG-16).</summary>
public sealed class PaymentEditModel : IValidatableObject
{
    /// <summary>
    /// UNCL4461 payment means code (BT-81). <c>31</c> is a credit transfer.
    /// </summary>
    /// <remarks>
    /// Other codes in common use: <c>1</c> unspecified, <c>10</c> cash, <c>42</c> payment to a
    /// bank account, <c>48</c> card, <c>49</c> direct debit, <c>58</c> SEPA credit transfer.
    /// </remarks>
    [Required(ErrorMessage = "The payment means code is required.")]
    [StringLength(10, MinimumLength = 1)]
    [Display(Name = "Payment means")]
    public string MeansCode { get; set; } = "31";

    /// <summary>The account to be paid into (BT-84).</summary>
    [Iban]
    [StringLength(34)]
    [Display(Name = "IBAN")]
    public string? Iban { get; set; }

    /// <summary>The account holder's name (BT-85).</summary>
    [StringLength(CiusRoLengths.PaymentAccountName)]
    [Display(Name = "Account holder")]
    public string? AccountHolder { get; set; }

    /// <summary>Remittance reference for the payer to quote (BT-83).</summary>
    [StringLength(CiusRoLengths.RemittanceInformation)]
    [Display(Name = "Payment reference")]
    public string? Reference { get; set; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // A credit transfer with no account to transfer to is not actionable, and BR-49 requires
        // the account for exactly the codes that mean "pay into an account".
        var needsAccount = MeansCode is "30" or "31" or "42" or "58";

        if (needsAccount && string.IsNullOrWhiteSpace(Iban))
        {
            yield return new ValidationResult(
                $"Payment means '{MeansCode}' is a transfer, so an account (BT-84) is required.",
                [nameof(Iban)]);
        }
    }
}

/// <summary>A reference to another document (BG-3 / BG-24).</summary>
public sealed class DocumentReferenceEditModel
{
    /// <summary>The referenced document's number (BT-25).</summary>
    [Required(ErrorMessage = "The referenced document number is required.")]
    [StringLength(CiusRoLengths.PrecedingDocumentNumber, MinimumLength = 1)]
    [Display(Name = "Document number")]
    public string Number { get; set; } = string.Empty;

    /// <summary>When it was issued (BT-26).</summary>
    [DataType(DataType.Date)]
    [Display(Name = "Issue date")]
    public DateOnly? IssueDate { get; set; }
}
