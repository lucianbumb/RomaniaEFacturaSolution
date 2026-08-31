using System.Xml.Serialization;

namespace RomaniaEFactura.Ubl;

/// <summary>
/// A UBL 2.1 <c>Invoice</c> constrained to CIUS-RO. Sent to ANAF with <c>standard=UBL</c> and
/// validated as <c>FACT1</c>.
/// </summary>
/// <remarks>
/// Property declaration order is the XSD sequence order; see the note in Components.cs.
/// </remarks>
[XmlRoot("Invoice", Namespace = UblNamespaces.Invoice)]
public sealed class UblInvoice
{
    /// <summary>UBL version (BT-23).</summary>
    [XmlElement("UBLVersionID", Namespace = UblNamespaces.Cbc)]
    public string UblVersionId { get; set; } = UblNamespaces.UblVersionId;

    /// <summary>Specification identifier (BT-24). BR-RO-001 requires the CIUS-RO value.</summary>
    [XmlElement("CustomizationID", Namespace = UblNamespaces.Cbc)]
    public string CustomizationId { get; set; } = UblNamespaces.CustomizationId;

    /// <summary>Business process type (BT-23).</summary>
    [XmlElement("ProfileID", Namespace = UblNamespaces.Cbc)]
    public string? ProfileId { get; set; }

    /// <summary>Invoice number (BT-1).</summary>
    [XmlElement("ID", Namespace = UblNamespaces.Cbc)]
    public Identifier Id { get; set; } = new();

    /// <summary>
    /// Issue date (BT-2). Declared as <c>xs:date</c> — without this the serializer emits a full
    /// dateTime such as <c>2026-08-31T00:00:00</c>, which the UBL schema rejects.
    /// </summary>
    [XmlElement("IssueDate", Namespace = UblNamespaces.Cbc, DataType = "date")]
    public DateTime IssueDate { get; set; }

    /// <summary>Payment due date (BT-9).</summary>
    [XmlElement("DueDate", Namespace = UblNamespaces.Cbc, DataType = "date")]
    public DateTime? DueDate { get; set; }

    /// <summary>Indicates whether <see cref="DueDate"/> is serialized.</summary>
    [XmlIgnore]
    public bool DueDateSpecified => DueDate.HasValue;

    /// <summary>Invoice type code (BT-3). <c>380</c> is a commercial invoice.</summary>
    [XmlElement("InvoiceTypeCode", Namespace = UblNamespaces.Cbc)]
    public string InvoiceTypeCode { get; set; } = "380";

    /// <summary>Free-text notes (BT-22).</summary>
    [XmlElement("Note", Namespace = UblNamespaces.Cbc)]
    public List<string> Notes { get; set; } = [];

    /// <summary>Value-added tax point date (BT-7).</summary>
    [XmlElement("TaxPointDate", Namespace = UblNamespaces.Cbc, DataType = "date")]
    public DateTime? TaxPointDate { get; set; }

    /// <summary>Indicates whether <see cref="TaxPointDate"/> is serialized.</summary>
    [XmlIgnore]
    public bool TaxPointDateSpecified => TaxPointDate.HasValue;

    /// <summary>Invoice currency (BT-5).</summary>
    [XmlElement("DocumentCurrencyCode", Namespace = UblNamespaces.Cbc)]
    public string DocumentCurrencyCode { get; set; } = "RON";

    /// <summary>VAT accounting currency (BT-6).</summary>
    [XmlElement("TaxCurrencyCode", Namespace = UblNamespaces.Cbc)]
    public string? TaxCurrencyCode { get; set; }

    /// <summary>Buyer accounting reference (BT-19).</summary>
    [XmlElement("AccountingCost", Namespace = UblNamespaces.Cbc)]
    public string? AccountingCost { get; set; }

    /// <summary>Buyer reference (BT-10), often mandated for public-sector buyers.</summary>
    [XmlElement("BuyerReference", Namespace = UblNamespaces.Cbc)]
    public string? BuyerReference { get; set; }

    /// <summary>Invoicing period (BG-14).</summary>
    [XmlElement("InvoicePeriod", Namespace = UblNamespaces.Cac)]
    public Period? InvoicePeriod { get; set; }

    /// <summary>Purchase order reference (BT-13).</summary>
    [XmlElement("OrderReference", Namespace = UblNamespaces.Cac)]
    public OrderReference? OrderReference { get; set; }

    /// <summary>Preceding invoice references (BG-3), required when correcting an invoice.</summary>
    [XmlElement("BillingReference", Namespace = UblNamespaces.Cac)]
    public List<BillingReference> BillingReferences { get; set; } = [];

    /// <summary>Supporting documents and attachments (BG-24).</summary>
    [XmlElement("AdditionalDocumentReference", Namespace = UblNamespaces.Cac)]
    public List<DocumentReference> AdditionalDocumentReferences { get; set; } = [];

    /// <summary>The seller (BG-4).</summary>
    [XmlElement("AccountingSupplierParty", Namespace = UblNamespaces.Cac)]
    public PartyWrapper AccountingSupplierParty { get; set; } = new();

    /// <summary>The buyer (BG-7).</summary>
    [XmlElement("AccountingCustomerParty", Namespace = UblNamespaces.Cac)]
    public PartyWrapper AccountingCustomerParty { get; set; } = new();

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

    /// <summary>Invoice lines (BG-25).</summary>
    [XmlElement("InvoiceLine", Namespace = UblNamespaces.Cac)]
    public List<InvoiceLine> InvoiceLines { get; set; } = [];
}

/// <summary>A single invoice line (BG-25).</summary>
public sealed class InvoiceLine
{
    /// <summary>Line identifier (BT-126).</summary>
    [XmlElement("ID", Namespace = UblNamespaces.Cbc)]
    public Identifier Id { get; set; } = new();

    /// <summary>Free-text note for the line (BT-127).</summary>
    [XmlElement("Note", Namespace = UblNamespaces.Cbc)]
    public string? Note { get; set; }

    /// <summary>Quantity invoiced (BT-129).</summary>
    [XmlElement("InvoicedQuantity", Namespace = UblNamespaces.Cbc)]
    public Quantity InvoicedQuantity { get; set; } = new();

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

/// <summary>A reference to a purchase order (BT-13).</summary>
public sealed class OrderReference
{
    /// <summary>The order number.</summary>
    [XmlElement("ID", Namespace = UblNamespaces.Cbc)]
    public Identifier Id { get; set; } = new();
}

/// <summary>A reference to a preceding invoice (BG-3).</summary>
public sealed class BillingReference
{
    /// <summary>The referenced document.</summary>
    [XmlElement("InvoiceDocumentReference", Namespace = UblNamespaces.Cac)]
    public DocumentReference InvoiceDocumentReference { get; set; } = new();
}

/// <summary>A reference to another document (BG-24).</summary>
public sealed class DocumentReference
{
    /// <summary>The document identifier.</summary>
    [XmlElement("ID", Namespace = UblNamespaces.Cbc)]
    public Identifier Id { get; set; } = new();

    /// <summary>The issue date of the referenced document.</summary>
    /// <remarks>
    /// Backed by <see cref="IssueDateValue"/> rather than carrying the mapping itself:
    /// <see cref="XmlSerializer"/> reconciles <c>cbc:IssueDate</c> across the whole namespace and
    /// cannot map it to both <see cref="DateTime"/> (on the documents, where it is mandatory) and
    /// <see cref="Nullable{T}"/> here, where it is not.
    /// </remarks>
    [XmlIgnore]
    public DateTime? IssueDate { get; set; }

    /// <summary>Serialization surrogate for <see cref="IssueDate"/>. Use <see cref="IssueDate"/> instead.</summary>
    [XmlElement("IssueDate", Namespace = UblNamespaces.Cbc, DataType = "date")]
    public DateTime IssueDateValue
    {
        get => IssueDate ?? default;
        set => IssueDate = value;
    }

    /// <summary>Indicates whether <see cref="IssueDateValue"/> is serialized.</summary>
    [XmlIgnore]
    public bool IssueDateValueSpecified => IssueDate.HasValue;

    /// <summary>Description of the referenced document (BT-123).</summary>
    [XmlElement("DocumentDescription", Namespace = UblNamespaces.Cbc)]
    public string? DocumentDescription { get; set; }
}

/// <summary>Delivery details (BG-13).</summary>
public sealed class Delivery
{
    /// <summary>Actual delivery date (BT-72).</summary>
    [XmlElement("ActualDeliveryDate", Namespace = UblNamespaces.Cbc, DataType = "date")]
    public DateTime? ActualDeliveryDate { get; set; }

    /// <summary>Indicates whether <see cref="ActualDeliveryDate"/> is serialized.</summary>
    [XmlIgnore]
    public bool ActualDeliveryDateSpecified => ActualDeliveryDate.HasValue;

    /// <summary>Where the goods were delivered (BG-15).</summary>
    [XmlElement("DeliveryLocation", Namespace = UblNamespaces.Cac)]
    public DeliveryLocation? DeliveryLocation { get; set; }
}

/// <summary>A delivery location (BG-15).</summary>
public sealed class DeliveryLocation
{
    /// <summary>Location identifier (BT-71).</summary>
    [XmlElement("ID", Namespace = UblNamespaces.Cbc)]
    public Identifier? Id { get; set; }

    /// <summary>The delivery address.</summary>
    [XmlElement("Address", Namespace = UblNamespaces.Cac)]
    public PostalAddress? Address { get; set; }
}

/// <summary>Payment terms in words (BT-20).</summary>
public sealed class PaymentTerms
{
    /// <summary>The terms.</summary>
    [XmlElement("Note", Namespace = UblNamespaces.Cbc)]
    public string Note { get; set; } = string.Empty;
}
