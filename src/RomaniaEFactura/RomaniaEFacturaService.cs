using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Authentication;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.EditModels;
using RomaniaEFactura.Persistence;
using RomaniaEFactura.Reconciliation;
using RomaniaEFactura.Transport;
using RomaniaEFactura.Ubl;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura;

/// <summary>The default <see cref="IRomaniaEFacturaService"/>.</summary>
public sealed class RomaniaEFacturaService(
    IAnafApiClient api,
    IAnafOAuthClient oauth,
    IEFacturaTokenStore tokens,
    EFacturaDbContext db,
    IOptions<EFacturaOptions> options,
    TimeProvider time,
    ILogger<RomaniaEFacturaService> logger) : IRomaniaEFacturaService
{
    private readonly EFacturaOptions _options = options.Value;

    // ------------------------------------------------------------ authorization

    /// <inheritdoc />
    public async Task<AuthorizationStatus> GetAuthorizationStatusAsync(
        string? cif = null, CancellationToken cancellationToken = default)
    {
        var company = ResolveCif(cif);
        var token = await tokens.GetAsync(company, cancellationToken).ConfigureAwait(false);

        // An expired access token is still "connected": it refreshes silently. Only a missing
        // authorization, or one whose refresh token has gone, needs a person and a certificate.
        return token is null
            ? AuthorizationStatus.NotConnected(company)
            : new AuthorizationStatus(company, token.CanRefresh, token.AccessTokenExpiresAt, token.ObtainedAt);
    }

    /// <inheritdoc />
    public Uri BuildAuthorizationUrl(string? cif = null, string? returnUrl = null) =>
        oauth.BuildAuthorizationUrl(ResolveCif(cif), returnUrl);

    /// <inheritdoc />
    public Task DisconnectAsync(string? cif = null, CancellationToken cancellationToken = default) =>
        tokens.RemoveAsync(ResolveCif(cif), cancellationToken);

    // ----------------------------------------------------------------- outbound

    /// <inheritdoc />
    public ValidationReport Verify(UblInvoice invoice) => CiusRoValidator.Validate(invoice);

    /// <inheritdoc />
    public ValidationReport Verify(UblCreditNote creditNote) => CiusRoValidator.Validate(creditNote);

    /// <inheritdoc />
    public ValidationReport Verify(InvoiceEditModel invoice) => EditModelValidator.Validate(invoice);

    /// <inheritdoc />
    public ValidationReport Verify(CreditNoteEditModel creditNote) =>
        EditModelValidator.Validate(creditNote);

    /// <inheritdoc />
    public ValidationReport Verify(BuyerMessageEditModel message) =>
        EditModelValidator.Validate(message);

    /// <inheritdoc />
    public async Task<AnafResult<SubmissionReceipt>> SendInvoiceAsync(
        UblInvoice invoice,
        string? cif = null,
        UploadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        // Verified before it leaves, so a format defect is caught here rather than by ANAF hours
        // later. This is the whole point of offline validation.
        var report = Verify(invoice);
        if (!report.IsValid) return Invalid<SubmissionReceipt>(report);

        return await SubmitAsync(
            UblSerializer.Serialize(invoice),
            invoice.Id?.Value,
            cif,
            options ?? new UploadOptions(AnafStandard.Ubl),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AnafResult<SubmissionReceipt>> SendCreditNoteAsync(
        UblCreditNote creditNote,
        string? cif = null,
        UploadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(creditNote);

        var report = Verify(creditNote);
        if (!report.IsValid) return Invalid<SubmissionReceipt>(report);

        return await SubmitAsync(
            UblSerializer.Serialize(creditNote),
            creditNote.Id?.Value,
            cif,
            options ?? new UploadOptions(AnafStandard.CreditNote),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AnafResult<SubmissionReceipt>> SendInvoiceAsync(
        InvoiceEditModel invoice,
        string? cif = null,
        UploadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        // Validated as a model rather than as UBL, so a problem is reported against the field the
        // caller filled in instead of against an XPath into a document they never wrote.
        var report = Verify(invoice);
        if (!report.IsValid) return Invalid<SubmissionReceipt>(report);

        return await SubmitAsync(
            UblSerializer.Serialize(invoice.ToUbl()),
            invoice.Number,
            cif,
            options ?? new UploadOptions(AnafStandard.Ubl),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AnafResult<SubmissionReceipt>> SendCreditNoteAsync(
        CreditNoteEditModel creditNote,
        string? cif = null,
        UploadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(creditNote);

        var report = Verify(creditNote);
        if (!report.IsValid) return Invalid<SubmissionReceipt>(report);

        return await SubmitAsync(
            UblSerializer.Serialize(creditNote.ToUbl()),
            creditNote.Number,
            cif,
            options ?? new UploadOptions(AnafStandard.CreditNote),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AnafResult<SubmissionReceipt>> SendBuyerMessageAsync(
        BuyerMessageEditModel message,
        string? cif = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var report = Verify(message);
        if (!report.IsValid) return Invalid<SubmissionReceipt>(report);

        // The standard is not the caller's to choose here: a buyer message is only ever RASP, and
        // any other value would be refused at upload.
        return await SubmitAsync(
            message.ToXml(),
            message.UploadIndex,
            cif,
            new UploadOptions(AnafStandard.BuyerMessage),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<AnafResult<SubmissionReceipt>> SendRawXmlAsync(
        string xml,
        string? cif = null,
        UploadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        // Deliberately unverified. The guarantee about format does not extend to this path.
        return SubmitAsync(xml, documentId: null, cif, options ?? new UploadOptions(), cancellationToken);
    }

    private async Task<AnafResult<SubmissionReceipt>> SubmitAsync(
        string xml,
        string? documentId,
        string? cif,
        UploadOptions options,
        CancellationToken cancellationToken)
    {
        var company = ResolveCif(cif);
        var upload = await api.UploadAsync(xml, company, options, cancellationToken).ConfigureAwait(false);
        if (!upload.IsSuccess) return upload.CarryError<SubmissionReceipt>();

        var now = time.GetUtcNow();

        // Recorded before returning. If this row were lost the index would be too, and with it any
        // way of ever learning what ANAF decided.
        db.Submissions.Add(new EFacturaSubmission
        {
            UploadIndex = upload.Value.UploadIndex,
            Cif = company,
            DocumentId = documentId,
            State = UploadState.InProgress,
            SubmittedAt = now,
            NextPollAt = PollSchedule.NextPollAt(now, attemptsSoFar: 0),
        });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Submitted {DocumentId} for CIF {Cif}; ANAF index {Index}.",
            documentId ?? "(raw xml)", company, upload.Value.UploadIndex);

        return AnafResult<SubmissionReceipt>.Success(
            new SubmissionReceipt(upload.Value.UploadIndex, company, now));
    }

    /// <inheritdoc />
    public async Task<SubmissionStatus?> GetSubmissionAsync(
        string uploadIndex, CancellationToken cancellationToken = default)
    {
        var row = await db.Submissions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UploadIndex == uploadIndex, cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : ToStatus(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubmissionStatus>> GetSubmissionsAsync(
        string? cif = null, int take = 50, CancellationToken cancellationToken = default)
    {
        var company = ResolveCif(cif);

        return await db.Submissions
            .AsNoTracking()
            .Where(s => s.Cif == company)
            .OrderByDescending(s => s.SubmittedAt)
            .Take(take)
            .Select(s => ToStatus(s))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ inbound

    /// <inheritdoc />
    public async Task<AnafResult<InboxSyncResult>> SyncInboxAsync(
        string? cif = null, CancellationToken cancellationToken = default)
    {
        var company = ResolveCif(cif);
        var now = time.GetUtcNow();

        var cursor = await db.InboxCursors
            .FirstOrDefaultAsync(c => c.Cif == company, cancellationToken)
            .ConfigureAwait(false);

        // Resume from the watermark, or start sixty days back on a first run - which is as far as
        // ANAF will answer for anyway.
        var from = cursor?.SyncedUpTo ?? now.AddDays(-60);

        var listing = await api.ListMessagesAsync(from, now, page: 1, company, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!listing.IsSuccess) return listing.CarryError<InboxSyncResult>();

        var known = await db.InboxMessages
            .Where(m => m.Cif == company)
            .Select(m => m.DownloadId)
            .ToHashSetAsync(cancellationToken)
            .ConfigureAwait(false);

        var added = 0;
        var seen = 0;

        foreach (var message in listing.Value.Messages)
        {
            // A message with no id has to be resolved before it can be recorded, and resolving
            // costs a stareMesaj call - so it is only done for messages not already held.
            var downloadId = message.Id;
            if (string.IsNullOrEmpty(downloadId))
            {
                if (string.IsNullOrEmpty(message.RequestId)) continue;

                var resolved = await api.GetStatusAsync(message.RequestId, cancellationToken).ConfigureAwait(false);
                if (!resolved.IsSuccess || resolved.Value.DownloadId is null) continue;

                downloadId = resolved.Value.DownloadId;
            }

            if (known.Contains(downloadId))
            {
                seen++;
                continue;
            }

            db.InboxMessages.Add(new EFacturaInboxMessage
            {
                DownloadId = downloadId,
                Cif = company,
                Type = message.Type,
                RequestId = message.RequestId,
                SupplierCif = message.SupplierCif,
                CustomerCif = message.CustomerCif,
                CreatedAt = message.CreatedAt,
                DiscoveredAt = now,
            });

            known.Add(downloadId);
            added++;
        }

        if (cursor is null)
        {
            db.InboxCursors.Add(new EFacturaInboxCursor { Cif = company, SyncedUpTo = now, LastSyncedAt = now });
        }
        else
        {
            cursor.SyncedUpTo = now;
            cursor.LastSyncedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return AnafResult<InboxSyncResult>.Success(new InboxSyncResult(company, added, seen, now));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InboxMessage>> GetInboxAsync(
        string? cif = null, int take = 100, CancellationToken cancellationToken = default)
    {
        var company = ResolveCif(cif);

        var rows = await db.InboxMessages
            .AsNoTracking()
            .Where(m => m.Cif == company)
            .OrderByDescending(m => m.CreatedAt ?? m.DiscoveredAt)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. rows.Select(m => new InboxMessage(
            m.DownloadId, m.Cif, m.Type, m.RequestId, m.SupplierCif, m.CustomerCif,
            m.CreatedAt, m.Archive is not null))];
    }

    /// <inheritdoc />
    public async Task<AnafResult<byte[]>> GetArchiveAsync(
        string downloadId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadId);

        // Served locally where possible: downloads are capped at roughly ten per identifier per
        // day, so re-fetching something already held could deny an allowance to a message that
        // has never been retrieved.
        var stored = await db.InboxMessages
            .FirstOrDefaultAsync(m => m.DownloadId == downloadId, cancellationToken)
            .ConfigureAwait(false);

        if (stored?.Archive is { } cached) return AnafResult<byte[]>.Success(cached);

        var submission = await db.Submissions
            .FirstOrDefaultAsync(s => s.DownloadId == downloadId, cancellationToken)
            .ConfigureAwait(false);

        if (submission?.Archive is { } fromSubmission) return AnafResult<byte[]>.Success(fromSubmission);

        var downloaded = await api.DownloadArchiveAsync(downloadId, cancellationToken).ConfigureAwait(false);
        if (!downloaded.IsSuccess) return downloaded;

        if (stored is not null)
        {
            stored.Archive = downloaded.Value;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return downloaded;
    }

    /// <inheritdoc />
    public async Task<AnafResult<EFacturaDocument>> GetDocumentAsync(
        string downloadId, CancellationToken cancellationToken = default)
    {
        var archive = await GetArchiveAsync(downloadId, cancellationToken).ConfigureAwait(false);
        if (!archive.IsSuccess) return archive.CarryError<EFacturaDocument>();

        try
        {
            return AnafResult<EFacturaDocument>.Success(EFacturaArchiveReader.Read(archive.Value));
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException)
        {
            return AnafResult<EFacturaDocument>.Failure(new AnafError(
                AnafErrorKind.Unreadable, $"The downloaded archive could not be read: {ex.Message}"));
        }
    }

    // ------------------------------------------------------------------ utility

    /// <inheritdoc />
    public async Task<AnafResult<byte[]>> RenderPdfAsync(
        string downloadId, CancellationToken cancellationToken = default)
    {
        var document = await GetDocumentAsync(downloadId, cancellationToken).ConfigureAwait(false);
        if (!document.IsSuccess) return document.CarryError<byte[]>();

        // Some archives already carry a rendering, which saves a call entirely.
        if (document.Value.Pdf is { } embedded) return AnafResult<byte[]>.Success(embedded);

        var standard = document.Value.Kind == EFacturaDocumentKind.CreditNote
            ? AnafStandard.CreditNote
            : AnafStandard.Ubl;

        return await api.RenderPdfAsync(document.Value.Xml, standard, skipValidation: true, cancellationToken)
            .ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- helpers

    private static SubmissionStatus ToStatus(EFacturaSubmission s) => new(
        s.UploadIndex, s.Cif, s.DocumentId, s.State, s.SubmittedAt, s.ResolvedAt,
        s.Archive != null, s.LastError, s.NextPollAt);

    /// <summary>
    /// Turns a failed offline validation into a result, so a caller handles it the same way as any
    /// other refusal rather than through a separate exception path.
    /// </summary>
    private static AnafResult<T> Invalid<T>(ValidationReport report) =>
        AnafResult<T>.Failure(new AnafError(
            AnafErrorKind.InvalidRequest,
            "The document does not satisfy CIUS-RO: "
            + string.Join("; ", report.Errors.Select(e => e.ToString()))));

    private string ResolveCif(string? cif)
    {
        var resolved = RomanianCif.Normalize(string.IsNullOrWhiteSpace(cif) ? _options.Cif : cif);

        return string.IsNullOrEmpty(resolved)
            ? throw new InvalidOperationException(
                "No CIF was supplied and none is configured. Set EFacturaOptions.Cif or pass one per call.")
            : resolved;
    }
}
