using System.ComponentModel.DataAnnotations;

namespace RomaniaEFactura.EditModels;

/// <summary>
/// An invoice, in the shape an application would ask a person to fill in.
/// </summary>
/// <remarks>
/// <para>
/// This is the model the library promises for: fill it in, let
/// <see cref="IRomaniaEFacturaService.Verify(InvoiceEditModel)"/> agree, and ANAF will not reject
/// the result on format grounds. The attributes make that promise visible in a form — a Blazor
/// <c>EditForm</c> with a <c>DataAnnotationsValidator</c> shows every field-level rule with no
/// extra code — and <see cref="Validate"/> covers the cross-field rules attributes cannot express.
/// </para>
/// <para>
/// Where the model cannot express something, <c>SendRawXmlAsync</c> is the way out; nothing sent
/// that way carries the guarantee.
/// </para>
/// </remarks>
public sealed class InvoiceEditModel : DocumentEditModel
{
    /// <summary>
    /// UNCL1001 invoice type code (BT-3). <c>380</c> is a commercial invoice.
    /// </summary>
    /// <remarks>
    /// Also seen in Romania: <c>384</c> a corrected invoice, <c>389</c> a self-billed invoice,
    /// <c>751</c> an accounting-information-only document.
    /// </remarks>
    [Required(ErrorMessage = "The invoice type code is required.")]
    [StringLength(4, MinimumLength = 3)]
    [Display(Name = "Invoice type")]
    public string TypeCode { get; set; } = "380";

    /// <summary>
    /// When payment is due (BT-9).
    /// </summary>
    /// <remarks>
    /// BR-CO-25 requires an invoice with an amount outstanding to state either this or
    /// <see cref="DocumentEditModel.PaymentTerms"/>, so that the buyer knows when to pay.
    /// </remarks>
    [DataType(DataType.Date)]
    [Display(Name = "Due date")]
    public DateOnly? DueDate { get; set; }

    /// <inheritdoc />
    protected override string DocumentNoun => "invoice";

    /// <inheritdoc />
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext)) yield return result;

        if (DueDate is not null && DueDate < IssueDate)
        {
            yield return new ValidationResult(
                "The due date is before the issue date.", [nameof(DueDate)]);
        }

        // BR-CO-25: something has to tell the buyer when to pay. ANAF applies this to invoices
        // only — a credit note with neither is accepted.
        if (PayableAmount > 0 && DueDate is null && string.IsNullOrWhiteSpace(PaymentTerms))
        {
            yield return new ValidationResult(
                "An invoice with an amount due must state either a due date or payment terms (BR-CO-25).",
                [nameof(DueDate)]);
        }
    }
}

/// <summary>
/// A credit note, correcting an invoice already sent.
/// </summary>
/// <remarks>
/// Quantities and amounts are stated positive here, as they are in the UBL document: the credit
/// note is negative by its nature, not by its arithmetic. Entering negatives produces a document
/// that credits the wrong way round, so the model rejects them.
/// </remarks>
public sealed class CreditNoteEditModel : DocumentEditModel
{
    /// <summary>
    /// UNCL1001 credit note type code (BT-3). <c>381</c> is a commercial credit note.
    /// </summary>
    [Required(ErrorMessage = "The credit note type code is required.")]
    [StringLength(4, MinimumLength = 3)]
    [Display(Name = "Credit note type")]
    public string TypeCode { get; set; } = "381";

    /// <inheritdoc />
    protected override string DocumentNoun => "credit note";

    /// <inheritdoc />
    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext)) yield return result;

        // Not an EN16931 rule, but a credit note referring to nothing cannot be matched to what it
        // corrects, and the buyer's accounting system will not know what to do with it.
        if (PrecedingDocuments.Count == 0)
        {
            yield return new ValidationResult(
                "A credit note must reference the invoice it corrects (BG-3).",
                [nameof(PrecedingDocuments)]);
        }

        var negativeLines = Lines
            .Select((line, index) => (line, index))
            .Where(entry => entry.line.Quantity < 0)
            .Select(entry => entry.index + 1)
            .ToList();

        if (negativeLines.Count > 0)
        {
            yield return new ValidationResult(
                "A credit note states positive quantities — it is already a credit. "
                + $"Line {string.Join(", ", negativeLines)} is negative.",
                [nameof(Lines)]);
        }
    }
}
