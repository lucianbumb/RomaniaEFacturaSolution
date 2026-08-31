using RomaniaEFactura.Reconciliation;

namespace RomaniaEFactura.Tests.Reconciliation;

/// <summary>
/// The polling schedule, which is what keeps reconciliation inside ANAF's daily allowance.
/// </summary>
/// <remarks>
/// ANAF caps <c>stareMesaj</c> at roughly twenty calls per document per day. The obvious
/// implementation — a timer polling every minute — exhausts that in twenty minutes and then cannot
/// see its own document for the rest of the day, including the moment it actually finishes.
/// </remarks>
public class PollScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheFirstDayFitsComfortablyInsideAnafsAllowance()
    {
        // Walk a whole day of the schedule and count the calls it would make.
        var at = Now;
        var calls = 0;

        while (at < Now.AddDays(1))
        {
            at = PollSchedule.NextPollAt(at, calls);
            calls++;
        }

        // Twenty is the observed cap. Staying well inside it leaves headroom for a manual check
        // and for the download the resolution triggers.
        Assert.InRange(calls, 5, 12);
    }

    [Fact]
    public void IntervalsWidenSoEarlyPollsAreDenseAndLaterOnesAreNot()
    {
        // Most documents resolve within minutes, so the early polls are worth spending; one still
        // unresolved after an hour is unlikely to resolve in the next.
        var gaps = Enumerable.Range(0, 12)
            .Select(attempt => PollSchedule.NextPollAt(Now, attempt) - Now)
            .ToList();

        Assert.Equal(TimeSpan.FromMinutes(1), gaps[0]);
        for (var i = 1; i < gaps.Count; i++)
        {
            Assert.True(gaps[i] >= gaps[i - 1],
                $"Interval {i} ({gaps[i]}) is shorter than interval {i - 1} ({gaps[i - 1]}).");
        }
    }

    [Fact]
    public void TheIntervalIsCappedRatherThanGrowingWithoutBound()
    {
        var far = PollSchedule.NextPollAt(Now, attemptsSoFar: 100) - Now;

        Assert.Equal(TimeSpan.FromHours(8), far);
    }

    [Fact]
    public void AnExhaustedQuotaDefersUntilTheNextDayRatherThanRetryingSooner()
    {
        // A spent allowance does not clear gradually, so anything before midnight is refused too
        // and only burns calls that will also fail.
        var next = PollSchedule.AfterQuotaExhausted(Now);

        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), next);
        Assert.True(next > Now);
    }

    [Fact]
    public void ATransientFailureRetriesQuicklyAndBacksOff()
    {
        var first = PollSchedule.AfterTransientFailure(Now, 1) - Now;
        var third = PollSchedule.AfterTransientFailure(Now, 3) - Now;

        Assert.True(first < third);
        Assert.True(first <= TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void TransientBackoffIsCappedSoAnOutageDoesNotPushRetriesDaysOut()
    {
        var afterLongOutage = PollSchedule.AfterTransientFailure(Now, 50) - Now;

        Assert.Equal(TimeSpan.FromMinutes(60), afterLongOutage);
    }

    [Fact]
    public void ASubmissionIsAbandonedOnlyAfterSeveralDays()
    {
        // ANAF normally resolves within minutes. Continuing to poll one stuck for a week takes
        // allowance from documents that could still settle.
        Assert.False(PollSchedule.ShouldGiveUp(Now.AddDays(-3), Now));
        Assert.True(PollSchedule.ShouldGiveUp(Now.AddDays(-8), Now));
    }

    [Fact]
    public void ANegativeAttemptCountIsAProgrammingError()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PollSchedule.NextPollAt(Now, -1));
    }
}
