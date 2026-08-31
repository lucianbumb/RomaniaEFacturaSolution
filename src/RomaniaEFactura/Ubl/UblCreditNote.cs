using System.Xml.Serialization;

namespace RomaniaEFactura.Ubl;

/// <summary>
/// A UBL 2.1 <c>CreditNote</c> constrained to CIUS-RO. Sent to ANAF with <c>standard=CN</c> and
/// validated as <c>FCN</c>.
/// </summary>
/// <remarks>
/// Structurally parallel to <see cref="UblInvoice"/>, but the schema is a separate type rather
/// than a subtype: the root namespace differs, the type code element is
/// <c>CreditNoteTypeCode</c>, there is no <c>DueDate</c>, and lines carry
/// <c>CreditedQuantity</c> instead of <c>InvoicedQuantity</c>. Property declaration order is the
/// XSD sequence order.
/// </remarks>
[XmlRoot("CreditNote", Namespace = UblNamespaces.CreditNote)]
public sealed class UblCreditNote
{
    /// <summary>UBL version (BT-23).</summary>
    [XmlElement("UBLVersionID", Namespace = UblNamespaces.Cbc)]
    public string UblVersionId { get; set; } = UblNamespaces.UblVersionId;

    /// <summary>Specification identifier (BT-24). BR-RO-001 requires the CIUS-RO value.</summary>
    [XmlElement("CustomizationID", Namespace = UblNamespaces.Cbc)]
    public string CustomizationId { get; set; } = UblNamespaces.CustomizationId;

    /// <summary>Business process type.</summary>
    [XmlElement("ProfileID", Namespace = UblNamespaces.Cbc)]
    public string? ProfileId { get; set; }

    /// <summary>Credit note number (BT-1).</summary>
    [XmlElement("ID", Namespace = UblNamespaces.Cbc)]
    public Identifier Id { get; set; } = new();

    /// <summary>Issue date (BT-2), serialized as <c>xs:date</c>.</summary>
    [XmlElement("IssueDate", Namespace = UblNamespaces.Cbc, DataType = "date")]
    public DateTime IssueDate { get; set; }

    /// <summary>Credit note type code (BT-3). <c>381</c> is a commercial credit note.</summary>
    [XmlElement("CreditNoteTypeCode", Namespace = UblNamespaces.Cbc)]
    public string CreditNoteTypeCode { get; set; } = "381";

    /// <summary>Free-text notes (BT-22).</summary>
    [XmlElement("Note", Namespace = UblNamespaces.Cbc)]
    public List<string> Notes { get; set; } = [];

    /// <summary>Value-added tax point date (BT-7).</summary>
    [XmlElement("TaxPointDate", Namespace = UblNamespaces.Cbc, DataType = "date")]
    public DateTime? TaxPointDate { get; set; }

    /// <summary>Indicates whether <see cref="TaxPointDate"/> is serialized.</summary>
    [XmlIgnore]
    public bool TaxPointDateSpecified => TaxPointDate.HasValue;

    /// <summary>Document currency (BT-5).</summary>
    [XmlElement("DocumentCurrencyCode", Namespace = UblNamespaces.Cbc)]
    public string DocumentCurrencyCode { get; set; } = "RON";

    /// <summary>VAT accounting currency (BT-6).</summary>
    [XmlElement("TaxCurrencyCode", Namespace = UblNamespaces.Cbc)]
    public string? TaxCurrencyCode { get; set; }

    /// <summary>Buyer accounting reference (BT-19).</summary>
    [XmlElement("AccountingCost", Namespace = UblNamespaces.Cbc)]
    public string? AccountingCost { get; set; }

    /// <summary>Buyer reference (BT-10).</summary>
    [XmlElement("BuyerReference", Namespace = UblNamespaces.Cbc)]
    public string? BuyerReference { get; set; }

    /// <summary>Period covered (BG-14).</summary>
    [XmlElement("InvoicePeriod", Namespace = UblNamespaces.Cac)]
    public Period? InvoicePeriod { get; set; }

    /// <summary>Purchase order reference (BT-13).</summary>
    [XmlElement("OrderReference", Namespace = UblNamespaces.Cac)]
    public OrderReference? OrderReference { get; set; }

    /// <summary>
    /// The invoice being credited (BG-3). Practically always required — a credit note that
    /// references nothing cannot be reconciled by the buyer.
    /// </summary>
    [XmlElement("BillingReference", Namespace = UblNamespaces.Cac)]
    public List<BillingReference> BillingReferences { get; set; } = [];

    /// <summary>Supporting documents (BG-24).</summary>
    [XmlElement("AdditionalDocumentReference", Namespace = UblNamespaces.Cac)]
    public List<DocumentReference> AdditionalDocumentReferences { get; set; } = [];

    /// <summary>The seller (BG-4).</summary>
    [XmlElement("AccountingSupplierParty", Namespace = UblNamespaces.Cac)]
    public PartyWrapper AccountingSupplierParty { get; set; } = new();

    /// <summary>The buyer (BG-7).</summary>
    [XmlElement("AccountingCustomerParty", Namespace = UblNamespaces.Cac)]
    public PartyWrapper AccountingCustomerParty { get; set; } = new();

    /// <summary>
    /// The seller's tax representative (BG-11).
    /// </summary>
    /// <remarks>
    /// A company selling into Romania without being established there appoints a fiscal
    /// representative, and the invoice must name them. CIUS-RO adds four rules of its own for the
    /// representative's address — BR-RO-140, 150, 160 and 170 — which are the same demands it makes
    /// of the seller and buyer addresses, including the Bucharest sector rule.
    /// </remarks>
    [XmlElement("TaxRepresentativeParty", Namespace = UblNamespaces.Cac)]
    public Party? TaxRepresentativeParty { get; set; }

    /// <summary>Delivery information (BG-13).</summary>
    [XmlElement("Delivery", Namespace = UblNamespaces.Cac)]
    public Delivery? Delivery { get; set; }

    /// <summary>Payment instructions (BG-16).</summary>
    [XmlElement("PaymentMeans", Namespace = UblNamespaces.Cac)]
    public List<PaymentMeans> PaymentMeans { get; set; } = [];

    /// <summary>Payment terms (BT-20).</summary>
    [XmlElement("PaymentTerms", Namespace = UblNamespaces.Cac)]
    public PaymentTerms? PaymentTerms { get; set; }

    /// <summary>Document-level allowances and charges (BG-20 / BG-21).</summary>
    [XmlElement("AllowanceCharge", Namespace = UblNamespaces.Cac)]
    public List<AllowanceCharge> AllowanceCharges { get; set; } = [];

    /// <summary>VAT breakdown (BG-22).</summary>
    [XmlElement("TaxTotal", Namespace = UblNamespaces.Cac)]
    public List<TaxTotal> TaxTotals { get; set; } = [];

    /// <summary>Document totals (BG-22).</summary>
    [XmlElement("LegalMonetaryTotal", Namespace = UblNamespaces.Cac)]
    public MonetaryTotal LegalMonetaryTotal { get; set; } = new();

    /// <summary>Credit note lines (BG-25).</summary>
    [XmlElement("CreditNoteLine", Namespace = UblNamespaces.Cac)]
    public List<CreditNoteLine> CreditNoteLines { get; set; } = [];
}

/// <summary>A single credit note line (BG-25).</summary>
public sealed class CreditNoteLine
{
    /// <summary>Line identifier (BT-126).</summary>
    [XmlElement("ID", Namespace = UblNamespaces.Cbc)]
    public Identifier Id { get; set; } = new();

    /// <summary>Free-text note for the line (BT-127).</summary>
    [XmlElement("Note", Namespace = UblNamespaces.Cbc)]
    public string? Note { get; set; }

    /// <summary>Quantity credited (BT-129).</summary>
    [XmlElement("CreditedQuantity", Namespace = UblNamespaces.Cbc)]
    public Quantity CreditedQuantity { get; set; } = new();

    /// <summary>Line net amount (BT-131).</summary>
    [XmlElement("LineExtensionAmount", Namespace = UblNamespaces.Cbc)]
    public Amount LineExtensionAmount { get; set; } = new();

    /// <summary>Buyer accounting reference for the line (BT-133).</summary>
    [XmlElement("AccountingCost", Namespace = UblNamespaces.Cbc)]
    public string? AccountingCost { get; set; }

    /// <summary>Period the line covers (BG-26).</summary>
    [XmlElement("InvoicePeriod", Namespace = UblNamespaces.Cac)]
    public Period? InvoicePeriod { get; set; }

    /// <summary>Line-level allowances and charges (BG-27 / BG-28).</summary>
    [XmlElement("AllowanceCharge", Namespace = UblNamespaces.Cac)]
    public List<AllowanceCharge> AllowanceCharges { get; set; } = [];

    /// <summary>The goods or service (BG-31).</summary>
    [XmlElement("Item", Namespace = UblNamespaces.Cac)]
    public Item Item { get; set; } = new();

    /// <summary>The unit price (BG-29).</summary>
    [XmlElement("Price", Namespace = UblNamespaces.Cac)]
    public Price Price { get; set; } = new();
}
