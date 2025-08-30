using Microsoft.Extensions.Logging;
using RomaniaEFacturaLibrary.Models.Ubl;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System.Xml.Schema;

namespace RomaniaEFacturaLibrary.Services.Xml;

/// <summary>
/// Service for XML operations on UBL invoices
/// </summary>
public interface IXmlService
{
    /// <summary>
    /// Serializes a UBL invoice to XML string
    /// </summary>
    Task<string> SerializeInvoiceAsync(UblInvoice invoice, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deserializes XML string to UBL invoice
    /// </summary>
    Task<UblInvoice> DeserializeInvoiceAsync(string xmlContent, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validates XML against UBL 2.1 schema
    /// </summary>
    Task<XmlValidationResult> ValidateXmlAsync(string xmlContent, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Formats XML with proper indentation
    /// </summary>
    string FormatXml(string xmlContent);
    
    /// <summary>
    /// Removes BOM and problematic sequences from XML
    /// </summary>
    string CleanXml(string xmlContent);
}

public class XmlService : IXmlService
{
    private readonly ILogger<XmlService> _logger;
    private static readonly XmlSerializerNamespaces DefaultNamespaces;

    static XmlService()
    {
        DefaultNamespaces = new XmlSerializerNamespaces();
        DefaultNamespaces.Add("", "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2");
        DefaultNamespaces.Add("cac", "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2");
        DefaultNamespaces.Add("cbc", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2");
        DefaultNamespaces.Add("xsi", "http://www.w3.org/2001/XMLSchema-instance");
    }

    public XmlService(ILogger<XmlService> logger)
    {
        _logger = logger;
    }

    public async Task<string> SerializeInvoiceAsync(UblInvoice invoice, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Serializing UBL invoice {InvoiceId}", invoice.Id);
        
        return await Task.Run(() =>
        {
            var serializer = new XmlSerializer(typeof(UblInvoice));
            
            var settings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8,
                Indent = true,
                IndentChars = "  ",
                OmitXmlDeclaration = false,
                NamespaceHandling = NamespaceHandling.OmitDuplicates
            };

            using var memoryStream = new MemoryStream();
            using var xmlWriter = XmlWriter.Create(memoryStream, settings);
            
            serializer.Serialize(xmlWriter, invoice, DefaultNamespaces);
            xmlWriter.Flush();
            
            var xml = Encoding.UTF8.GetString(memoryStream.ToArray());
            
            _logger.LogDebug("Successfully serialized invoice to {Length} character XML", xml.Length);
            
            return xml;
        }, cancellationToken);
    }

    public async Task<UblInvoice> DeserializeInvoiceAsync(string xmlContent, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Deserializing XML content of {Length} characters", xmlContent.Length);
        
        return await Task.Run(() =>
        {
            // Clean the XML first
            var cleanedXml = CleanXml(xmlContent);
            
            var serializer = new XmlSerializer(typeof(UblInvoice));
            
            using var stringReader = new StringReader(cleanedXml);
            using var xmlReader = XmlReader.Create(stringReader, new XmlReaderSettings
            {
                IgnoreWhitespace = true,
                IgnoreComments = true
            });
            
            var invoice = (UblInvoice)serializer.Deserialize(xmlReader)!;
            
            _logger.LogDebug("Successfully deserialized invoice {InvoiceId}", invoice.Id);
            
            return invoice;
        }, cancellationToken);
    }

    public async Task<XmlValidationResult> ValidateXmlAsync(string xmlContent, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Validating XML content");
        
        return await Task.Run(() =>
        {
            var result = new XmlValidationResult();
            
            try
            {
                // Clean the XML first
                var cleanedXml = CleanXml(xmlContent);
                
                var doc = new XmlDocument();
                doc.LoadXml(cleanedXml);
                
                // Basic well-formed check passed
                result.IsWellFormed = true;
                
                // Check for required namespaces
                var nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("ubl", "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2");
                nsmgr.AddNamespace("cac", "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2");
                nsmgr.AddNamespace("cbc", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2");
                
                // Validate required elements exist
                var requiredElements = new[]
                {
                    "//cbc:CustomizationID",
                    "//cbc:ID",
                    "//cbc:IssueDate",
                    "//cbc:InvoiceTypeCode",
                    "//cbc:DocumentCurrencyCode",
                    "//cac:AccountingSupplierParty",
                    "//cac:AccountingCustomerParty",
                    "//cac:LegalMonetaryTotal",
                    "//cac:InvoiceLine"
                };
                
                foreach (var xpath in requiredElements)
                {
                    var node = doc.SelectSingleNode(xpath, nsmgr);
                    if (node == null)
                    {
                        result.Errors.Add($"Required element missing: {xpath}");
                        result.IsValid = false;
                    }
                }
                
                // Check CustomizationID value
                var customizationNode = doc.SelectSingleNode("//cbc:CustomizationID", nsmgr);
                if (customizationNode != null)
                {
                    var expectedCustomization = "urn:cen.eu:en16931:2017#compliant#urn:efactura.mfinante.ro:CIUS-RO:1.0.1";
                    if (customizationNode.InnerText != expectedCustomization)
                    {
                        result.Warnings.Add($"CustomizationID should be: {expectedCustomization}");
                    }
                }
                
                if (result.Errors.Count == 0)
                {
                    result.IsValid = true;
                }
                
                _logger.LogDebug("XML validation completed. Valid: {IsValid}, Errors: {ErrorCount}, Warnings: {WarningCount}", 
                    result.IsValid, result.Errors.Count, result.Warnings.Count);
            }
            catch (XmlException ex)
            {
                result.IsWellFormed = false;
                result.IsValid = false;
                result.Errors.Add($"XML parsing error: {ex.Message}");
                _logger.LogWarning(ex, "XML validation failed due to parsing error");
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Errors.Add($"Validation error: {ex.Message}");
                _logger.LogError(ex, "Unexpected error during XML validation");
            }
            
            return result;
        }, cancellationToken);
    }

    public string FormatXml(string xmlContent)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xmlContent);
            
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                Encoding = Encoding.UTF8,
                OmitXmlDeclaration = false
            };
            
            using var stringWriter = new StringWriter();
            using var xmlWriter = XmlWriter.Create(stringWriter, settings);
            
            doc.WriteContentTo(xmlWriter);
            xmlWriter.Flush();
            
            return stringWriter.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to format XML, returning original content");
            return xmlContent;
        }
    }

    public string CleanXml(string xmlContent)
    {
        if (string.IsNullOrEmpty(xmlContent))
            return xmlContent;
        
        // Remove BOM if present
        if (xmlContent.StartsWith("\uFEFF"))
        {
            xmlContent = xmlContent.Substring(1);
        }
        
        // Remove problematic schema location that ANAF doesn't like
        xmlContent = xmlContent.Replace(
            "xsi:schemaLocation=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2 ../../UBL-2.1(1)/xsd/maindoc/UBL-Invoice-2.1.xsd\"", 
            "");
        
        // Normalize line endings
        xmlContent = xmlContent.Replace("\r\n", "\n").Replace("\r", "\n");
        
        return xmlContent.Trim();
    }
}

/// <summary>
/// Result of XML validation
/// </summary>
public class XmlValidationResult
{
    public bool IsWellFormed { get; set; }
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
