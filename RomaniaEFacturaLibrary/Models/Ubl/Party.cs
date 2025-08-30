using System.Xml.Serialization;

namespace RomaniaEFacturaLibrary.Models.Ubl;

/// <summary>
/// Party information (Supplier or Customer)
/// </summary>
public class Party
{
    [XmlElement("PartyIdentification", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public List<PartyIdentification> PartyIdentifications { get; set; } = new();
    
    [XmlElement("PartyName", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public PartyName? PartyName { get; set; }
    
    [XmlElement("PostalAddress", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public PostalAddress? PostalAddress { get; set; }
    
    [XmlElement("PartyTaxScheme", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public List<PartyTaxScheme> PartyTaxSchemes { get; set; } = new();
    
    [XmlElement("PartyLegalEntity", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public PartyLegalEntity? PartyLegalEntity { get; set; }
    
    [XmlElement("Contact", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public Contact? Contact { get; set; }
}

public class PartyIdentification
{
    [XmlElement("ID", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string Id { get; set; } = string.Empty;
}

public class PartyName
{
    [XmlElement("Name", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string Name { get; set; } = string.Empty;
}

public class PostalAddress
{
    [XmlElement("StreetName", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string StreetName { get; set; } = string.Empty;
    
    [XmlElement("CityName", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string CityName { get; set; } = string.Empty;
    
    [XmlElement("PostalZone", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string? PostalZone { get; set; }
    
    [XmlElement("CountrySubentity", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string? CountrySubentity { get; set; }
    
    [XmlElement("Country", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public Country? Country { get; set; }
}

public class Country
{
    [XmlElement("IdentificationCode", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string IdentificationCode { get; set; } = "RO";
}

public class PartyTaxScheme
{
    [XmlElement("CompanyID", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string CompanyId { get; set; } = string.Empty;
    
    [XmlElement("TaxScheme", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public TaxScheme? TaxScheme { get; set; }
}

public class TaxScheme
{
    [XmlElement("ID", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string Id { get; set; } = "VAT";
}

public class PartyLegalEntity
{
    [XmlElement("RegistrationName", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string RegistrationName { get; set; } = string.Empty;
    
    [XmlElement("CompanyID", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string CompanyId { get; set; } = string.Empty;
    
    [XmlElement("CompanyLegalForm", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string? CompanyLegalForm { get; set; }
}

public class Contact
{
    [XmlElement("Name", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string? Name { get; set; }
    
    [XmlElement("Telephone", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string? Telephone { get; set; }
    
    [XmlElement("ElectronicMail", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string? ElectronicMail { get; set; }
}
