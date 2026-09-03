using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.Lookup;

namespace RomaniaEFactura.Tests.Lookup;

/// <summary>
/// The one-request-per-second limit ANAF publishes with the taxpayer register.
/// </summary>
/// <remarks>
/// <para>
/// It is a limit on the client, not on the subject — unlike the e-Factura endpoints, where the
/// pacing is per company. Two lookups about different companies still have to be a second apart.
/// </para>
/// <para>
/// Tested with a clock and a delay the test controls, so the suite does not spend real seconds
/// proving arithmetic.
/// </para>
/// </remarks>
public class LookupPacingTests
{
    [Fact]
    public async Task ASecondBatchWaitsOutTheGap()
    {
        // A hundred and one codes is two batches, which is the smallest thing that has to pace.
        var clock = new TestClock();
        var client = CreateClient(clock, out var waits);

        await client.LookupAsync(Codes(101));

        var wait = Assert.Single(waits);
        Assert.True(wait > TimeSpan.Zero, $"Expected a wait before the second batch, got {wait}.");
        Assert.True(wait <= TimeSpan.FromSeconds(1), $"Waited {wait}, which is longer than the limit requires.");
    }

    [Fact]
    public async Task OneBatchDoesNotWait()
    {
        var clock = new TestClock();
        var client = CreateClient(clock, out var waits);

        // Far enough past the last call that nothing is owed.
        clock.Advance(TimeSpan.FromMinutes(1));

        await client.LookupAsync(Codes(10));

        Assert.Empty(waits);
    }

    [Fact]
    public async Task TheGapIsMeasuredFromTheLastCallRatherThanSleptUnconditionally()
    {
        var clock = new TestClock();
        var client = CreateClient(clock, out var waits);

        await client.LookupAsync(Codes(1));
        clock.Advance(TimeSpan.FromSeconds(5));
        await client.LookupAsync(Codes(1));

        Assert.Empty(waits);
    }

    // ------------------------------------------------------------- the harness

    private static IEnumerable<string> Codes(int count) =>
        Enumerable.Range(0, count).Select(SyntheticCif);

    private static string SyntheticCif(int seed)
    {
        ReadOnlySpan<byte> weights = [7, 5, 3, 2, 1, 7, 5, 3, 2];
        var body = (30_000_000 + seed).ToString(System.Globalization.CultureInfo.InvariantCulture);

        var sum = 0;
        var offset = weights.Length - body.Length;
        for (var i = 0; i < body.Length; i++) sum += (body[i] - '0') * weights[offset + i];

        var control = sum * 10 % 11;
        return body + (control == 10 ? 0 : control);
    }

    private static AnafCompanyLookupClient CreateClient(TestClock clock, out List<TimeSpan> waits)
    {
        var recorded = new List<TimeSpan>();
        waits = recorded;

        return new AnafCompanyLookupClient(
            new EmptyRegisterHttpClientFactory(),
            Options.Create(new EFacturaOptions
            {
                CompanyLookupBaseAddress = new Uri("https://register.invalid/api/PlatitorTvaRest/v9"),
            }),
            NullLogger<AnafCompanyLookupClient>.Instance)
        {
            // Its own pacing state, so a previous test in this class cannot leave this one owing a
            // wait it never incurred. The default is process-wide on purpose.
            Pacer = new LookupPacer(),
            Clock = () => clock.Now,
            Delay = (span, _) =>
            {
                recorded.Add(span);

                // The wait is what a real delay would have done, so the next call is not still owed
                // it and the test measures one wait rather than a cascade.
                clock.Advance(span);
                return Task.CompletedTask;
            },
        };
    }

    private sealed class TestClock
    {
        public DateTimeOffset Now { get; private set; } = new(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan by) => Now = Now.Add(by);
    }

    /// <summary>Answers as the register does when it knows none of the codes.</summary>
    private sealed class EmptyRegisterHttpClientFactory : IHttpClientFactory
    {
        private readonly Handler _handler = new();

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);

        private sealed class Handler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"cod":200,"message":"SUCCESS","found":[],"notFound":[]}"""),
                });
        }
    }
}
