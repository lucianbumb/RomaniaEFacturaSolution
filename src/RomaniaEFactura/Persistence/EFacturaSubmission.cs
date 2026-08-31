using RomaniaEFactura.Transport;

namespace RomaniaEFactura.Persistence;

/// <summary>
/// A document submitted to ANAF, tracked until its outcome is known and stored.
/// </summary>
/// <remarks>
/// Submission is not one round trip: ANAF accepts the upload in seconds but takes minutes to hours
/// to decide, so the outcome has to be reconciled long after the request that sent it has ended.
/// This row is what survives in between — without it, the <c>index_incarcare</c> is lost and the
/// submission can never be matched to its result.
/// </remarks>
public sealed class EFacturaSubmission
{
    /// <summary>ANAF's <c>index_incarcare</c>, and the only handle on the submission.</summary>
    public required string UploadIndex { get; set; }

    /// <summary>The company that submitted.</summary>
    public required string Cif { get; set; }

    /// <summary>The document number, so a person can recognise this row.</summary>
    public string? DocumentId { get; set; }

    /// <summary>How far ANAF has got with it.</summary>
    public UploadState State { get; set; } = UploadState.InProgress;

    /// <summary>The identifier needed to download the response, once ANAF has produced one.</summary>
    public string? DownloadId { get; set; }

    /// <summary>When the document was submitted.</summary>
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the outcome became known.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>When the reconciler should next ask about this document.</summary>
    public DateTimeOffset NextPollAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// How many times ANAF has been asked. Drives the widening poll interval, and is what keeps
    /// the daily allowance from being spent in the first few minutes.
    /// </summary>
    public int PollAttempts { get; set; }

    /// <summary>
    /// Consecutive transient failures. Counted separately from <see cref="PollAttempts"/> because
    /// a call that never reached ANAF's business logic taught nothing about the document.
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>
    /// The signed archive ANAF returned, holding the document and the ministry's seal.
    /// </summary>
    /// <remarks>
    /// Retained because the signature is the proof of submission. Stored in the database by
    /// default since it is a few kilobytes and must not be lost; a host that would rather keep it
    /// elsewhere can read it out and clear the column.
    /// </remarks>
    public byte[]? Archive { get; set; }

    /// <summary>The last thing that went wrong, for a person diagnosing a stuck submission.</summary>
    public string? LastError { get; set; }

    /// <summary>Whether the reconciler has stopped working on this submission.</summary>
    public bool IsSettled => State is not UploadState.InProgress && (Archive is not null || DownloadId is null);
}

/// <summary>
/// A message seen in the SPV inbox, recorded so it is only ever downloaded once.
/// </summary>
/// <remarks>
/// Downloads are capped at roughly ten per identifier per day, so re-fetching a message already
/// held is not merely wasteful — it spends an allowance that may be needed for a message not yet
/// retrieved.
/// </remarks>
public sealed class EFacturaInboxMessage
{
    /// <summary>ANAF's download identifier, and the natural key for deduplication.</summary>
    public required string DownloadId { get; set; }

    /// <summary>The company the message belongs to.</summary>
    public required string Cif { get; set; }

    /// <summary>The message type as ANAF words it.</summary>
    public string? Type { get; set; }

    /// <summary>The upload index this message responds to, when it is a response to one.</summary>
    public string? RequestId { get; set; }

    /// <summary>The seller's fiscal code.</summary>
    public string? SupplierCif { get; set; }

    /// <summary>The buyer's fiscal code.</summary>
    public string? CustomerCif { get; set; }

    /// <summary>When ANAF created the message.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>When this row was first written.</summary>
    public DateTimeOffset DiscoveredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>The downloaded archive, once it has been fetched.</summary>
    public byte[]? Archive { get; set; }

    /// <summary>Whether the archive has been retrieved.</summary>
    public bool IsDownloaded => Archive is not null;
}

/// <summary>
/// How far the inbox has been read for one company.
/// </summary>
/// <remarks>
/// Without a watermark every sync would ask for the full sixty-day window, which is both slow and
/// a good way to trip ANAF's rate limiting on a company with a busy inbox.
/// </remarks>
public sealed class EFacturaInboxCursor
{
    /// <summary>The company.</summary>
    public required string Cif { get; set; }

    /// <summary>The most recent point the inbox has been read up to.</summary>
    public DateTimeOffset SyncedUpTo { get; set; }

    /// <summary>When the last successful sync ran.</summary>
    public DateTimeOffset LastSyncedAt { get; set; } = DateTimeOffset.UtcNow;
}
