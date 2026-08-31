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
/// How much an archive is allowed to expand to.
/// </summary>
/// <remarks>
/// <para>
/// DEFLATE reaches roughly a thousand to one, so an archive small enough to arrive unremarked
/// expands to more memory than the process has. Nothing in a ZIP says how big it will turn out to
/// be — the recorded sizes are written by whoever built the archive — so the only honest defence is
/// to stop reading once a budget is spent.
/// </para>
/// <para>
/// The defaults are far above anything real. ANAF caps an upload at 10 MB, and a downloaded archive
/// is one document, its signature and occasionally a PDF.
/// </para>
/// </remarks>
public sealed record ArchiveLimits
{
    /// <summary>The limits applied when a caller names none.</summary>
    public static ArchiveLimits Default { get; } = new();

    /// <summary>
    /// The most an archive may expand to in total, counted across every entry and every level of
    /// nesting rather than per entry.
    /// </summary>
    public long MaxTotalUncompressedBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>The most entries an archive may hold, counted across nesting in the same way.</summary>
    public int MaxEntries { get; init; } = 256;

    /// <summary>How many archives deep the reader will follow before it stops.</summary>
    public int MaxNestingDepth { get; init; } = 4;
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
    /// <param name="limits">
    /// How far the archive may be allowed to expand. Omit it for <see cref="ArchiveLimits.Default"/>,
    /// which is generous enough that no real e-Factura archive approaches it.
    /// </param>
    /// <exception cref="InvalidDataException">
    /// The archive holds no recognisable document, or it expands past <paramref name="limits"/>.
    /// </exception>
    public static EFacturaDocument Read(byte[] archive, ArchiveLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(archive);

        var entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        Collect(archive, entries, new Budget(limits ?? ArchiveLimits.Default), depth: 0);

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
    /// Real archives are sometimes a ZIP inside a ZIP. Depth was already limited; the budget limits
    /// the two dimensions that matter more, since one entry at depth zero can expand to more than
    /// the machine has.
    /// </remarks>
    private static void Collect(
        byte[] archive,
        Dictionary<string, byte[]> entries,
        Budget budget,
        int depth)
    {
        if (depth > budget.Limits.MaxNestingDepth) return;

        using var stream = new MemoryStream(archive);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in zip.Entries)
        {
            budget.CountEntry(entry.FullName);

            using var entryStream = entry.Open();
            var content = budget.ReadWithinBudget(entryStream, entry.FullName);

            if (entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                Collect(content, entries, budget, depth + 1);
                continue;
            }

            entries[entry.Name] = content;
        }
    }

    /// <summary>
    /// What is left of an archive's allowance, shared across the whole recursive walk.
    /// </summary>
    /// <remarks>
    /// Shared rather than per archive because everything collected is held at once: a nested
    /// archive costs its own decompressed bytes and then its children's on top of them.
    /// </remarks>
    private sealed class Budget(ArchiveLimits limits)
    {
        private long _bytesRemaining = limits.MaxTotalUncompressedBytes;
        private int _entriesRemaining = limits.MaxEntries;

        public ArchiveLimits Limits { get; } = limits;

        public void CountEntry(string name)
        {
            if (--_entriesRemaining < 0)
            {
                throw new InvalidDataException(
                    $"The archive holds more than {Limits.MaxEntries} entries, which no e-Factura "
                    + $"archive does. Refused at '{Describe(name)}'.");
            }
        }

        /// <summary>
        /// Copies an entry, stopping the moment it costs more than is left.
        /// </summary>
        /// <remarks>
        /// The check belongs during the copy, not before or after it. A ZIP records its own
        /// uncompressed sizes and whoever built it wrote them, so consulting
        /// <c>ZipArchiveEntry.Length</c> first would trust the very thing being defended against —
        /// and reading the entry whole in order to measure it is the allocation the limit exists to
        /// prevent.
        /// </remarks>
        public byte[] ReadWithinBudget(Stream source, string name)
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];

            int read;
            while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
            {
                _bytesRemaining -= read;
                if (_bytesRemaining < 0)
                {
                    throw new InvalidDataException(
                        $"The archive expands past {Limits.MaxTotalUncompressedBytes:N0} bytes, which "
                        + "no e-Factura archive does — ANAF caps an upload at 10 MB. Refused while "
                        + $"reading '{Describe(name)}'.");
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }

        /// <summary>
        /// Renders an entry name for a message. The name is written by whoever built the archive,
        /// so it is stripped of anything that would forge a line in a log, and shortened.
        /// </summary>
        private static string Describe(string name)
        {
            var clean = new string([.. name.Where(c => !char.IsControl(c))]);
            return clean.Length <= 100 ? clean : clean[..100] + "...";
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
