using System.Xml.Linq;

namespace MockAnafServer;

/// <summary>
/// Every response shape the mock emits, in one place.
/// </summary>
/// <remarks>
/// <para>
/// This file is the mock's half of the contract described in <c>docs/anaf-wire-formats.md</c>.
/// Nothing else in the mock may construct a response body: when ANAF's real behaviour turns out to
/// differ, correcting it must be a change here and nowhere else.
/// </para>
/// <para>
/// The single most important property of these responses is that **failures are HTTP 200**. ANAF
/// signals every business error inside a success status, in the endpoint's own format, and
/// <c>descarcare</c> will hand back a JSON error body where a ZIP was expected. A client that
/// branches on <c>IsSuccessStatusCode</c> reads all of these as success, which is exactly the
/// defect this mock exists to catch.
/// </para>
/// </remarks>
public static class AnafResponses
{
    private static readonly XNamespace UploadNs = "mfp:anaf:dgti:spv:respUploadFisier:v1";
    private static readonly XNamespace StatusNs = "mfp:anaf:dgti:efactura:stareMesajFactura:v1";

    /// <summary>ANAF's timestamp format, used for <c>dateResponse</c> and <c>data_creare</c>.</summary>
    public const string TimestampFormat = "yyyyMMddHHmm";

    // ---------------------------------------------------------------- upload

    /// <summary>An accepted upload, carrying the index the client must persist.</summary>
    public static string UploadAccepted(string indexIncarcare, DateTimeOffset now) =>
        Xml(new XElement(UploadNs + "header",
            new XAttribute("dateResponse", now.ToString(TimestampFormat)),
            new XAttribute("ExecutionStatus", "0"),
            new XAttribute("index_incarcare", indexIncarcare)));

    /// <summary>
    /// A rejected upload. Still HTTP 200 — the rejection is carried by
    /// <c>ExecutionStatus="1"</c> plus one or more <c>Errors</c> children.
    /// </summary>
    public static string UploadRejected(DateTimeOffset now, params string[] errors) =>
        Xml(new XElement(UploadNs + "header",
            new XAttribute("dateResponse", now.ToString(TimestampFormat)),
            new XAttribute("ExecutionStatus", "1"),
            errors.Select(e => new XElement(UploadNs + "Errors", new XAttribute("errorMessage", e)))));

    // ------------------------------------------------------------ stareMesaj

    /// <summary>A processed upload, with the identifier <c>descarcare</c> needs.</summary>
    public static string StatusResolved(string stare, string idDescarcare) =>
        Xml(new XElement(StatusNs + "header",
            new XAttribute("stare", stare),
            new XAttribute("id_descarcare", idDescarcare)));

    /// <summary>An upload still being processed. Carries no download identifier.</summary>
    public static string StatusPending(string stare) =>
        Xml(new XElement(StatusNs + "header", new XAttribute("stare", stare)));

    /// <summary>A status query that failed. Still HTTP 200.</summary>
    public static string StatusError(string message) =>
        Xml(new XElement(StatusNs + "header",
            new XElement(StatusNs + "Errors", new XAttribute("errorMessage", message))));

    // ----------------------------------------------------------- lista mesaje

    /// <summary>A message list. <c>numar_total_pagini</c> is present only when paginated.</summary>
    public static object MessageList(IEnumerable<object> messages, int? totalPages = null)
    {
        var payload = new Dictionary<string, object>
        {
            ["mesaje"] = messages.ToArray(),
            ["serial"] = "mock",
            ["titlu"] = "Lista Mesaje",
        };

        if (totalPages is { } pages)
        {
            payload["numar_total_pagini"] = pages;
        }

        return payload;
    }

    /// <summary>
    /// A message-list error, including the empty case: ANAF answers "Nu exista mesaje" through the
    /// same <c>eroare</c> field it uses for genuine failures, so a client must treat that one
    /// string as an empty result rather than a fault.
    /// </summary>
    public static object MessageListError(string message) =>
        new Dictionary<string, object> { ["eroare"] = message, ["titlu"] = "Lista Mesaje" };

    // ------------------------------------------------------------- descarcare

    /// <summary>
    /// A download failure. Note the shape: JSON, on HTTP 200, where the caller expected a ZIP.
    /// </summary>
    public static object DownloadError(string message) =>
        new Dictionary<string, object> { ["eroare"] = message, ["titlu"] = "Descarcare mesaj" };

    // ---------------------------------------------------------------- shared

    /// <summary>
    /// A genuine HTTP 400. Unlike everything above, this really does use an error status — and a
    /// different shape again, so a client cannot assume one content type per endpoint.
    /// </summary>
    public static object BadRequest(string message, DateTimeOffset now) =>
        new Dictionary<string, object>
        {
            ["timestamp"] = now.ToString("dd-MM-yyyy HH:mm:ss"),
            ["status"] = 400,
            ["error"] = "Bad Request",
            ["message"] = message,
        };

    /// <summary>The validation endpoint's result, which is JSON in both directions.</summary>
    public static object ValidationResult(bool valid, params string[] messages) =>
        valid
            ? new Dictionary<string, object>
            {
                ["stare"] = "ok",
                ["trace_id"] = Guid.NewGuid().ToString(),
            }
            : new Dictionary<string, object>
            {
                ["stare"] = "nok",
                ["Messages"] = messages.Select(m => new Dictionary<string, string> { ["message"] = m }).ToArray(),
                ["trace_id"] = Guid.NewGuid().ToString(),
            };

    private static string Xml(XElement root) =>
        new XDeclaration("1.0", "UTF-8", "yes") + Environment.NewLine + root;
}
