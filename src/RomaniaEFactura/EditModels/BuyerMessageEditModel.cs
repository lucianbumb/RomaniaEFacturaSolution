using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace RomaniaEFactura.EditModels;

/// <summary>
/// A message from the buyer back to the seller about an invoice already received (RASP).
/// </summary>
/// <remarks>
/// <para>
/// This is how a buyer disputes an invoice inside e-Factura rather than by email: the message is
/// uploaded like a document, with <c>standard=RASP</c>, and reaches the seller through the SPV.
/// It is not UBL and carries none of the EN16931 rules — two attributes and nothing else.
/// </para>
/// <para>
/// <b>Provenance.</b> ANAF publishes no schema for this format, and it is absent from the four
/// OpenAPI specifications the rest of the library is built from. The shape used here — a
/// <c>header</c> element in <c>mfp:anaf:dgti:spv:reqMesaj:v1</c> carrying
/// <c>index_incarcare</c> and <c>message</c> — is corroborated by two independent sources but by
/// no official one, so it is the only wire format in the library not confirmed against ANAF's own
/// documentation. Confirming it against the test environment is part of the real-run milestone.
/// </para>
/// </remarks>
public sealed class BuyerMessageEditModel
{
    /// <summary>
    /// The upload index of the invoice being answered.
    /// </summary>
    /// <remarks>
    /// ANAF's <c>index_incarcare</c> — the same value <c>SendInvoiceAsync</c> returns to the
    /// seller, and the value the buyer sees on the received message.
    /// </remarks>
    [Required(ErrorMessage = "The upload index of the invoice is required.")]
    [RegularExpression("^[0-9]+$", ErrorMessage = "The upload index is numeric.")]
    [Display(Name = "Invoice upload index")]
    public string UploadIndex { get; set; } = string.Empty;

    /// <summary>What the buyer wants to say to the seller.</summary>
    [Required(ErrorMessage = "The message is required.")]
    [StringLength(1000, MinimumLength = 1)]
    [Display(Name = "Message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Renders the message as the XML ANAF's upload endpoint expects.</summary>
    public string ToXml()
    {
        var wire = new BuyerMessageDocument
        {
            UploadIndex = UploadIndex,
            Message = Message,
        };

        var ns = new XmlSerializerNamespaces();
        ns.Add(string.Empty, BuyerMessageDocument.Namespace);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
            NamespaceHandling = NamespaceHandling.OmitDuplicates,
        };

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            BuyerMessageDocument.Serializer.Serialize(writer, wire, ns);
        }

        return settings.Encoding.GetString(stream.ToArray());
    }
}

/// <summary>The wire form of a buyer message.</summary>
[XmlRoot("header", Namespace = Namespace)]
public sealed class BuyerMessageDocument
{
    /// <summary>The namespace ANAF expects on the message element.</summary>
    public const string Namespace = "mfp:anaf:dgti:spv:reqMesaj:v1";

    internal static readonly XmlSerializer Serializer = new(typeof(BuyerMessageDocument));

    /// <summary>The upload index of the invoice being answered.</summary>
    [XmlAttribute("index_incarcare")]
    public string UploadIndex { get; set; } = string.Empty;

    /// <summary>The message text.</summary>
    [XmlAttribute("message")]
    public string Message { get; set; } = string.Empty;
}
