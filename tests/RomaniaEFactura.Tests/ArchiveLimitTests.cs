using System.IO.Compression;
using System.Text;

namespace RomaniaEFactura.Tests;

/// <summary>
/// How far a downloaded archive is allowed to expand.
/// </summary>
/// <remarks>
/// <para>
/// The archive normally comes from ANAF over TLS, so this is hardening rather than a hole anyone
/// can reach today. But <see cref="EFacturaArchiveReader"/> is public API, and an application that
/// lets somebody upload an e-Factura archive for inspection is one plausible feature away — at
/// which point an unbounded reader is an out-of-memory crash triggered by a small file.
/// </para>
/// <para>
/// DEFLATE reaches roughly a thousand to one on repetitive input, which is what makes the archives
/// below small enough to build in a test and large enough to matter.
/// </para>
/// </remarks>
public class ArchiveLimitTests
{
    [Fact]
    public void AnOrdinaryArchiveIsUnaffected()
    {
        var archive = Zip(("factura.xml", Encoding.UTF8.GetBytes(SampleXml)));

        var document = EFacturaArchiveReader.Read(archive);

        Assert.Equal(EFacturaDocumentKind.Invoice, document.Kind);
    }

    [Fact]
    public void AnEntryThatExpandsPastTheDefaultIsRefused()
    {
        // Against the default, not a test-sized limit: a limit that only holds when a test lowers
        // it would leave every real caller unprotected and the suite still green.
        var archive = Zip(("factura.xml", Repetitive((int)ArchiveLimits.Default.MaxTotalUncompressedBytes + 1024)));

        var exception = Assert.Throws<InvalidDataException>(() => EFacturaArchiveReader.Read(archive));

        Assert.Contains("expands past", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBudgetIsSpentAcrossEntriesRatherThanPerEntry()
    {
        // Four entries each comfortably under the limit, together over it. Checking per entry would
        // let this through while holding all four in memory at once.
        var limits = new ArchiveLimits { MaxTotalUncompressedBytes = 4 * 1024 * 1024 };
        var archive = Zip(
            ("a.bin", Repetitive(1_500_000)),
            ("b.bin", Repetitive(1_500_000)),
            ("c.bin", Repetitive(1_500_000)),
            ("factura.xml", Encoding.UTF8.GetBytes(SampleXml)));

        var exception = Assert.Throws<InvalidDataException>(() => EFacturaArchiveReader.Read(archive, limits));

        Assert.Contains("expands past", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBudgetIsSpentAcrossNestingToo()
    {
        // Two megabytes outside and two more inside, against a three megabyte budget. Neither half
        // exceeds it alone, so this only fails if the budget survives the recursion rather than
        // starting again inside the nested archive.
        var limits = new ArchiveLimits { MaxTotalUncompressedBytes = 3 * 1024 * 1024 };
        var inner = Zip(("big.bin", Repetitive(2_000_000)));
        var archive = Zip(
            ("outer.bin", Repetitive(2_000_000)),
            ("inner.zip", inner),
            ("factura.xml", Encoding.UTF8.GetBytes(SampleXml)));

        Assert.Throws<InvalidDataException>(() => EFacturaArchiveReader.Read(archive, limits));
    }

    [Fact]
    public void TooManyEntriesAreRefused()
    {
        var limits = new ArchiveLimits { MaxEntries = 8 };
        var archive = Zip([.. Enumerable.Range(0, 20).Select(i => ($"f{i}.txt", Encoding.UTF8.GetBytes("x")))]);

        var exception = Assert.Throws<InvalidDataException>(() => EFacturaArchiveReader.Read(archive, limits));

        Assert.Contains("more than 8 entries", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnArchiveInsideTheLimitStillReads()
    {
        // The limit must not be so eager that a real archive — document, signature, PDF — trips it.
        var limits = new ArchiveLimits { MaxTotalUncompressedBytes = 4 * 1024 * 1024 };
        var archive = Zip(
            ("factura.xml", Encoding.UTF8.GetBytes(SampleXml)),
            ("semnatura_4417.xml", Encoding.UTF8.GetBytes("<Signature />")),
            ("factura.pdf", Repetitive(500_000)));

        var document = EFacturaArchiveReader.Read(archive, limits);

        Assert.Equal(EFacturaDocumentKind.Invoice, document.Kind);
        Assert.NotNull(document.SignatureXml);
        Assert.NotNull(document.Pdf);
    }

    [Fact]
    public void TheMessageNamesTheEntryWithoutLettingItForgeALogLine()
    {
        // The entry name is written by whoever built the archive. It reaches a log, so a newline in
        // it would let that person write a line of their own.
        var limits = new ArchiveLimits { MaxTotalUncompressedBytes = 1024 };
        var archive = Zip(("evil\n2026-01-01 INFO all is well.bin", Repetitive(64 * 1024)));

        var exception = Assert.Throws<InvalidDataException>(() => EFacturaArchiveReader.Read(archive, limits));

        Assert.DoesNotContain('\n', exception.Message);
        Assert.Contains("evil", exception.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Bytes that compress hard, which is the whole point of the attack.</summary>
    private static byte[] Repetitive(int length) => new byte[length];

    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = zip.CreateEntry(name, CompressionLevel.SmallestSize).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private const string SampleXml =
        """<?xml version="1.0" encoding="UTF-8"?><Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2" />""";
}
