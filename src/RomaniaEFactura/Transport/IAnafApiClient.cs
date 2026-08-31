namespace RomaniaEFactura.Transport;

/// <summary>
/// The ANAF e-Factura HTTP API, one method per endpoint.
/// </summary>
/// <remarks>
/// Deliberately thin: it speaks ANAF's vocabulary rather than the application's, and every method
/// returns <see cref="AnafResult{T}"/> rather than throwing on a business failure. The friendlier
/// surface sits above this.
/// </remarks>
public interface IAnafApiClient
{
    /// <summary>Submits a document. Returns the index that must be persisted to reconcile it later.</summary>
    /// <param name="xml">The document.</param>
    /// <param name="cif">The company submitting, with or without an <c>RO</c> prefix.</param>
    /// <param name="options">Which standard, and any of the optional submission flags.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<AnafResult<UploadReceipt>> UploadAsync(
        string xml,
        string? cif = null,
        UploadOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Asks how far a submitted document has got.</summary>
    /// <remarks>
    /// Capped at roughly twenty calls per document per day, so this must not be polled on a timer.
    /// </remarks>
    /// <param name="uploadIndex">ANAF's <c>index_incarcare</c>.</param>
    /// <param name="cif">
    /// Whose submission it is, defaulting to the configured company. It decides which stored
    /// authorization the call is made with, so a deployment serving several companies has to pass
    /// the one the submission belongs to or ANAF will answer that it has no rights.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<AnafResult<MessageStatus>> GetStatusAsync(
        string uploadIndex,
        string? cif = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists messages from the last <paramref name="days"/> days.</summary>
    /// <param name="days">Between 1 and 60, as ANAF requires.</param>
    /// <param name="cif">The company, defaulting to the configured one.</param>
    /// <param name="filter">Restricts the result to one message type.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<AnafResult<IReadOnlyList<AnafMessage>>> ListMessagesAsync(
        int days,
        string? cif = null,
        MessageFilter filter = MessageFilter.All,
        CancellationToken cancellationToken = default);

    /// <summary>Lists one page of messages in a time range.</summary>
    /// <remarks>
    /// The range is clamped to sixty days before the call, because ANAF rejects an older start
    /// outright despite the endpoint accepting arbitrary timestamps.
    /// </remarks>
    Task<AnafResult<MessagePage>> ListMessagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        int page = 1,
        string? cif = null,
        MessageFilter filter = MessageFilter.All,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a message as its raw ZIP archive, holding the document and the ministry's
    /// signature.
    /// </summary>
    /// <remarks>
    /// Capped at roughly ten calls per identifier per day. The archive should be retained: the
    /// signature is the proof of submission.
    /// </remarks>
    /// <param name="downloadId">ANAF's download identifier.</param>
    /// <param name="cif">
    /// Whose message it is, defaulting to the configured company. It decides which stored
    /// authorization the call is made with, and therefore which documents ANAF will hand over.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<AnafResult<byte[]>> DownloadArchiveAsync(
        string downloadId,
        string? cif = null,
        CancellationToken cancellationToken = default);

    /// <summary>Runs ANAF's online validator over a document.</summary>
    /// <remarks>
    /// A network call, and therefore a second opinion rather than the primary gate — offline
    /// validation is what the library promises.
    /// </remarks>
    Task<AnafResult<AnafValidationOutcome>> ValidateAsync(
        string xml,
        AnafStandard standard = AnafStandard.Ubl,
        CancellationToken cancellationToken = default);

    /// <summary>Renders a document to PDF using ANAF's converter.</summary>
    /// <param name="xml">The document.</param>
    /// <param name="standard">Which standard the document follows.</param>
    /// <param name="skipValidation">
    /// Skips ANAF's validation pass. Faster, but ANAF does not guarantee the rendering is faithful
    /// for a document it has not checked.
    /// </param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<AnafResult<byte[]>> RenderPdfAsync(
        string xml,
        AnafStandard standard = AnafStandard.Ubl,
        bool skipValidation = false,
        CancellationToken cancellationToken = default);
}

/// <summary>Supplies the bearer token for ANAF calls.</summary>
/// <remarks>
/// Separated from the transport so the client can be exercised without an OAuth flow. The durable,
/// per-company implementation arrives with the token store.
/// </remarks>
public interface IAnafAccessTokenProvider
{
    /// <summary>
    /// Returns a usable access token for the company, or <see langword="null"/> when nobody has
    /// authorized it yet — which the transport reports as
    /// <see cref="AnafErrorKind.NotAuthorized"/> rather than throwing.
    /// </summary>
    Task<string?> GetAccessTokenAsync(string cif, CancellationToken cancellationToken = default);
}
