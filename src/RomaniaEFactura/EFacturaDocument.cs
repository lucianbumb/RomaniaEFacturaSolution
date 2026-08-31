using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using RomaniaEFactura.Ubl;

namespace RomaniaEFactura;

/// <summary>What kind of document an archive turned out to hold.</summary>
public enum EFacturaDocumentKind
{
    /// <summary>An invoice.</summary>
    Invoice = 0,

    /// <summary>A credit note.</summary>
    CreditNote,

    /// <summary>A debit note. Inbound only — these cannot be submitted.</summary>
    DebitNote,

    /// <summary>A report of the validation errors that caused a rejection.</summary>
    ValidationErrors,

    /// <summary>A message from a buyer back to a seller.</summary>
    BuyerMessage,

    /// <summary>Something the library does not recognise. The XML is still available.</summary>
    Unknown,
}

/// <summary>
/// The contents of a downloaded archive.
/// </summary>
/// <remarks>
/// Discriminated rather than assumed to be an invoice, because it frequently is not: a rejection
/// yields an error report, a received document may be a credit or debit note, and a buyer message
/// is something else again. The previous version deserialized everything as an invoice, so any
/// non-invoice message failed with a parser error rather than being handled.
/// </remarks>
public sealed record EFacturaDocument
{
    /// <summary>What the archive held.</summary>
    public required EFacturaDocumentKind Kind { get; init; }

    /// <summary>The document XML, exactly as ANAF supplied it.</summary>
    public required string Xml { get; init; }

    /// <summary>The ministry's signature over the document, when the archive carried one.</summary>
    public string? SignatureXml { get; init; }

    /// <summary>The parsed invoice, when <see cref="Kind"/> is <see cref="EFacturaDocumentKind.Invoice"/>.</summary>
    public UblInvoice? Invoice { get; init; }

    /// <summary>The parsed credit note, when the archive held one.</summary>
    public UblCreditNote? CreditNote { get; init; }

    /// <summary>The parsed debit note, when the archive held one.</summary>
    public UblDebitNote? DebitNote { get; init; }

    /// <summary>A PDF, when the archive already contained one.</summary>
    public byte[]? Pdf { get; init; }
}

/// <summary>
/// Reads the archives ANAF returns from <c>descarcare</c>.
/// </summary>
public static partial class EFacturaArchiveReader
{
    /// <summary>
    /// Extracts and identifies the document in an archive.
    /// </summary>
    /// <param name="archive">The ZIP bytes returned by ANAF.</param>
    /// <exception cref="InvalidDataException">The archive holds no recognisable document.</exception>
    public static EFacturaDocument Read(byte[] archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        var entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        Collect(archive, entries, depth: 0);

        var signature = entries
            .FirstOrDefault(e => e.Key.StartsWith("semnatura", StringComparison.OrdinalIgnoreCase)
                                 && e.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

        var pdf = entries.FirstOrDefault(e => e.Key.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

        foreach (var (name, content) in entries)
        {
            if (!name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.StartsWith("semnatura", StringComparison.OrdinalIgnoreCase)) continue;

            var xml = Decode(content);
            var kind = Identify(xml);

            return new EFacturaDocument
            {
                Kind = kind,
                Xml = xml,
                SignatureXml = signature.Value is null ? null : Decode(signature.Value),
                Pdf = pdf.Value,
                Invoice = kind == EFacturaDocumentKind.Invoice ? UblSerializer.DeserializeInvoice(xml) : null,
                CreditNote = kind == EFacturaDocumentKind.CreditNote ? UblSerializer.DeserializeCreditNote(xml) : null,
                DebitNote = kind == EFacturaDocumentKind.DebitNote ? UblSerializer.DeserializeDebitNote(xml) : null,
            };
        }

        throw new InvalidDataException(
            $"The archive holds no document XML. Entries: {string.Join(", ", entries.Keys)}");
    }

    /// <summary>
    /// Collects every file in the archive, following nested archives.
    /// </summary>
    /// <remarks>
    /// Real archives are sometimes a ZIP inside a ZIP. The depth limit guards against a crafted
    /// archive that nests indefinitely.
    /// </remarks>
    private static void Collect(byte[] archive, Dictionary<string, byte[]> entries, int depth)
    {
        if (depth > 4) return;

        using var stream = new MemoryStream(archive);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in zip.Entries)
        {
            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            var content = buffer.ToArray();

            if (entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                Collect(content, entries, depth + 1);
                continue;
            }

            entries[entry.Name] = content;
        }
    }

    private static string Decode(byte[] content) =>
        new UTF8Encoding(false).GetString(content).TrimStart('﻿');

    /// <summary>
    /// Identifies a document from its root element.
    /// </summary>
    /// <remarks>
    /// Matched with a regular expression rather than by parsing, because the document may be
    /// something the UBL deserializers cannot read at all — an error report, for instance — and
    /// failing to identify it is worse than reporting it as unknown.
    /// </remarks>
    private static EFacturaDocumentKind Identify(string xml)
    {
        var match = RootElementPattern().Match(xml);
        if (!match.Success) return EFacturaDocumentKind.Unknown;

        return match.Groups["root"].Value switch
        {
            "Invoice" => EFacturaDocumentKind.Invoice,
            "CreditNote" => EFacturaDocumentKind.CreditNote,
            "DebitNote" => EFacturaDocumentKind.DebitNote,
            // ANAF names the rejection report's root element after the errors it carries.
            "header" or "Errors" or "erori" => EFacturaDocumentKind.ValidationErrors,
            _ => EFacturaDocumentKind.Unknown,
        };
    }

    [GeneratedRegex(@"<\s*(?:[\w.-]+:)?(?<root>Invoice|CreditNote|DebitNote|header|Errors|erori)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex RootElementPattern();
}
