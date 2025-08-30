using System.Xml.Serialization;

namespace RomaniaEFacturaLibrary.Models.Ubl;

/// <summary>
/// UBL 2.1 Invoice root element
/// </summary>
[XmlRoot("Invoice", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2")]
public class UblInvoice
{
    [XmlNamespaceDeclarations]
    public XmlSerializerNamespaces Namespaces { get; set; } = new();
    
    [XmlElement("UBLVersionID", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string UblVersionId { get; set; } = "2.1";
    
    [XmlElement("CustomizationID", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string CustomizationId { get; set; } = "urn:cen.eu:en16931:2017#compliant#urn:efactura.mfinante.ro:CIUS-RO:1.0.1";
    
    [XmlElement("ProfileID", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string ProfileId { get; set; } = "urn:efactura.mfinante.ro:CIUS-RO:1.0.1";
    
    [XmlElement("ID", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string Id { get; set; } = string.Empty;
    
    [XmlElement("IssueDate", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public DateTime IssueDate { get; set; }
    
    [XmlElement("DueDate", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public DateTime? DueDate { get; set; }
    
    [XmlElement("InvoiceTypeCode", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string InvoiceTypeCode { get; set; } = "380"; // Commercial invoice
    
    [XmlElement("Note", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public List<string> Notes { get; set; } = new();
    
    [XmlElement("DocumentCurrencyCode", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string DocumentCurrencyCode { get; set; } = "RON";
    
    [XmlElement("TaxCurrencyCode", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2")]
    public string? TaxCurrencyCode { get; set; }
    
    [XmlElement("AccountingSupplierParty", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public Party? AccountingSupplierParty { get; set; }
    
    [XmlElement("AccountingCustomerParty", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public Party? AccountingCustomerParty { get; set; }
    
    [XmlElement("PaymentMeans", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public List<PaymentMeans> PaymentMeans { get; set; } = new();
    
    [XmlElement("TaxTotal", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public List<TaxTotal> TaxTotals { get; set; } = new();
    
    [XmlElement("LegalMonetaryTotal", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public MonetaryTotal? LegalMonetaryTotal { get; set; }
    
    [XmlElement("InvoiceLine", Namespace = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2")]
    public List<InvoiceLine> InvoiceLines { get; set; } = new();
    
    public UblInvoice()
    {
        Namespaces.Add("", "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2");
        Namespaces.Add("cac", "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2");
        Namespaces.Add("cbc", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2");
        Namespaces.Add("xsi", "http://www.w3.org/2001/XMLSchema-instance");
    }
}
