using System.Collections.Concurrent;
using System.Text;

namespace RomaniaEFactura.LiveTests;

/// <summary>
/// Collects what the real service actually did, and writes it out at the end of the run.
/// </summary>
/// <remarks>
/// <para>
/// A live run's value is not only whether it passed. It is the answers: how long ANAF took to
/// settle a document, what the daily cap is counted against, whether the buyer-message format is
/// right. Those belong in <c>docs/anaf-wire-formats.md</c>, and this makes them easy to copy there
/// instead of scraping them out of test output.
/// </para>
/// <para>
/// Shared across the suite through a collection fixture, so a document uploaded by one test can be
/// polled by the next without sending another. That makes the tests order-dependent, which is a
/// real cost — accepted because each upload is a fiscal document in ANAF's test register and a
/// suite that sends one per test wastes both quota and register entries.
/// </para>
/// </remarks>
public sealed class LiveRunReport : IDisposable
{
    private readonly ConcurrentDictionary<string, string> _observations = new(StringComparer.Ordinal);
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;

    /// <summary>Everything recorded so far.</summary>
    public IReadOnlyDictionary<string, string> Observations => _observations;

    /// <summary>The upload index from the lifecycle test, for later tests to reuse.</summary>
    public string? UploadIndex { get; set; }

    /// <summary>The download identifier once a document has resolved.</summary>
    public string? DownloadId { get; set; }

    /// <summary>Records one observation, replacing any earlier one under the same key.</summary>
    public void Record(string key, string observation) => _observations[key] = observation;

    /// <summary>Writes the report where a person will find it.</summary>
    public void Dispose()
    {
        if (_observations.IsEmpty) return;

        var report = new StringBuilder()
            .AppendLine("# ANAF live test-environment run")
            .AppendLine()
            .AppendLine($"Started {_started:u}, finished {DateTimeOffset.UtcNow:u}.")
            .AppendLine($"Company: {LiveAnaf.Cif}. Environment: ANAF test.")
            .AppendLine()
            .AppendLine("| Observation | What the service did |")
            .AppendLine("|---|---|");

        foreach (var (key, value) in _observations.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            report.AppendLine($"| `{key}` | {value.Replace("|", "\\|", StringComparison.Ordinal)} |");
        }

        report.AppendLine()
            .AppendLine("Copy anything here that contradicts `docs/anaf-wire-formats.md` into that")
            .AppendLine("document, and fix the mock to match — never the client.");

        var path = Path.Combine(AppContext.BaseDirectory, "live-run-report.md");
        File.WriteAllText(path, report.ToString());

        // Written to the console too, because the file lands in bin/ where nobody looks.
        Console.WriteLine();
        Console.WriteLine(report.ToString());
        Console.WriteLine($"(also written to {path})");
    }
}

/// <summary>
/// Runs the live suite as one sequence sharing a single report.
/// </summary>
/// <remarks>
/// Serialized deliberately. Tests hitting ANAF in parallel would interleave their quota usage and
/// make the timing measurements meaningless.
/// </remarks>
[CollectionDefinition(nameof(LiveRunCollection), DisableParallelization = true)]
public sealed class LiveRunCollection : ICollectionFixture<LiveRunReport>;
