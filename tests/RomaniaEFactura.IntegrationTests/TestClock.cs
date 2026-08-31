namespace RomaniaEFactura.IntegrationTests;

/// <summary>A clock the test moves by hand, so a simulated day takes milliseconds.</summary>
/// <remarks>
/// Shared by every suite that needs one, because the library reads time through
/// <see cref="TimeProvider"/> from the container: a test holding its own private clock would move
/// only its own copy, and the service and the reconciler would disagree about what is due.
/// </remarks>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
