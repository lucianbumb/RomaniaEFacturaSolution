using System.Xml.Serialization;

namespace RomaniaEFactura.Ubl;

/// <summary>
/// A UBL 2.1 <c>DebitNote</c>. <b>Receive-side only</b> — this document can be read from a
/// downloaded archive and rendered to PDF, but it cannot be sent to e-Factura.
/// </summary>
/// <remarks>
/// <para>
/// Two independent facts make debit notes inbound-only, both verified against ANAF rather than
/// inferred. The <c>upload</c> endpoint accepts only <c>UBL</c>, <c>CN</c>, <c>CII</c> and
/// <c>RASP</c> for its <c>standard</c> parameter, so there is no way to submit one; and ANAF's
/// offline validator accepts only <c>FACT1</c> and <c>FCN</c> for its document type, so one
/// cannot be validated either.
/// </para>
/// <para>
/// There is deliberately no <c>Verify</c> overload for this type and no way to pass it to a send
/// operation: a document that cannot be submitted should not be expressible on the send path. It
/// is still needed because <c>descarcare</c> can return one, and <c>transformare/FDN</c> renders
/// it to PDF.
/// </para>
/// </remarks>
[XmlRoot("DebitNote", Namespace = UblNamespaces.DebitNote)]
public sealed class UblDebitNote
{
    /// <summary>UBL version (BT-23).</summary>
    [XmlElement("UBLVersionID", Namespace = UblNamespaces.Cbc)]
    public string? UblVersionId { get; set; }

    /// <summary>Specification identifier (BT-24).</summary>
    [XmlElement("CustomizationID", Namespace = UblNamespaces.Cbc)]
    public string? CustomizationId { get; set; }

    /// <summary>Business process type.</summary>
    [XmlElement("ProfileID", Namespace = UblNamespaces.Cbc)]
    public string? ProfileId { get; set; }

    /// <summary>Debit note number (BT-1).</summary>
    [XmlElement("ID", Namespace = UblNamespaces.Cbc)]
    public Identifier Id { get; set; } = new();

    /// <summary>Issue date (BT-2).</summary>
    [XmlElement("IssueDate", Namespace = UblNamespaces.Cbc, DataType = "date")]
    public DateTime IssueDate { get; set; }

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

    /// <summary>The document being debited (BG-3).</summary>
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

    /// <summary>Payment instructions (BG-16).</summary>
    [XmlElement("PaymentMeans", Namespace = UblNamespaces.Cac)]
    public List<PaymentMeans> PaymentMeans { get; set; } = [];

    /// <summary>Payment terms (BT-20).</summary>
    [XmlElement("PaymentTerms", Namespace = UblNamespaces.Cac)]
    public PaymentTerms? PaymentTerms { get; set; }

    /// <summary>Document-level allowances and charges.</summary>
    [XmlElement("AllowanceCharge", Namespace = UblNamespaces.Cac)]
    public List<AllowanceCharge> AllowanceCharges { get; set; } = [];

    /// <summary>VAT breakdown (BG-22).</summary>
    [XmlElement("TaxTotal", Namespace = UblNamespaces.Cac)]
    public List<TaxTotal> TaxTotals { get; set; } = [];

    /// <summary>Document totals (BG-22).</summary>
    [XmlElement("RequestedMonetaryTotal", Namespace = UblNamespaces.Cac)]
    public MonetaryTotal RequestedMonetaryTotal { get; set; } = new();

    /// <summary>Debit note lines (BG-25).</summary>
    [XmlElement("DebitNoteLine", Namespace = UblNamespaces.Cac)]
    public List<DebitNoteLine> DebitNoteLines { get; set; } = [];
}

/// <summary>A single debit note line (BG-25).</summary>
public sealed class DebitNoteLine
{
    /// <summary>Line identifier (BT-126).</summary>
    [XmlElement("ID", Namespace = UblNamespaces.Cbc)]
    public Identifier Id { get; set; } = new();

    /// <summary>Free-text note for the line (BT-127).</summary>
    [XmlElement("Note", Namespace = UblNamespaces.Cbc)]
    public string? Note { get; set; }

    /// <summary>Quantity debited (BT-129).</summary>
    [XmlElement("DebitedQuantity", Namespace = UblNamespaces.Cbc)]
    public Quantity DebitedQuantity { get; set; } = new();

    /// <summary>Line net amount (BT-131).</summary>
    [XmlElement("LineExtensionAmount", Namespace = UblNamespaces.Cbc)]
    public Amount LineExtensionAmount { get; set; } = new();

    /// <summary>Buyer accounting reference for the line (BT-133).</summary>
    [XmlElement("AccountingCost", Namespace = UblNamespaces.Cbc)]
    public string? AccountingCost { get; set; }

    /// <summary>Period the line covers (BG-26).</summary>
    [XmlElement("InvoicePeriod", Namespace = UblNamespaces.Cac)]
    public Period? InvoicePeriod { get; set; }

    /// <summary>Line-level allowances and charges.</summary>
    [XmlElement("AllowanceCharge", Namespace = UblNamespaces.Cac)]
    public List<AllowanceCharge> AllowanceCharges { get; set; } = [];

    /// <summary>The goods or service (BG-31).</summary>
    [XmlElement("Item", Namespace = UblNamespaces.Cac)]
    public Item Item { get; set; } = new();

    /// <summary>The unit price (BG-29).</summary>
    [XmlElement("Price", Namespace = UblNamespaces.Cac)]
    public Price Price { get; set; } = new();
}
