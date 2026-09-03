using System.Collections.Concurrent;

namespace MockAnafServer;

/// <summary>
/// The mock's in-memory model of one SPV account: what has been uploaded, what is available to
/// download, and how much of each daily quota has been spent.
/// </summary>
/// <remarks>
/// Deterministic on purpose. Identifiers are sequential rather than random so a failing
/// integration test can be read without cross-referencing generated values.
/// </remarks>
public sealed class MockAnafState
{
    private readonly ConcurrentDictionary<string, UploadRecord> _uploads = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MessageRecord> _messages = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, QuotaCounter> _quotas = new(StringComparer.Ordinal);
    private long _nextIndex = 5_001_000_000;
    private long _nextDownloadId = 3_001_000_000;

    /// <summary>
    /// How many times <c>stareMesaj</c> reports "in prelucrare" before an upload resolves.
    /// Zero makes uploads resolve immediately, which keeps most tests fast.
    /// </summary>
    public int PollsBeforeResolution { get; set; }

    /// <summary>The clock, overridable so tests can exercise the 60-day boundary.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// The taxpayer register, keyed by fiscal code.
    /// </summary>
    /// <remarks>
    /// Seeded with a handful of companies covering the combinations that change how a document is
    /// built: in the e-Factura register or not, VAT-registered or not, inactive or not. A test
    /// adds its own through <c>/__mock/companies</c>.
    /// </remarks>
    public ConcurrentDictionary<string, RegisteredCompany> Companies { get; } = new(
        StringComparer.Ordinal)
    {
        // The account under test: ordinary, active, VAT-registered, in the e-Factura register.
        ["12345674"] = new RegisteredCompany(
            "12345674", "SC TEST SRL", "J12/345/2001", "0264111222", "6201", "RO49AAAA1B31007593840000",
            EFactura: true, Vat: true, Inactive: false,
            "Strada Memorandumului", "28", "Cluj-Napoca", "Cluj", "CJ", "RO", "400114", "Etaj 2"),

        // Not in the e-Factura register: a document to this one goes through uploadb2c.
        ["19867705"] = new RegisteredCompany(
            "19867705", "SC FARA EFACTURA SRL", "J40/999/2007", null, "4711", null,
            EFactura: false, Vat: true, Inactive: false,
            "Bulevardul Unirii", "12", "Bucuresti", "Bucuresti", "B", "RO", "030167", null),

        // Not VAT-registered, so no BT-48 belongs on a document to it.
        ["80000009"] = new RegisteredCompany(
            "80000009", "SC NEPLATITOR TVA SRL", "J13/222/2015", null, "5610", null,
            EFactura: true, Vat: false, Inactive: false,
            "Strada Stefan cel Mare", "5", "Constanta", "Constanta", "CT", "RO", "900178", null),

        // On the register of inactive taxpayers.
        ["98765438"] = new RegisteredCompany(
            "98765438", "SC INACTIVA SRL", "J22/111/2010", null, "4520", null,
            EFactura: false, Vat: false, Inactive: true,
            "Strada Lapusneanu", "3", "Iasi", "Iasi", "IS", "RO", "700057", null),
    };

    /// <summary>Daily call cap for <c>stareMesaj</c>, per upload index.</summary>
    public int StatusQuotaPerDay { get; set; } = 20;

    /// <summary>Daily call cap for <c>descarcare</c>, per download identifier.</summary>
    public int DownloadQuotaPerDay { get; set; } = 10;

    /// <summary>The most recent upload, for a test that needs to see what was sent.</summary>
    public UploadRecord? LastUpload() =>
        _uploads.Values.OrderByDescending(u => u.Uploaded).ThenByDescending(u => u.IndexIncarcare, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>Records an accepted upload and returns its index.</summary>
    public UploadRecord AddUpload(string cif, string standard, string xml, bool willBeRejected)
    {
        var index = Interlocked.Increment(ref _nextIndex).ToString();
        var record = new UploadRecord(index, cif, standard, xml, Clock())
        {
            RemainingPolls = PollsBeforeResolution,
            Outcome = willBeRejected ? "nok" : "ok",
        };

        _uploads[index] = record;
        return record;
    }

    /// <summary>Finds an upload by the index returned at upload time.</summary>
    public UploadRecord? FindUpload(string index) =>
        _uploads.TryGetValue(index, out var record) ? record : null;

    /// <summary>
    /// Advances an upload one poll. Once its remaining polls reach zero it resolves, a download
    /// identifier is minted, and a matching message appears in the list.
    /// </summary>
    public UploadRecord Poll(UploadRecord upload)
    {
        if (upload.IdDescarcare is not null) return upload;

        if (upload.RemainingPolls > 0)
        {
            upload.RemainingPolls--;
            return upload;
        }

        upload.IdDescarcare = Interlocked.Increment(ref _nextDownloadId).ToString();

        var isError = upload.Outcome == "nok";
        _messages[upload.IdDescarcare] = new MessageRecord(
            Id: upload.IdDescarcare,
            IdSolicitare: upload.IndexIncarcare,
            Cif: upload.Cif,
            Tip: isError ? "ERORI FACTURA" : "FACTURA TRIMISA",
            Detalii: isError
                ? $"Erori de validare identificate la factura primita cu id_incarcare={upload.IndexIncarcare}"
                : $"Factura cu id_incarcare={upload.IndexIncarcare} emisa de cif_emitent={upload.Cif}",
            CifEmitent: upload.Cif,
            CifBeneficiar: upload.Cif,
            Created: Clock(),
            Xml: upload.Xml,
            IsError: isError);

        return upload;
    }

    /// <summary>
    /// Adds a message that did not originate from an upload — an invoice received from someone
    /// else, for example.
    /// </summary>
    public MessageRecord AddIncomingMessage(
        string cif,
        string xml,
        string tip = "FACTURA PRIMITA",
        string? cifEmitent = null,
        bool hideId = false,
        DateTimeOffset? created = null)
    {
        var id = Interlocked.Increment(ref _nextDownloadId).ToString();
        var solicitare = Interlocked.Increment(ref _nextIndex).ToString();

        var record = new MessageRecord(
            Id: id,
            IdSolicitare: solicitare,
            Cif: cif,
            Tip: tip,
            Detalii: $"Factura primita de la cif_emitent={cifEmitent ?? "8000000000"}",
            CifEmitent: cifEmitent ?? "8000000000",
            CifBeneficiar: cif,
            Created: created ?? Clock(),
            Xml: xml,
            IsError: false)
        {
            // Some real messages arrive carrying only id_solicitare, forcing the client to resolve
            // the download identifier through stareMesaj before it can download anything.
            HideId = hideId,
        };

        _messages[id] = record;
        if (hideId)
        {
            // Make the hidden message resolvable the way the real service does.
            _uploads[solicitare] = new UploadRecord(solicitare, cif, "UBL", xml, record.Created)
            {
                Outcome = "ok",
                IdDescarcare = id,
                RemainingPolls = 0,
            };
        }

        return record;
    }

    /// <summary>Messages for a CIF created within the window, newest first.</summary>
    public IReadOnlyList<MessageRecord> MessagesFor(string cif, DateTimeOffset from, DateTimeOffset to) =>
        [.. _messages.Values
            .Where(m => string.Equals(m.Cif, cif, StringComparison.Ordinal))
            .Where(m => m.Created >= from && m.Created <= to)
            .OrderByDescending(m => m.Created)];

    /// <summary>Finds a downloadable message by its identifier.</summary>
    public MessageRecord? FindMessage(string id) =>
        _messages.TryGetValue(id, out var record) ? record : null;

    /// <summary>
    /// Spends one unit of a daily quota, returning false once the cap for that identifier is
    /// reached. Modelled per identifier per day, matching ANAF's error wording.
    /// </summary>
    public bool TrySpendQuota(string endpoint, string id, int limit)
    {
        var key = $"{endpoint}:{id}:{Clock():yyyyMMdd}";
        var counter = _quotas.GetOrAdd(key, _ => new QuotaCounter());

        lock (counter)
        {
            if (counter.Count >= limit) return false;
            counter.Count++;
            return true;
        }
    }

    /// <summary>Clears everything, so one test cannot leak state into the next.</summary>
    public void Reset()
    {
        _uploads.Clear();
        _messages.Clear();
        _quotas.Clear();
        PollsBeforeResolution = 0;
        Interlocked.Exchange(ref _nextIndex, 5_001_000_000);
        Interlocked.Exchange(ref _nextDownloadId, 3_001_000_000);
    }

    private sealed class QuotaCounter
    {
        public int Count { get; set; }
    }
}

/// <summary>An upload the mock has accepted.</summary>
public sealed record UploadRecord(
    string IndexIncarcare,
    string Cif,
    string Standard,
    string Xml,
    DateTimeOffset Uploaded)
{
    /// <summary>How many more polls report "in prelucrare" before this resolves.</summary>
    public int RemainingPolls { get; set; }

    /// <summary>Whether the upload will resolve as <c>ok</c> or <c>nok</c>.</summary>
    public string Outcome { get; set; } = "ok";

    /// <summary>The download identifier, once processing has finished.</summary>
    public string? IdDescarcare { get; set; }
}

/// <summary>A message available for download.</summary>
public sealed record MessageRecord(
    string Id,
    string IdSolicitare,
    string Cif,
    string Tip,
    string Detalii,
    string CifEmitent,
    string CifBeneficiar,
    DateTimeOffset Created,
    string Xml,
    bool IsError)
{
    /// <summary>
    /// When set, the message list omits <c>id</c>, so a client must resolve it via
    /// <c>stareMesaj</c> using <c>id_solicitare</c>.
    /// </summary>
    public bool HideId { get; set; }
}

/// <summary>A company as the mock's taxpayer register holds it.</summary>
/// <param name="Cui">Fiscal code.</param>
/// <param name="Name">Registered name.</param>
/// <param name="RegistrationNumber">Commerce register number.</param>
/// <param name="Phone">Telephone.</param>
/// <param name="CaenCode">Principal activity code.</param>
/// <param name="Iban">Bank account, when the register holds one.</param>
/// <param name="EFactura">Whether it is in the RO e-Factura register.</param>
/// <param name="Vat">Whether it is registered for VAT.</param>
/// <param name="Inactive">Whether it is on the register of inactive taxpayers.</param>
/// <param name="Street">Street name.</param>
/// <param name="Number">Street number.</param>
/// <param name="Locality">Town or city.</param>
/// <param name="County">County name.</param>
/// <param name="CountyCode">Two-letter county code.</param>
/// <param name="Country">Country name.</param>
/// <param name="PostalCode">Postal code.</param>
/// <param name="Details">Further address detail.</param>
public sealed record RegisteredCompany(
    string Cui,
    string Name,
    string? RegistrationNumber,
    string? Phone,
    string? CaenCode,
    string? Iban,
    bool EFactura,
    bool Vat,
    bool Inactive,
    string Street,
    string Number,
    string Locality,
    string County,
    string CountyCode,
    string Country,
    string PostalCode,
    string? Details);
