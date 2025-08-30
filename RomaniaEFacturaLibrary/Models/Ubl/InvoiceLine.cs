using System.Xml.Serialization;

namespace RomaniaEFacturaLibrary.Models.Ubl;

/// <summary>
/// Invoice line item
/// </summary>
public class InvoiceLine
{
    [XmlElement("ID", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string Id { get; set; } = string.Empty;
    
    [XmlElement("InvoicedQuantity", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public Quantity? InvoicedQuantity { get; set; }
    
    [XmlElement("LineExtensionAmount", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public Amount? LineExtensionAmount { get; set; }
    
    [XmlElement("AllowanceCharge", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public List<AllowanceCharge> AllowanceCharges { get; set; } = new();
    
    [XmlElement("Item", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public Item? Item { get; set; }
    
    [XmlElement("Price", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public Price? Price { get; set; }
}

/// <summary>
/// Quantity with unit code
/// </summary>
public class Quantity
{
    [XmlAttribute("unitCode")]
    public string UnitCode { get; set; } = "EA"; // Each
    
    [XmlText]
    public decimal Value { get; set; }
}

/// <summary>
/// Monetary amount with currency
/// </summary>
public class Amount
{
    [XmlAttribute("currencyID")]
    public string CurrencyId { get; set; } = "RON";
    
    [XmlText]
    public decimal Value { get; set; }
}

/// <summary>
/// Item information
/// </summary>
public class Item
{
    [XmlElement("Description", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string? Description { get; set; }
    
    [XmlElement("Name", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string Name { get; set; } = string.Empty;
    
    [XmlElement("SellersItemIdentification", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public ItemIdentification? SellersItemIdentification { get; set; }
    
    [XmlElement("ClassifiedTaxCategory", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public List<TaxCategory> ClassifiedTaxCategories { get; set; } = new();
}

/// <summary>
/// Item identification
/// </summary>
public class ItemIdentification
{
    [XmlElement("ID", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string Id { get; set; } = string.Empty;
}

/// <summary>
/// Price information
/// </summary>
public class Price
{
    [XmlElement("PriceAmount", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public Amount? PriceAmount { get; set; }
    
    [XmlElement("BaseQuantity", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public Quantity? BaseQuantity { get; set; }
}

/// <summary>
/// Allowance or charge
/// </summary>
public class AllowanceCharge
{
    [XmlElement("ChargeIndicator", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public bool ChargeIndicator { get; set; } // false = allowance, true = charge
    
    [XmlElement("AllowanceChargeReasonCode", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string? AllowanceChargeReasonCode { get; set; }
    
    [XmlElement("AllowanceChargeReason", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string? AllowanceChargeReason { get; set; }
    
    [XmlElement("Amount", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public Amount? Amount { get; set; }
    
    [XmlElement("BaseAmount", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public Amount? BaseAmount { get; set; }
    
    [XmlElement("MultiplierFactorNumeric", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public decimal? MultiplierFactorNumeric { get; set; }
    
    [XmlElement("TaxCategory", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public List<TaxCategory> TaxCategories { get; set; } = new();
}

/// <summary>
/// Tax category information
/// </summary>
public class TaxCategory
{
    [XmlElement("ID", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string Id { get; set; } = "S"; // Standard rate
    
    [XmlElement("Percent", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public decimal? Percent { get; set; }
    
    [XmlElement("TaxExemptionReasonCode", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string? TaxExemptionReasonCode { get; set; }
    
    [XmlElement("TaxExemptionReason", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string? TaxExemptionReason { get; set; }
    
    [XmlElement("TaxScheme", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public TaxScheme? TaxScheme { get; set; }
}
