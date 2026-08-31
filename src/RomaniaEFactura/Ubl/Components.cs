using System.Xml.Serialization;

namespace RomaniaEFactura.Ubl;

// Element declaration order in these types IS the XSD sequence order. XmlSerializer emits members
// in declaration order, and UBL's schema is a strict xs:sequence, so reordering a property is a
// breaking change that produces schema-invalid XML. Add new members at their schema position.

/// <summary>A monetary amount together with its currency (UBL <c>AmountType</c>).</summary>
public sealed class Amount
{
    /// <summary>Creates an empty amount. Required by <see cref="XmlSerializer"/>.</summary>
    public Amount() { }

    /// <summary>Creates an amount in the given currency.</summary>
    public Amount(decimal value, string currencyId = "RON")
    {
        Value = value;
        CurrencyId = currencyId;
    }

    /// <summary>ISO 4217 currency code.</summary>
    [XmlAttribute("currencyID")]
    public string CurrencyId { get; set; } = "RON";

    /// <summary>
    /// The numeric value. Serialized by <see cref="System.Xml.XmlConvert"/>, which is culture
    /// invariant and preserves the decimal's scale, so 100.00m emits as <c>100.00</c>.
    /// Decimal-place limits are enforced by the validation rules, not here.
    /// </summary>
    [XmlText]
    public decimal Value { get; set; }
}

/// <summary>A quantity together with its unit of measure (UBL <c>QuantityType</c>).</summary>
public sealed class Quantity
{
    /// <summary>Creates an empty quantity. Required by <see cref="XmlSerializer"/>.</summary>
    public Quantity() { }

    /// <summary>Creates a quantity with the given UN/ECE Recommendation 20 unit code.</summary>
    public Quantity(decimal value, string unitCode = "H87")
    {
        Value = value;
        UnitCode = unitCode;
    }

    /// <summary>UN/ECE Recommendation 20 unit code. <c>H87</c> is "piece".</summary>
    [XmlAttribute("unitCode")]
    public string UnitCode { get; set; } = "H87";

    /// <summary>The numeric value.</summary>
    [XmlText]
    public decimal Value { get; set; }
}

/// <summary>An identifier that may carry a scheme (UBL <c>IdentifierType</c>).</summary>
public sealed class Identifier : IEquatable<Identifier>
{
    /// <summary>Creates an empty identifier. Required by <see cref="XmlSerializer"/>.</summary>
    public Identifier() { }

    /// <summary>Creates an identifier, optionally qualified by a scheme.</summary>
    public Identifier(string value, string? schemeId = null)
    {
        Value = value;
        SchemeId = schemeId;
    }

    /// <summary>The scheme the identifier belongs to, when one applies.</summary>
    [XmlAttribute("schemeID")]
    public string? SchemeId { get; set; }

    /// <summary>The identifier itself.</summary>
    [XmlText]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Allows a bare string wherever an identifier is expected.
    /// </summary>
    /// <remarks>
    /// Every <c>cbc:ID</c> and <c>cbc:CompanyID</c> in the model is typed as
    /// <see cref="Identifier"/>, because <see cref="XmlSerializer"/> reconciles an element name
    /// across the whole namespace and refuses to map <c>cbc:ID</c> to both string and a complex
    /// type. This conversion keeps that uniformity from leaking into calling code.
    /// </remarks>
    public static implicit operator Identifier(string value) => new(value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Two identifiers are equal when both their value and their scheme match.</summary>
    public bool Equals(Identifier? other) =>
        other is not null
        && string.Equals(Value, other.Value, StringComparison.Ordinal)
        && string.Equals(SchemeId, other.SchemeId, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Identifier);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Value, SchemeId);

    /// <summary>Compares two identifiers for equality.</summary>
    public static bool operator ==(Identifier? left, Identifier? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Compares two identifiers for inequality.</summary>
    public static bool operator !=(Identifier? left, Identifier? right) => !(left == right);
}

/// <summary>A postal address (BG-5 / BG-8).</summary>
public sealed class PostalAddress
{
    /// <summary>Street name and number (BT-35).</summary>
    [XmlElement("StreetName", Namespace = UblNamespaces.Cbc)]
    public string? StreetName { get; set; }

    /// <summary>Additional address line (BT-36).</summary>
    [XmlElement("AdditionalStreetName", Namespace = UblNamespaces.Cbc)]
    public string? AdditionalStreetName { get; set; }

    /// <summary>City (BT-37).</summary>
    [XmlElement("CityName", Namespace = UblNamespaces.Cbc)]
    public string? CityName { get; set; }

    /// <summary>Post code (BT-38).</summary>
    [XmlElement("PostalZone", Namespace = UblNamespaces.Cbc)]
    public string? PostalZone { get; set; }

    /// <summary>
    /// County subdivision (BT-39). CIUS-RO requires an ISO 3166-2:RO code such as
    /// <c>RO-B</c> or <c>RO-AR</c> for Romanian addresses.
    /// </summary>
    [XmlElement("CountrySubentity", Namespace = UblNamespaces.Cbc)]
    public string? CountrySubentity { get; set; }

    /// <summary>
    /// A third address line (BT-162 / BT-163 / BT-165).
    /// </summary>
    /// <remarks>
    /// UBL puts lines one and two in their own elements and the third in a nested
    /// <c>cac:AddressLine</c>, which is why this looks unlike its two siblings.
    /// </remarks>
    [XmlElement("AddressLine", Namespace = UblNamespaces.Cac)]
    public AddressLine? AddressLine { get; set; }

    /// <summary>Country (BT-40 / BT-55).</summary>
    [XmlElement("Country", Namespace = UblNamespaces.Cac)]
    public Country? Country { get; set; }
}

/// <summary>The third line of an address, which UBL nests rather than naming directly.</summary>
public sealed class AddressLine
{
    /// <summary>The line itself.</summary>
    [XmlElement("Line", Namespace = UblNamespaces.Cbc)]
    public string Line { get; set; } = string.Empty;
}

/// <summary>A country reference.</summary>
public sealed class Country
{
    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    [XmlElement("IdentificationCode", Namespace = UblNamespaces.Cbc)]
    public string IdentificationCode { get; set; } = "RO";
}

/// <summary>A tax scheme reference. For VAT this is always <c>VAT</c>.</summary>
public sealed class TaxScheme
{
    /// <summary>The scheme identifier.</summary>
    [XmlElement("ID", Namespace = UblNamespaces.Cbc)]
    public Identifier Id { get; set; } = "VAT";
}

/// <summary>A party's registration in a tax scheme (BT-31 / BT-48).</summary>
public sealed class PartyTaxScheme
{
    /// <summary>The VAT identifier, including the country prefix.</summary>
    [XmlElement("CompanyID", Namespace = UblNamespaces.Cbc)]
    public Identifier CompanyId { get; set; } = new();

    /// <summary>The scheme the identifier belongs to.</summary>
    [XmlElement("TaxScheme", Namespace = UblNamespaces.Cac)]
    public TaxScheme TaxScheme { get; set; } = new();
}

/// <summary>A party's legal registration (BG-6 / BG-9).</summary>
public sealed class PartyLegalEntity
{
    /// <summary>Legal registration name (BT-27 / BT-44).</summary>
    [XmlElement("RegistrationName", Namespace = UblNamespaces.Cbc)]
    public string? RegistrationName { get; set; }

    /// <summary>
    /// Legal registration identifier (BT-30 / BT-47). ANAF's validator rejects a document whose
    /// buyer or seller cannot be identified, and checks the Romanian CIF control digit.
    /// </summary>
    [XmlElement("CompanyID", Namespace = UblNamespaces.Cbc)]
    public Identifier? CompanyId { get; set; }

    /// <summary>Additional legal information (BT-33).</summary>
    [XmlElement("CompanyLegalForm", Namespace = UblNamespaces.Cbc)]
    public string? CompanyLegalForm { get; set; }
}

/// <summary>Contact details for a party (BG-6 / BG-9).</summary>
public sealed class Contact
{
    /// <summary>Contact point name (BT-41 / BT-56).</summary>
    [XmlElement("Name", Namespace = UblNamespaces.Cbc)]
    public string? Name { get; set; }

    /// <summary>Telephone (BT-42 / BT-57).</summary>
    [XmlElement("Telephone", Namespace = UblNamespaces.Cbc)]
    public string? Telephone { get; set; }

    /// <summary>Email (BT-43 / BT-58).</summary>
    [XmlElement("ElectronicMail", Namespace = UblNamespaces.Cbc)]
    public string? ElectronicMail { get; set; }
}

/// <summary>A trading party — the seller (BG-4) or the buyer (BG-7).</summary>
public sealed class Party
{
    /// <summary>Electronic address (BT-34 / BT-49).</summary>
    [XmlElement("EndpointID", Namespace = UblNamespaces.Cbc)]
    public Identifier? EndpointId { get; set; }

    /// <summary>Party identifier (BT-29 / BT-46).</summary>
    [XmlElement("PartyIdentification", Namespace = UblNamespaces.Cac)]
    public List<PartyIdentification> PartyIdentifications { get; set; } = [];

    /// <summary>Trading name (BT-28 / BT-45).</summary>
    [XmlElement("PartyName", Namespace = UblNamespaces.Cac)]
    public PartyName? PartyName { get; set; }

    /// <summary>Postal address.</summary>
    [XmlElement("PostalAddress", Namespace = UblNamespaces.Cac)]
    public PostalAddress? PostalAddress { get; set; }

    /// <summary>VAT registrations.</summary>
    [XmlElement("PartyTaxScheme", Namespace = UblNamespaces.Cac)]
    public List<PartyTaxScheme> PartyTaxSchemes { get; set; } = [];

    /// <summary>Legal registration.</summary>
    [XmlElement("PartyLegalEntity", Namespace = UblNamespaces.Cac)]
    public PartyLegalEntity? PartyLegalEntity { get; set; }

    /// <summary>Contact details.</summary>
    [XmlElement("Contact", Namespace = UblNamespaces.Cac)]
    public Contact? Contact { get; set; }
}

/// <summary>A party identifier.</summary>
public sealed class PartyIdentification
{
    /// <summary>The identifier.</summary>
    [XmlElement("ID", Namespace = UblNamespaces.Cbc)]
    public Identifier Id { get; set; } = new();
}

/// <summary>A party's trading name.</summary>
public sealed class PartyName
{
    /// <summary>The name.</summary>
    [XmlElement("Name", Namespace = UblNamespaces.Cbc)]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// The seller or buyer wrapper. This type exists because UBL nests the party one level deeper:
/// <c>AccountingSupplierParty</c> is a <c>SupplierParty</c> containing a <c>cac:Party</c>.
/// Mapping the party directly onto the wrapper produces schema-invalid XML.
/// </summary>
public sealed class PartyWrapper
{
    /// <summary>Creates an empty wrapper. Required by <see cref="XmlSerializer"/>.</summary>
    public PartyWrapper() { }

    /// <summary>Wraps the supplied party.</summary>
    public PartyWrapper(Party party) => Party = party;

    /// <summary>The wrapped party.</summary>
    [XmlElement("Party", Namespace = UblNamespaces.Cac)]
    public Party Party { get; set; } = new();
}

/// <summary>A date range, used for the invoicing period (BG-14).</summary>
public sealed class Period
{
    /// <summary>Start of the period (BT-73).</summary>
    [XmlElement("StartDate", Namespace = UblNamespaces.Cbc, DataType = "date")]
    public DateTime? StartDate { get; set; }

    /// <summary>Indicates whether <see cref="StartDate"/> is serialized.</summary>
    [XmlIgnore]
    public bool StartDateSpecified => StartDate.HasValue;

    /// <summary>End of the period (BT-74).</summary>
    [XmlElement("EndDate", Namespace = UblNamespaces.Cbc, DataType = "date")]
    public DateTime? EndDate { get; set; }

    /// <summary>Indicates whether <see cref="EndDate"/> is serialized.</summary>
    [XmlIgnore]
    public bool EndDateSpecified => EndDate.HasValue;
}

/// <summary>How payment is to be made (BG-16).</summary>
public sealed class PaymentMeans
{
    /// <summary>Payment means code (BT-81). <c>31</c> is credit transfer.</summary>
    [XmlElement("PaymentMeansCode", Namespace = UblNamespaces.Cbc)]
    public string PaymentMeansCode { get; set; } = "31";

    /// <summary>Remittance information (BT-83).</summary>
    [XmlElement("PaymentID", Namespace = UblNamespaces.Cbc)]
    public string? PaymentId { get; set; }

    /// <summary>The account to be credited (BG-17).</summary>
    [XmlElement("PayeeFinancialAccount", Namespace = UblNamespaces.Cac)]
    public FinancialAccount? PayeeFinancialAccount { get; set; }
}

/// <summary>A bank account.</summary>
public sealed class FinancialAccount
{
    /// <summary>IBAN (BT-84).</summary>
    [XmlElement("ID", Namespace = UblNamespaces.Cbc)]
    public Identifier Id { get; set; } = new();

    /// <summary>Account holder name (BT-85).</summary>
    [XmlElement("Name", Namespace = UblNamespaces.Cbc)]
    public string? Name { get; set; }
}

/// <summary>Total VAT for the document (BG-22).</summary>
public sealed class TaxTotal
{
    /// <summary>Total VAT amount (BT-110).</summary>
    [XmlElement("TaxAmount", Namespace = UblNamespaces.Cbc)]
    public Amount TaxAmount { get; set; } = new();

    /// <summary>VAT breakdown per category (BG-23).</summary>
    [XmlElement("TaxSubtotal", Namespace = UblNamespaces.Cac)]
    public List<TaxSubtotal> TaxSubtotals { get; set; } = [];
}

/// <summary>VAT for one category and rate (BG-23).</summary>
public sealed class TaxSubtotal
{
    /// <summary>Taxable amount for this category (BT-116).</summary>
    [XmlElement("TaxableAmount", Namespace = UblNamespaces.Cbc)]
    public Amount TaxableAmount { get; set; } = new();

    /// <summary>VAT amount for this category (BT-117).</summary>
    [XmlElement("TaxAmount", Namespace = UblNamespaces.Cbc)]
    public Amount TaxAmount { get; set; } = new();

    /// <summary>The category and rate.</summary>
    [XmlElement("TaxCategory", Namespace = UblNamespaces.Cac)]
    public TaxCategory TaxCategory { get; set; } = new();
}

/// <summary>A VAT category and rate.</summary>
public sealed class TaxCategory
{
    /// <summary>UNCL5305 category code (BT-118). <c>S</c> is standard rate.</summary>
    [XmlElement("ID", Namespace = UblNamespaces.Cbc)]
    public Identifier Id { get; set; } = "S";

    /// <summary>VAT rate as a percentage (BT-119).</summary>
    [XmlElement("Percent", Namespace = UblNamespaces.Cbc)]
    public decimal? Percent { get; set; }

    /// <summary>Indicates whether <see cref="Percent"/> is serialized.</summary>
    [XmlIgnore]
    public bool PercentSpecified => Percent.HasValue;

    /// <summary>Reason for exemption (BT-120).</summary>
    [XmlElement("TaxExemptionReason", Namespace = UblNamespaces.Cbc)]
    public string? TaxExemptionReason { get; set; }

    /// <summary>Coded reason for exemption (BT-121).</summary>
    [XmlElement("TaxExemptionReasonCode", Namespace = UblNamespaces.Cbc)]
    public string? TaxExemptionReasonCode { get; set; }

    /// <summary>The scheme the category belongs to.</summary>
    [XmlElement("TaxScheme", Namespace = UblNamespaces.Cac)]
    public TaxScheme TaxScheme { get; set; } = new();
}

/// <summary>Document-level totals (BG-22).</summary>
public sealed class MonetaryTotal
{
    /// <summary>Sum of line net amounts (BT-106).</summary>
    [XmlElement("LineExtensionAmount", Namespace = UblNamespaces.Cbc)]
    public Amount LineExtensionAmount { get; set; } = new();

    /// <summary>Total without VAT (BT-109).</summary>
    [XmlElement("TaxExclusiveAmount", Namespace = UblNamespaces.Cbc)]
    public Amount TaxExclusiveAmount { get; set; } = new();

    /// <summary>Total with VAT (BT-112).</summary>
    [XmlElement("TaxInclusiveAmount", Namespace = UblNamespaces.Cbc)]
    public Amount TaxInclusiveAmount { get; set; } = new();

    /// <summary>Sum of allowances (BT-107).</summary>
    [XmlElement("AllowanceTotalAmount", Namespace = UblNamespaces.Cbc)]
    public Amount? AllowanceTotalAmount { get; set; }

    /// <summary>Sum of charges (BT-108).</summary>
    [XmlElement("ChargeTotalAmount", Namespace = UblNamespaces.Cbc)]
    public Amount? ChargeTotalAmount { get; set; }

    /// <summary>Amount already paid (BT-113).</summary>
    [XmlElement("PrepaidAmount", Namespace = UblNamespaces.Cbc)]
    public Amount? PrepaidAmount { get; set; }

    /// <summary>Amount due for payment (BT-115).</summary>
    [XmlElement("PayableAmount", Namespace = UblNamespaces.Cbc)]
    public Amount PayableAmount { get; set; } = new();
}

/// <summary>An allowance or a charge (BG-20 / BG-21 at document level, BG-27 / BG-28 on a line).</summary>
public sealed class AllowanceCharge
{
    /// <summary><see langword="false"/> for an allowance, <see langword="true"/> for a charge.</summary>
    [XmlElement("ChargeIndicator", Namespace = UblNamespaces.Cbc)]
    public bool ChargeIndicator { get; set; }

    /// <summary>Coded reason (BT-98 / BT-105).</summary>
    [XmlElement("AllowanceChargeReasonCode", Namespace = UblNamespaces.Cbc)]
    public string? ReasonCode { get; set; }

    /// <summary>Reason in words (BT-97 / BT-104).</summary>
    [XmlElement("AllowanceChargeReason", Namespace = UblNamespaces.Cbc)]
    public string? Reason { get; set; }

    /// <summary>Percentage applied to <see cref="BaseAmount"/> (BT-94 / BT-101).</summary>
    [XmlElement("MultiplierFactorNumeric", Namespace = UblNamespaces.Cbc)]
    public decimal? MultiplierFactorNumeric { get; set; }

    /// <summary>Indicates whether <see cref="MultiplierFactorNumeric"/> is serialized.</summary>
    [XmlIgnore]
    public bool MultiplierFactorNumericSpecified => MultiplierFactorNumeric.HasValue;

    /// <summary>The allowance or charge amount (BT-92 / BT-99).</summary>
    [XmlElement("Amount", Namespace = UblNamespaces.Cbc)]
    public Amount Amount { get; set; } = new();

    /// <summary>The base the percentage applies to (BT-93 / BT-100).</summary>
    [XmlElement("BaseAmount", Namespace = UblNamespaces.Cbc)]
    public Amount? BaseAmount { get; set; }

    /// <summary>VAT category for the allowance or charge (document level only).</summary>
    [XmlElement("TaxCategory", Namespace = UblNamespaces.Cac)]
    public TaxCategory? TaxCategory { get; set; }
}

/// <summary>The goods or service on a line (BG-31).</summary>
public sealed class Item
{
    /// <summary>Item description (BT-154).</summary>
    [XmlElement("Description", Namespace = UblNamespaces.Cbc)]
    public string? Description { get; set; }

    /// <summary>Item name (BT-153).</summary>
    [XmlElement("Name", Namespace = UblNamespaces.Cbc)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Seller's item identifier (BT-155).</summary>
    [XmlElement("SellersItemIdentification", Namespace = UblNamespaces.Cac)]
    public ItemIdentification? SellersItemIdentification { get; set; }

    /// <summary>Standard item identifier (BT-157).</summary>
    [XmlElement("StandardItemIdentification", Namespace = UblNamespaces.Cac)]
    public ItemIdentification? StandardItemIdentification { get; set; }

    /// <summary>Item classification (BT-158).</summary>
    [XmlElement("CommodityClassification", Namespace = UblNamespaces.Cac)]
    public List<CommodityClassification> CommodityClassifications { get; set; } = [];

    /// <summary>VAT category applying to the item (BG-30).</summary>
    [XmlElement("ClassifiedTaxCategory", Namespace = UblNamespaces.Cac)]
    public LineTaxCategory ClassifiedTaxCategory { get; set; } = new();

    /// <summary>Item attributes (BG-32) — name and value pairs describing the goods.</summary>
    [XmlElement("AdditionalItemProperty", Namespace = UblNamespaces.Cac)]
    public List<ItemProperty> AdditionalItemProperties { get; set; } = [];
}

/// <summary>
/// One attribute of an item (BG-32): colour, size, a serial number.
/// </summary>
/// <remarks>
/// Both halves are mandatory — BR-54 — because an attribute with only a name says nothing and one
/// with only a value says nothing about what it is.
/// </remarks>
public sealed class ItemProperty
{
    /// <summary>What the attribute is (BT-160).</summary>
    [XmlElement("Name", Namespace = UblNamespaces.Cbc)]
    public string Name { get; set; } = string.Empty;

    /// <summary>What it is set to (BT-161).</summary>
    [XmlElement("Value", Namespace = UblNamespaces.Cbc)]
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// The VAT category on a line (BG-30). Deliberately narrower than <see cref="TaxCategory"/>.
/// </summary>
/// <remarks>
/// Rules UBL-CR-598 through UBL-CR-604 forbid a line's <c>ClassifiedTaxCategory</c> from carrying
/// a base unit measure, a per-unit amount, an exemption reason or reason code, tier information,
/// or a tax scheme name. The exemption reason belongs to the document-level VAT breakdown alone.
/// Omitting those members makes the mistake impossible to express rather than merely detectable —
/// worth doing because setting the reason in both places is a natural thing to try, and ANAF
/// rejects it outright even though the Schematron marks the rule a warning.
/// </remarks>
public sealed class LineTaxCategory
{
    /// <summary>UNCL5305 category code (BT-151). <c>S</c> is standard rate.</summary>
    [XmlElement("ID", Namespace = UblNamespaces.Cbc)]
    public Identifier Id { get; set; } = "S";

    /// <summary>VAT rate as a percentage (BT-152).</summary>
    [XmlElement("Percent", Namespace = UblNamespaces.Cbc)]
    public decimal? Percent { get; set; }

    /// <summary>Indicates whether <see cref="Percent"/> is serialized.</summary>
    [XmlIgnore]
    public bool PercentSpecified => Percent.HasValue;

    /// <summary>The scheme the category belongs to.</summary>
    [XmlElement("TaxScheme", Namespace = UblNamespaces.Cac)]
    public TaxScheme TaxScheme { get; set; } = new();
}

/// <summary>An item identifier.</summary>
public sealed class ItemIdentification
{
    /// <summary>The identifier.</summary>
    [XmlElement("ID", Namespace = UblNamespaces.Cbc)]
    public Identifier Id { get; set; } = new();
}

/// <summary>A classification code for an item.</summary>
public sealed class CommodityClassification
{
    /// <summary>The classification code.</summary>
    [XmlElement("ItemClassificationCode", Namespace = UblNamespaces.Cbc)]
    public Identifier ItemClassificationCode { get; set; } = new();
}

/// <summary>The unit price of an item (BG-29).</summary>
public sealed class Price
{
    /// <summary>
    /// Net unit price (BT-146). Unlike the document and line amounts, this is allowed more than
    /// two decimal places — ANAF's own example uses four.
    /// </summary>
    [XmlElement("PriceAmount", Namespace = UblNamespaces.Cbc)]
    public Amount PriceAmount { get; set; } = new();

    /// <summary>The quantity the price applies to (BT-149).</summary>
    [XmlElement("BaseQuantity", Namespace = UblNamespaces.Cbc)]
    public Quantity? BaseQuantity { get; set; }
}
