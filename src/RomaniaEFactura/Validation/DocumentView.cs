using RomaniaEFactura.Ubl;

namespace RomaniaEFactura.Validation;

/// <summary>
/// A shape common to invoices and credit notes, so each rule is written once.
/// </summary>
/// <remarks>
/// UBL models the two as separate schema types with differently named members
/// (<c>InvoiceLine</c>/<c>InvoicedQuantity</c> versus <c>CreditNoteLine</c>/<c>CreditedQuantity</c>),
/// but the EN16931 rules apply to both identically.
/// </remarks>
internal sealed record DocumentView
{
    /// <summary>Whether this is an invoice or a credit note.</summary>
    public required string DocumentType { get; init; }

    /// <summary>Document number (BT-1).</summary>
    public required string Id { get; init; }

    /// <summary>Specification identifier (BT-24).</summary>
    public required string CustomizationId { get; init; }

    /// <summary>Issue date (BT-2).</summary>
    public required DateTime IssueDate { get; init; }

    /// <summary>Payment due date (BT-9). Credit notes do not carry one.</summary>
    public DateTime? DueDate { get; init; }

    /// <summary>Document type code (BT-3).</summary>
    public required string TypeCode { get; init; }

    /// <summary>Document currency (BT-5).</summary>
    public required string CurrencyCode { get; init; }

    /// <summary>The seller (BG-4).</summary>
    public required Party Seller { get; init; }

    /// <summary>The buyer (BG-7).</summary>
    public required Party Buyer { get; init; }

    /// <summary>The document lines (BG-25).</summary>
    public required IReadOnlyList<LineView> Lines { get; init; }

    /// <summary>VAT breakdown (BG-22).</summary>
    public required IReadOnlyList<TaxTotal> TaxTotals { get; init; }

    /// <summary>Document totals (BG-22).</summary>
    public required MonetaryTotal Totals { get; init; }

    /// <summary>Document-level allowances and charges.</summary>
    public required IReadOnlyList<AllowanceCharge> AllowanceCharges { get; init; }

    /// <summary>Payment terms (BT-20).</summary>
    public PaymentTerms? PaymentTerms { get; init; }

    /// <summary>Invoicing period (BG-14).</summary>
    public Period? InvoicePeriod { get; init; }

    /// <summary>Delivery details (BG-13), which carry an address of their own.</summary>
    public Delivery? Delivery { get; init; }

    /// <summary>Projects an invoice onto the common shape.</summary>
    public static DocumentView From(UblInvoice invoice) => new()
    {
        DocumentType = "Invoice",
        Id = invoice.Id?.Value ?? string.Empty,
        CustomizationId = invoice.CustomizationId,
        IssueDate = invoice.IssueDate,
        DueDate = invoice.DueDate,
        TypeCode = invoice.InvoiceTypeCode,
        CurrencyCode = invoice.DocumentCurrencyCode,
        Seller = invoice.AccountingSupplierParty?.Party ?? new Party(),
        Buyer = invoice.AccountingCustomerParty?.Party ?? new Party(),
        Lines = [.. invoice.InvoiceLines.Select((l, i) => new LineView
        {
            Index = i,
            Id = l.Id?.Value ?? string.Empty,
            Quantity = l.InvoicedQuantity,
            LineExtensionAmount = l.LineExtensionAmount,
            Item = l.Item,
            Price = l.Price,
            AllowanceCharges = l.AllowanceCharges,
            InvoicePeriod = l.InvoicePeriod,
        })],
        TaxTotals = invoice.TaxTotals,
        Totals = invoice.LegalMonetaryTotal ?? new MonetaryTotal(),
        AllowanceCharges = invoice.AllowanceCharges,
        PaymentTerms = invoice.PaymentTerms,
        InvoicePeriod = invoice.InvoicePeriod,
        Delivery = invoice.Delivery,
    };

    /// <summary>Projects a credit note onto the common shape.</summary>
    public static DocumentView From(UblCreditNote creditNote) => new()
    {
        DocumentType = "CreditNote",
        Id = creditNote.Id?.Value ?? string.Empty,
        CustomizationId = creditNote.CustomizationId,
        IssueDate = creditNote.IssueDate,
        // A credit note has no DueDate in UBL; BR-CO-25 is satisfied by payment terms instead.
        DueDate = null,
        TypeCode = creditNote.CreditNoteTypeCode,
        CurrencyCode = creditNote.DocumentCurrencyCode,
        Seller = creditNote.AccountingSupplierParty?.Party ?? new Party(),
        Buyer = creditNote.AccountingCustomerParty?.Party ?? new Party(),
        Lines = [.. creditNote.CreditNoteLines.Select((l, i) => new LineView
        {
            Index = i,
            Id = l.Id?.Value ?? string.Empty,
            Quantity = l.CreditedQuantity,
            LineExtensionAmount = l.LineExtensionAmount,
            Item = l.Item,
            Price = l.Price,
            AllowanceCharges = l.AllowanceCharges,
            InvoicePeriod = l.InvoicePeriod,
        })],
        TaxTotals = creditNote.TaxTotals,
        Totals = creditNote.LegalMonetaryTotal ?? new MonetaryTotal(),
        AllowanceCharges = creditNote.AllowanceCharges,
        PaymentTerms = creditNote.PaymentTerms,
        InvoicePeriod = creditNote.InvoicePeriod,
        Delivery = creditNote.Delivery,
    };
}

/// <summary>A document line, independent of whether it is invoiced or credited.</summary>
internal sealed record LineView
{
    /// <summary>Zero-based position, used to build a path for a finding.</summary>
    public required int Index { get; init; }

    /// <summary>Line identifier (BT-126).</summary>
    public required string Id { get; init; }

    /// <summary>Quantity (BT-129) and its unit code (BT-130).</summary>
    public Quantity? Quantity { get; init; }

    /// <summary>Line net amount (BT-131).</summary>
    public Amount? LineExtensionAmount { get; init; }

    /// <summary>The goods or service (BG-31).</summary>
    public Item? Item { get; init; }

    /// <summary>Unit price (BG-29).</summary>
    public Price? Price { get; init; }

    /// <summary>Line-level allowances and charges.</summary>
    public required IReadOnlyList<AllowanceCharge> AllowanceCharges { get; init; }

    /// <summary>Period the line covers (BG-26).</summary>
    public Period? InvoicePeriod { get; init; }

    /// <summary>A human-readable path to this line, for a finding.</summary>
    public string Path => $"Lines[{Index}]";
}
