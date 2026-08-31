using RomaniaEFactura.EditModels;
using RomaniaEFactura.Transport;
using RomaniaEFactura.Ubl;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura;

/// <summary>
/// Everything an application needs from e-Factura, in one injectable interface.
/// </summary>
/// <remarks>
/// Segmented internally, single externally: a page should not have to know which of five services
/// owns the operation it wants.
/// </remarks>
public interface IRomaniaEFacturaService
{
    // ------------------------------------------------------------ authorization

    /// <summary>
    /// Whether a company can currently talk to ANAF.
    /// </summary>
    /// <remarks>
    /// Worth checking before offering a send button. Authorization requires a person with a
    /// qualified certificate, so "not connected" is a state the interface has to show rather than
    /// an error to discover mid-submission.
    /// </remarks>
    Task<AuthorizationStatus> GetAuthorizationStatusAsync(
        string? cif = null,
        CancellationToken cancellationToken = default);

    /// <summary>Builds the URL a person visits to authorize a company.</summary>
    /// <param name="cif">The company to authorize, or null for the configured one.</param>
    /// <param name="returnUrl">Where to send the person once the callback completes.</param>
    /// <param name="user">
    /// Who is starting the round trip. Supply it when building the URL yourself rather than using
    /// the mapped endpoint: the callback refuses a state completed by a different person, and can
    /// only do so for a state that recorded one.
    /// </param>
    Uri BuildAuthorizationUrl(string? cif = null, string? returnUrl = null, string? user = null);

    /// <summary>Removes a company's authorization.</summary>
    Task DisconnectAsync(string? cif = null, CancellationToken cancellationToken = default);

    // ----------------------------------------------------------------- outbound

    /// <summary>
    /// Checks a document against EN16931 and CIUS-RO, entirely offline.
    /// </summary>
    /// <remarks>
    /// If this reports valid, ANAF will not reject the document on format grounds. It makes no
    /// claim about whether the submission will succeed — that depends on authorization, rights and
    /// connectivity, which come back from <see cref="SendInvoiceAsync(UblInvoice, string, UploadOptions, CancellationToken)"/>
    /// as typed results.
    /// </remarks>
    ValidationReport Verify(UblInvoice invoice);

    /// <inheritdoc cref="Verify(UblInvoice)" />
    ValidationReport Verify(UblCreditNote creditNote);

    /// <summary>
    /// Checks a filled-in invoice model, and the document it would produce.
    /// </summary>
    /// <remarks>
    /// The route most applications want. Field-level problems come back against the model's own
    /// property paths, so a form can put each one beside the input that caused it.
    /// </remarks>
    ValidationReport Verify(InvoiceEditModel invoice);

    /// <inheritdoc cref="Verify(InvoiceEditModel)" />
    ValidationReport Verify(CreditNoteEditModel creditNote);

    /// <inheritdoc cref="Verify(InvoiceEditModel)" />
    ValidationReport Verify(BuyerMessageEditModel message);

    /// <summary>
    /// Submits an invoice and records it for reconciliation.
    /// </summary>
    /// <remarks>
    /// Returns as soon as ANAF accepts the upload, which takes seconds. The outcome takes minutes
    /// to hours and is settled in the background, so a caller must not wait on it here.
    /// </remarks>
    Task<AnafResult<SubmissionReceipt>> SendInvoiceAsync(
        UblInvoice invoice,
        string? cif = null,
        UploadOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="SendInvoiceAsync(UblInvoice, string, UploadOptions, CancellationToken)" />
    Task<AnafResult<SubmissionReceipt>> SendCreditNoteAsync(
        UblCreditNote creditNote,
        string? cif = null,
        UploadOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a filled-in invoice model, mapping it to UBL along the way.
    /// </summary>
    /// <remarks>
    /// Verified first, exactly as <see cref="Verify(InvoiceEditModel)"/> would, so an invalid model
    /// comes back as a validation result rather than reaching ANAF.
    /// </remarks>
    Task<AnafResult<SubmissionReceipt>> SendInvoiceAsync(
        InvoiceEditModel invoice,
        string? cif = null,
        UploadOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc cref="SendInvoiceAsync(InvoiceEditModel, string, UploadOptions, CancellationToken)" />
    Task<AnafResult<SubmissionReceipt>> SendCreditNoteAsync(
        CreditNoteEditModel creditNote,
        string? cif = null,
        UploadOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a buyer's message back to the seller of an invoice already received (RASP).
    /// </summary>
    /// <remarks>
    /// How a buyer disputes an invoice inside e-Factura rather than by email. It travels the same
    /// upload path as a document and is reconciled the same way.
    /// </remarks>
    Task<AnafResult<SubmissionReceipt>> SendBuyerMessageAsync(
        BuyerMessageEditModel message,
        string? cif = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a document the library cannot express, bypassing its models.
    /// </summary>
    /// <remarks>
    /// The escape hatch. <see cref="Verify(UblInvoice)"/> is not applied, so the guarantee that
    /// ANAF will accept the format does not extend to anything sent this way.
    /// </remarks>
    Task<AnafResult<SubmissionReceipt>> SendRawXmlAsync(
        string xml,
        string? cif = null,
        UploadOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reports where a submission has got to, from local records.</summary>
    /// <remarks>
    /// Reads what the reconciler has already established rather than calling ANAF, so it is free
    /// to call as often as a page needs and cannot exhaust the daily allowance.
    /// </remarks>
    /// <param name="uploadIndex">ANAF's <c>index_incarcare</c>.</param>
    /// <param name="cif">
    /// Whose submission it is. One belonging to another company is not returned, so an application
    /// serving several cannot be made to hand one over by naming its index.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<SubmissionStatus?> GetSubmissionAsync(
        string uploadIndex,
        string? cif = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists recent submissions for a company, newest first.</summary>
    Task<IReadOnlyList<SubmissionStatus>> GetSubmissionsAsync(
        string? cif = null,
        int take = 50,
        CancellationToken cancellationToken = default);

    // ------------------------------------------------------------------ inbound

    /// <summary>
    /// Reads the SPV inbox and records anything new.
    /// </summary>
    /// <remarks>
    /// Resumes from a stored watermark rather than re-reading the whole window, and skips messages
    /// already held. Also runs on a schedule in the background, so calling it is a way to refresh
    /// on demand rather than the only way messages arrive.
    /// </remarks>
    Task<AnafResult<InboxSyncResult>> SyncInboxAsync(
        string? cif = null,
        CancellationToken cancellationToken = default);

    /// <summary>Lists messages known for a company, newest first.</summary>
    Task<IReadOnlyList<InboxMessage>> GetInboxAsync(
        string? cif = null,
        int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a message's document, parsed.
    /// </summary>
    /// <remarks>
    /// The archive behind a message is frequently not an invoice — it may be a credit note, a
    /// debit note, a validation error report, or a buyer message — so the result is discriminated
    /// rather than assumed.
    /// </remarks>
    /// <param name="downloadId">ANAF's download identifier.</param>
    /// <param name="cif">Whose message it is. See <see cref="GetArchiveAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<AnafResult<EFacturaDocument>> GetDocumentAsync(
        string downloadId,
        string? cif = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a message's raw archive, holding the document and the ministry's signature.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Served from local storage when it has already been fetched, so repeated access does not
    /// spend the daily download allowance.
    /// </para>
    /// <para>
    /// Which is why <paramref name="cif"/> matters. ANAF enforces rights on a download, but a
    /// cached archive never reaches ANAF, so the check happens here or not at all.
    /// </para>
    /// </remarks>
    /// <param name="downloadId">ANAF's download identifier.</param>
    /// <param name="cif">Whose message it is, defaulting to the configured company.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<AnafResult<byte[]>> GetArchiveAsync(
        string downloadId,
        string? cif = null,
        CancellationToken cancellationToken = default);

    // ------------------------------------------------------------------ utility

    /// <summary>Renders a document to PDF using ANAF's converter.</summary>
    /// <param name="downloadId">ANAF's download identifier.</param>
    /// <param name="cif">Whose message it is. See <see cref="GetArchiveAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<AnafResult<byte[]>> RenderPdfAsync(
        string downloadId,
        string? cif = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Whether a company can talk to ANAF, and for how much longer.</summary>
/// <param name="Cif">The company.</param>
/// <param name="IsConnected">Whether an authorization is stored and usable.</param>
/// <param name="AccessTokenExpiresAt">When the current access token lapses, if there is one.</param>
/// <param name="ConnectedSince">When the company was first authorized.</param>
public sealed record AuthorizationStatus(
    string Cif,
    bool IsConnected,
    DateTimeOffset? AccessTokenExpiresAt,
    DateTimeOffset? ConnectedSince)
{
    /// <summary>An unauthorized company.</summary>
    public static AuthorizationStatus NotConnected(string cif) => new(cif, false, null, null);
}

/// <summary>What a caller gets back when ANAF accepts a submission.</summary>
/// <param name="UploadIndex">ANAF's handle on the submission. Worth showing to a user.</param>
/// <param name="Cif">The submitting company.</param>
/// <param name="SubmittedAt">When ANAF accepted it.</param>
public sealed record SubmissionReceipt(string UploadIndex, string Cif, DateTimeOffset SubmittedAt);

/// <summary>Where a submission has got to.</summary>
/// <param name="UploadIndex">ANAF's handle on the submission.</param>
/// <param name="Cif">The submitting company.</param>
/// <param name="DocumentId">The document number, where it is known.</param>
/// <param name="State">How far ANAF has got.</param>
/// <param name="SubmittedAt">When it was sent.</param>
/// <param name="ResolvedAt">When the outcome became known.</param>
/// <param name="HasArchive">Whether the signed response has been retrieved and stored.</param>
/// <param name="LastError">The most recent problem, if the reconciler hit one.</param>
/// <param name="NextPollAt">
/// When ANAF will next be asked about this submission. Worth showing beside a pending document:
/// the schedule widens deliberately to stay inside the daily allowance, so a page that offers to
/// reconcile on demand needs to explain why nothing happened when nothing was due.
/// </param>
public sealed record SubmissionStatus(
    string UploadIndex,
    string Cif,
    string? DocumentId,
    UploadState State,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? ResolvedAt,
    bool HasArchive,
    string? LastError,
    DateTimeOffset NextPollAt)
{
    /// <summary>Whether ANAF accepted the document and it reached the buyer.</summary>
    public bool IsAccepted => State == UploadState.Ok;

    /// <summary>Whether ANAF rejected it. The archive holds the reasons.</summary>
    public bool IsRejected => State is UploadState.Nok or UploadState.RejectedAtUpload;

    /// <summary>Whether ANAF is still deciding.</summary>
    public bool IsPending => State == UploadState.InProgress;
}

/// <summary>A message in the local record of the SPV inbox.</summary>
/// <param name="DownloadId">ANAF's identifier for the message.</param>
/// <param name="Cif">The company it belongs to.</param>
/// <param name="Type">The message type as ANAF words it.</param>
/// <param name="RequestId">The upload this responds to, when it responds to one.</param>
/// <param name="SupplierCif">The seller.</param>
/// <param name="CustomerCif">The buyer.</param>
/// <param name="CreatedAt">When ANAF created it.</param>
/// <param name="IsDownloaded">Whether the archive has been fetched.</param>
public sealed record InboxMessage(
    string DownloadId,
    string Cif,
    string? Type,
    string? RequestId,
    string? SupplierCif,
    string? CustomerCif,
    DateTimeOffset? CreatedAt,
    bool IsDownloaded)
{
    /// <summary>Whether this message carries validation errors rather than a document.</summary>
    public bool IsError => Type?.Contains("ERORI", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>What an inbox sync found.</summary>
/// <param name="Cif">The company synced.</param>
/// <param name="NewMessages">How many messages were seen for the first time.</param>
/// <param name="AlreadyKnown">How many were already held and therefore not downloaded again.</param>
/// <param name="SyncedUpTo">The new watermark.</param>
public sealed record InboxSyncResult(string Cif, int NewMessages, int AlreadyKnown, DateTimeOffset SyncedUpTo);
