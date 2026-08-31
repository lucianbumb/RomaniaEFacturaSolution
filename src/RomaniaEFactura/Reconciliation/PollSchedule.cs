namespace RomaniaEFactura.Reconciliation;

/// <summary>
/// Decides when a submitted document should next be asked about.
/// </summary>
/// <remarks>
/// <para>
/// ANAF caps <c>stareMesaj</c> at roughly twenty calls per document per day. That single fact
/// rules out the obvious implementation: a timer polling every minute exhausts the budget in
/// twenty minutes and then cannot see its own document for the rest of the day — including the
/// moment it is actually finished.
/// </para>
/// <para>
/// So the interval widens instead. The schedule below spends ten calls covering the first day,
/// densely at the start where most documents resolve, and sparsely afterwards. That leaves half
/// the daily allowance unspent as headroom for manual checks and for a document that needs a
/// second day.
/// </para>
/// <para>
/// The exact cap is <b>unconfirmed against production</b> — its scope may be per document, per
/// company, or per application — so the budget is configurable rather than baked in. Confirming it
/// is part of the real-environment milestone.
/// </para>
/// </remarks>
public static class PollSchedule
{
    /// <summary>
    /// The gap before each successive poll. Dense early, because most documents resolve within
    /// minutes; wide later, because one that has not resolved in an hour is unlikely to in the
    /// next one.
    /// </summary>
    private static readonly TimeSpan[] Intervals =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(20),
        TimeSpan.FromMinutes(40),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(4),
        TimeSpan.FromHours(6),
    ];

    /// <summary>The widest gap, used once the schedule above is exhausted.</summary>
    private static readonly TimeSpan LongestInterval = TimeSpan.FromHours(8);

    /// <summary>How many calls the schedule spends in its first twenty-four hours.</summary>
    public static int CallsInFirstDay => Intervals.Length + 1;

    /// <summary>When to poll next, given how many times a document has already been asked about.</summary>
    /// <param name="now">The current time.</param>
    /// <param name="attemptsSoFar">How many polls have already been made for this document.</param>
    public static DateTimeOffset NextPollAt(DateTimeOffset now, int attemptsSoFar)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attemptsSoFar);

        var interval = attemptsSoFar < Intervals.Length ? Intervals[attemptsSoFar] : LongestInterval;
        return now + interval;
    }

    /// <summary>
    /// How long to leave a document alone once its daily allowance is spent.
    /// </summary>
    /// <remarks>
    /// Returns the start of the next day rather than a fixed delay. A spent quota does not clear
    /// gradually, so retrying before midnight only burns calls that will also be refused.
    /// </remarks>
    public static DateTimeOffset AfterQuotaExhausted(DateTimeOffset now) =>
        new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(1);

    /// <summary>
    /// When to retry after a transient failure — ANAF unreachable, or rate limiting.
    /// </summary>
    /// <remarks>
    /// Deliberately short and not counted as a poll attempt: nothing was learned about the
    /// document, so the schedule should not advance and the daily budget should not be charged for
    /// a call that never reached ANAF's business logic.
    /// </remarks>
    public static DateTimeOffset AfterTransientFailure(DateTimeOffset now, int consecutiveFailures)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveFailures);

        // 1, 2, 4, 8… minutes, capped so a long outage does not push the next attempt days out.
        var minutes = Math.Min(Math.Pow(2, Math.Min(consecutiveFailures, 10)), 60);
        return now.AddMinutes(minutes);
    }

    /// <summary>
    /// Whether a document has been polled for so long that a human should look at it.
    /// </summary>
    /// <remarks>
    /// ANAF normally resolves a submission within minutes. One still unresolved after several days
    /// usually means something is wrong that more polling will not fix, and continuing to spend
    /// calls on it takes budget from documents that could still be reconciled.
    /// </remarks>
    public static bool ShouldGiveUp(DateTimeOffset submittedAt, DateTimeOffset now) =>
        now - submittedAt > TimeSpan.FromDays(7);
}
