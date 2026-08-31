using System.IO.Compression;
using System.Text;

namespace MockAnafServer;

/// <summary>
/// Builds the ZIP archives <c>descarcare</c> returns.
/// </summary>
/// <remarks>
/// A real archive holds the document and the Ministry of Finance signature over it. The signature
/// here is a placeholder with the right shape and name — the mock cannot produce a genuine seal,
/// and a client must never be written to depend on one being verifiable in tests.
/// </remarks>
public static class ArchiveBuilder
{
    /// <summary>Builds the archive for a message.</summary>
    /// <param name="message">The message being downloaded.</param>
    /// <param name="scenario">Lets a test request a nested archive or an embedded PDF.</param>
    public static byte[] Build(MessageRecord message, MockScenario scenario)
    {
        var inner = BuildFlat(message, scenario);

        // Real archives are occasionally a ZIP inside a ZIP; a client has to recurse.
        return scenario == MockScenario.NestedArchive
            ? Wrap(inner, $"{message.Id}.zip")
            : inner;
    }

    private static byte[] BuildFlat(MessageRecord message, MockScenario scenario)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, $"{message.Id}.xml", message.Xml);
            Write(archive, $"semnatura_{message.Id}.xml", SignaturePlaceholder(message.Id));

            if (scenario == MockScenario.ArchiveWithPdf)
            {
                // Minimal but genuinely PDF-headed bytes, so a client's %PDF- sniff behaves.
                WriteBytes(archive, $"{message.Id}.pdf", Encoding.ASCII.GetBytes("%PDF-1.4\n% mock\n"));
            }
        }

        return buffer.ToArray();
    }

    private static byte[] Wrap(byte[] content, string entryName)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteBytes(archive, entryName, content);
        }

        return buffer.ToArray();
    }

    private static void Write(ZipArchive archive, string name, string content) =>
        WriteBytes(archive, name, new UTF8Encoding(false).GetBytes(content));

    private static void WriteBytes(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        stream.Write(content);
    }

    /// <summary>
    /// A stand-in for the MF signature. Shaped like the real thing so archive-handling code can be
    /// exercised, but deliberately not a valid signature.
    /// </summary>
    private static string SignaturePlaceholder(string id) =>
        $"""
         <?xml version="1.0" encoding="UTF-8"?>
         <Signature xmlns="http://www.w3.org/2000/09/xmldsig#">
           <SignedInfo>
             <Reference URI="{id}.xml" />
           </SignedInfo>
           <SignatureValue>MOCK-NOT-A-REAL-SIGNATURE</SignatureValue>
         </Signature>
         """;
}
