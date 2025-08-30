using System.Xml.Serialization;

namespace RomaniaEFacturaLibrary.Models.Ubl;

/// <summary>
/// Payment means information
/// </summary>
public class PaymentMeans
{
    [XmlElement("PaymentMeansCode", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string PaymentMeansCode { get; set; } = "31"; // Credit transfer
    
    [XmlElement("PaymentID", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string? PaymentId { get; set; }
    
    [XmlElement("PayeeFinancialAccount", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public FinancialAccount? PayeeFinancialAccount { get; set; }
}

/// <summary>
/// Financial account information
/// </summary>
public class FinancialAccount
{
    [XmlElement("ID", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string Id { get; set; } = string.Empty; // IBAN
    
    [XmlElement("Name", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string? Name { get; set; }
    
    [XmlElement("FinancialInstitutionBranch", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public FinancialInstitutionBranch? FinancialInstitutionBranch { get; set; }
}

/// <summary>
/// Financial institution branch
/// </summary>
public class FinancialInstitutionBranch
{
    [XmlElement("ID", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string? Id { get; set; } // BIC
    
    [XmlElement("Name", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string? Name { get; set; }
}

/// <summary>
/// Tax total information
/// </summary>
public class TaxTotal
{
    [XmlElement("TaxAmount", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public Amount? TaxAmount { get; set; }
    
    [XmlElement("TaxSubtotal", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public List<TaxSubtotal> TaxSubtotals { get; set; } = new();
}

/// <summary>
/// Tax subtotal by category
/// </summary>
public class TaxSubtotal
{
    [XmlElement("TaxableAmount", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public Amount? TaxableAmount { get; set; }
    
    [XmlElement("TaxAmount", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public Amount? TaxAmount { get; set; }
    
    [XmlElement("TaxCategory", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public TaxCategory? TaxCategory { get; set; }
}

/// <summary>
/// Legal monetary total
/// </summary>
public class MonetaryTotal
{
    [XmlElement("LineExtensionAmount", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public Amount? LineExtensionAmount { get; set; }
    
    [XmlElement("TaxExclusiveAmount", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public Amount? TaxExclusiveAmount { get; set; }
    
    [XmlElement("TaxInclusiveAmount", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public Amount? TaxInclusiveAmount { get; set; }
    
    [XmlElement("AllowanceTotalAmount", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public Amount? AllowanceTotalAmount { get; set; }
    
    [XmlElement("ChargeTotalAmount", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public Amount? ChargeTotalAmount { get; set; }
    
    [XmlElement("PayableAmount", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public Amount? PayableAmount { get; set; }
}
