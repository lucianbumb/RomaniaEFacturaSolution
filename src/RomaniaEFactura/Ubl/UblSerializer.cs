using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace RomaniaEFactura.Ubl;

/// <summary>
/// Serializes and deserializes UBL documents in the exact shape ANAF accepts.
/// </summary>
public static class UblSerializer
{
    // XmlSerializer instances are expensive to build and thread-safe once built, so cache them.
    // Constructing one per call also leaks a dynamic assembly per instance for some ctor overloads.
    private static readonly XmlSerializer InvoiceSerializer = new(typeof(UblInvoice));
    private static readonly XmlSerializer CreditNoteSerializer = new(typeof(UblCreditNote));

    private static readonly XmlSerializerNamespaces Namespaces = BuildNamespaces();

    private static XmlSerializerNamespaces BuildNamespaces()
    {
        var ns = new XmlSerializerNamespaces();
        // Default namespace for the document element; cac/cbc prefixed as ANAF's examples do.
        // xsi and xsd are deliberately absent: an xsi:schemaLocation on the root is the single
        // most common reason ANAF rejects an otherwise valid document.
        ns.Add(string.Empty, UblNamespaces.Invoice);
        ns.Add("cac", UblNamespaces.Cac);
        ns.Add("cbc", UblNamespaces.Cbc);
        return ns;
    }

    /// <summary>Serializes an invoice to XML.</summary>
    public static string Serialize(UblInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return Serialize(InvoiceSerializer, invoice, UblNamespaces.Invoice);
    }

    /// <summary>Serializes a credit note to XML.</summary>
    public static string Serialize(UblCreditNote creditNote)
    {
        ArgumentNullException.ThrowIfNull(creditNote);
        return Serialize(CreditNoteSerializer, creditNote, UblNamespaces.CreditNote);
    }

    private static string Serialize(XmlSerializer serializer, object document, string rootNamespace)
    {
        var ns = new XmlSerializerNamespaces();
        ns.Add(string.Empty, rootNamespace);
        ns.Add("cac", UblNamespaces.Cac);
        ns.Add("cbc", UblNamespaces.Cbc);

        var settings = new XmlWriterSettings
        {
            // UTF-8 without a BOM. A BOM ahead of the XML declaration makes ANAF's parser fail.
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = false,
            NamespaceHandling = NamespaceHandling.OmitDuplicates,
        };

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            serializer.Serialize(writer, document, ns);
        }

        return settings.Encoding.GetString(stream.ToArray());
    }

    /// <summary>Deserializes an invoice from XML.</summary>
    public static UblInvoice DeserializeInvoice(string xml)
    {
        using var reader = CreateReader(xml);
        return (UblInvoice)InvoiceSerializer.Deserialize(reader)!;
    }

    /// <summary>Deserializes a credit note from XML.</summary>
    public static UblCreditNote DeserializeCreditNote(string xml)
    {
        using var reader = CreateReader(xml);
        return (UblCreditNote)CreditNoteSerializer.Deserialize(reader)!;
    }

    /// <summary>
    /// Reads the local name of the document element, so a downloaded archive can be routed to the
    /// right deserializer. Returns <see langword="null"/> when the content is not XML.
    /// </summary>
    public static string? ReadDocumentType(string xml)
    {
        try
        {
            using var reader = CreateReader(xml);
            return reader.MoveToContent() == XmlNodeType.Element ? reader.LocalName : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static XmlReader CreateReader(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        return XmlReader.Create(
            new StringReader(xml.TrimStart('﻿')),
            new XmlReaderSettings
            {
                IgnoreWhitespace = true,
                IgnoreComments = true,
                // A downloaded document is untrusted input; never resolve external entities.
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
            });
    }
}
