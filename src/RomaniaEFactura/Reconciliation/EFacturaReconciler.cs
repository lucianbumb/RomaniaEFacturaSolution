using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.Persistence;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura.Reconciliation;

/// <summary>
/// Finishes what <c>SendInvoiceAsync</c> starts.
/// </summary>
/// <remarks>
/// <para>
/// A submission is accepted in seconds but decided in minutes to hours, so the outcome cannot be
/// awaited inside the request that sent it. This works through the tracked submissions, asks ANAF
/// how each is getting on, and stores the signed response once there is one.
/// </para>
/// <para>
/// It exists as a hosted service so an application gets reconciliation by registering the library,
/// without writing a scheduler. Every document it looks at is due according to
/// <see cref="PollSchedule"/>, which is what keeps the daily allowance from being spent in the
/// first few minutes.
/// </para>
/// </remarks>
public sealed class EFacturaReconciler(
    IServiceScopeFactory scopeFactory,
    IOptions<EFacturaOptions> options,
    TimeProvider time,
    ILogger<EFacturaReconciler> logger)
{
    private readonly EFacturaOptions _options = options.Value;

    /// <summary>
    /// Works through everything currently due, and returns how much it did.
    /// </summary>
    /// <remarks>
    /// Exposed separately from the hosted loop so a test can run exactly one pass, and so an
    /// application can force reconciliation on demand.
    /// </remarks>
    public async Task<ReconcileOutcome> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EFacturaDbContext>();
        var api = scope.ServiceProvider.GetRequiredService<IAnafApiClient>();

        var now = time.GetUtcNow();
        var due = await db.Submissions
            .Where(s => s.State == UploadState.InProgress && s.NextPollAt <= now)
            .OrderBy(s => s.NextPollAt)
            .Take(_options.ReconcileBatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var outcome = new ReconcileOutcome();

        foreach (var submission in due)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // A submission ANAF has not decided in a week will not be decided by asking again, and
            // every call spent on it is one unavailable to a document that could still settle.
            if (PollSchedule.ShouldGiveUp(submission.SubmittedAt, now))
            {
                submission.LastError = "ANAF did not resolve this submission within seven days.";
                submission.NextPollAt = now.AddDays(1);
                outcome.Abandoned++;
                logger.LogWarning(
                    "Submission {Index} for CIF {Cif} is still unresolved after seven days.",
                    submission.UploadIndex, submission.Cif);
                continue;
            }

            await PollAsync(db, api, submission, now, outcome, cancellationToken).ConfigureAwait(false);
        }

        if (due.Count > 0) await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return outcome;
    }

    private async Task PollAsync(
        EFacturaDbContext db,
        IAnafApiClient api,
        EFacturaSubmission submission,
        DateTimeOffset now,
        ReconcileOutcome outcome,
        CancellationToken cancellationToken)
    {
        var status = await api.GetStatusAsync(submission.UploadIndex, cancellationToken).ConfigureAwait(false);

        if (!status.IsSuccess)
        {
            HandleFailure(submission, status.Error!, now, outcome);
            return;
        }

        // The call reached ANAF's business logic, so it counted against the allowance and taught
        // something about the document. Both counters move accordingly.
        submission.PollAttempts++;
        submission.ConsecutiveFailures = 0;
        submission.LastError = null;
        outcome.Polled++;

        if (!status.Value.IsComplete)
        {
            submission.NextPollAt = PollSchedule.NextPollAt(now, submission.PollAttempts);
            return;
        }

        submission.State = status.Value.State;
        submission.ResolvedAt = now;

        if (status.Value.DownloadId is not { } downloadId)
        {
            // Refused at upload: the reason came back from the upload call, and there is nothing
            // to download.
            outcome.Resolved++;
            return;
        }

        submission.DownloadId = downloadId;

        // The archive holds the ministry's signature, which is the proof of submission and has to
        // be retained. Fetching it immediately means it is captured while the download allowance
        // for this identifier is certainly untouched.
        var archive = await api.DownloadArchiveAsync(downloadId, cancellationToken).ConfigureAwait(false);
        if (archive.IsSuccess)
        {
            submission.Archive = archive.Value;
            outcome.Downloaded++;
        }
        else
        {
            // The status is settled either way; only the archive is outstanding, so it is retried
            // without reopening the question of what ANAF decided.
            submission.LastError = $"Resolved as {status.Value.State}, but the archive could not be downloaded: {archive.Error}";
            submission.NextPollAt = archive.Error!.Kind == AnafErrorKind.QuotaExhausted
                ? PollSchedule.AfterQuotaExhausted(now)
                : PollSchedule.AfterTransientFailure(now, submission.ConsecutiveFailures + 1);
        }

        outcome.Resolved++;

        logger.LogInformation(
            "Submission {Index} for CIF {Cif} resolved as {State}.",
            submission.UploadIndex, submission.Cif, status.Value.State);
    }

    private void HandleFailure(
        EFacturaSubmission submission,
        AnafError error,
        DateTimeOffset now,
        ReconcileOutcome outcome)
    {
        submission.LastError = error.ToString();

        switch (error.Kind)
        {
            case AnafErrorKind.QuotaExhausted:
                // The allowance does not clear gradually, so anything before midnight is refused
                // too. Crucially, PollAttempts is not advanced: nothing was learned.
                submission.NextPollAt = PollSchedule.AfterQuotaExhausted(now);
                outcome.QuotaExhausted++;
                logger.LogWarning(
                    "The daily status allowance for submission {Index} is spent; resuming tomorrow.",
                    submission.UploadIndex);
                break;

            case AnafErrorKind.NotAuthorized:
                // Nothing to be done until a person re-authorizes, so stop asking hourly.
                submission.NextPollAt = now.AddHours(1);
                outcome.Unauthorized++;
                break;

            default:
                submission.ConsecutiveFailures++;
                submission.NextPollAt = PollSchedule.AfterTransientFailure(now, submission.ConsecutiveFailures);
                outcome.Failed++;
                break;
        }
    }
}

/// <summary>What one reconciliation pass did.</summary>
public sealed class ReconcileOutcome
{
    /// <summary>How many submissions were successfully asked about.</summary>
    public int Polled { get; set; }

    /// <summary>How many reached a final state.</summary>
    public int Resolved { get; set; }

    /// <summary>How many signed archives were retrieved.</summary>
    public int Downloaded { get; set; }

    /// <summary>How many were deferred because the daily allowance was spent.</summary>
    public int QuotaExhausted { get; set; }

    /// <summary>How many could not be asked about because the company is not authorized.</summary>
    public int Unauthorized { get; set; }

    /// <summary>How many hit a transient failure.</summary>
    public int Failed { get; set; }

    /// <summary>How many were given up on as too old.</summary>
    public int Abandoned { get; set; }

    /// <summary>Whether the pass did anything at all.</summary>
    public bool DidWork => Polled + Failed + QuotaExhausted + Unauthorized + Abandoned > 0;
}
