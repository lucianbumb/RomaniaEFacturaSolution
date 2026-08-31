using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura.Tests.Transport;

/// <summary>
/// What it takes to add an entry to the per-company pacing state, and what a malformed CIF does.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AnafApiClient"/> keeps two process-wide dictionaries keyed by CIF, and neither ever
/// evicts. That looks like unbounded growth keyed by caller-controlled input — the per-call CIF
/// override is routinely derived from request state in a multi-tenant application.
/// </para>
/// <para>
/// It is not, and the reason is worth pinning down rather than leaving to a reading of the code:
/// every path to a <c>GetOrAdd</c> runs <em>after</em> a token lookup that returns early when the
/// company has no stored authorization. An entry therefore costs an attacker a completed ANAF
/// authorization for that company — a qualified certificate each — so the dictionaries are bounded
/// by the companies the deployment actually serves.
/// </para>
/// <para>
/// The first test measures that directly, because it is the load-bearing fact and the ordering it
/// depends on is easy to reverse by accident.
/// </para>
/// </remarks>
public class CompanyGateTests
{
    [Fact]
    public async Task AnUnauthorizedCompanyLeavesNoTrace()
    {
        // Fifty distinct companies, none of them authorized. If an entry appeared for any of them,
        // the same request repeated with fresh values would grow the dictionary without limit.
        var http = new RecordingHttpClientFactory();
        var client = CreateClient(token: null, http: http);
        var before = GateKeys();

        for (var i = 0; i < 50; i++)
        {
            var result = await client.ListMessagesAsync(days: 7, cif: SyntheticCif(i));

            Assert.False(result.IsSuccess);
            Assert.Equal(AnafErrorKind.NotAuthorized, result.Error!.Kind);
        }

        Assert.Equal(before, GateKeys());
    }

    [Fact]
    public async Task AnUnauthorizedCompanyCostsNoHttpCall()
    {
        // The other half of the same fact, and the reason the daily allowance is not spent finding
        // out something the local store already knew.
        var http = new RecordingHttpClientFactory();
        var client = CreateClient(token: null, http: http);

        var result = await client.ListMessagesAsync(days: 7, cif: SyntheticCif(1));

        Assert.Equal(AnafErrorKind.NotAuthorized, result.Error!.Kind);
        Assert.Equal(0, http.Calls);
    }

    // -------------------------------------------------------- a malformed CIF

    [Fact]
    public async Task AMalformedCifIsRefusedLocally()
    {
        // 12345678 has a correct-looking shape and a wrong control digit. Sent to ANAF it comes
        // back as a sentence in Romanian, having spent a call from the daily allowance to say what
        // the control digit already said.
        var client = CreateClient();

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => client.ListMessagesAsync(days: 7, cif: "12345678"));

        Assert.Contains("control digit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMalformedCifNeverBecomesAKey()
    {
        var client = CreateClient();
        var before = GateKeys();

        await Assert.ThrowsAsync<ArgumentException>(() => client.ListMessagesAsync(days: 7, cif: "not-a-cif"));

        Assert.Equal(before, GateKeys());
    }

    [Fact]
    public async Task ThePrefixedAndUnprefixedSpellingsAreOneCompany()
    {
        // Both normalise to the same key, so they share one pacing gate rather than racing each
        // other into ANAF's rate limiter.
        var client = CreateClient();
        var before = GateKeys();

        await client.ListMessagesAsync(days: 7, cif: "RO12345674");
        await client.ListMessagesAsync(days: 7, cif: " 12345674 ");

        // Asserted as a set difference rather than a count, so the shared static state cannot make
        // this pass or fail on the order the class happens to run in.
        Assert.Equal(["12345674"], GateKeys().Except(before).Order());
    }

    [Fact]
    public async Task NoCifAnywhereIsAProgrammingError()
    {
        var client = CreateClient(configuredCif: string.Empty);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ListMessagesAsync(days: 7));
    }

    // ------------------------------------------------------------- the harness

    /// <summary>A CIF with a correct control digit, derived from a seed.</summary>
    private static string SyntheticCif(int seed)
    {
        // The Ministry of Finance weights, applied to an eight digit body to find its ninth digit.
        ReadOnlySpan<byte> weights = [7, 5, 3, 2, 1, 7, 5, 3, 2];
        var body = (10_000_000 + seed).ToString(System.Globalization.CultureInfo.InvariantCulture);

        var sum = 0;
        var offset = weights.Length - body.Length;
        for (var i = 0; i < body.Length; i++) sum += (body[i] - '0') * weights[offset + i];

        var control = sum * 10 % 11;
        return body + (control == 10 ? 0 : control);
    }

    /// <summary>
    /// Reads the private static gate dictionary.
    /// </summary>
    /// <remarks>
    /// Reflection, deliberately. The claim being tested is about that dictionary and nothing else,
    /// and asserting on a proxy for it — a timing, a call count — would pass for the wrong reason
    /// the moment the ordering it depends on changed.
    /// </remarks>
    private static IReadOnlyList<string> GateKeys()
    {
        var field = typeof(AnafApiClient).GetField("CompanyGates", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                "AnafApiClient.CompanyGates is gone. If the pacing state was reshaped, this test has to "
                + "follow it — the property it guards is that an unauthorized company adds nothing.");

        return [.. ((ConcurrentDictionary<string, SemaphoreSlim>)field.GetValue(null)!).Keys.Order()];
    }

    private static AnafApiClient CreateClient(
        string? token = "token",
        string configuredCif = "12345674",
        RecordingHttpClientFactory? http = null) =>
        new(http ?? new RecordingHttpClientFactory(),
            new StubTokenProvider(token),
            Options.Create(new EFacturaOptions
            {
                Cif = configuredCif,
                ApiBaseAddress = new Uri("https://api.invalid/test/FCTEL/rest"),
                MaxRetries = 0,
                MinimumDelayBetweenCalls = TimeSpan.Zero,
            }),
            NullLogger<AnafApiClient>.Instance);

    private sealed class StubTokenProvider(string? token) : IAnafAccessTokenProvider
    {
        public Task<string?> GetAccessTokenAsync(string cif, CancellationToken cancellationToken = default) =>
            Task.FromResult(token);
    }

    /// <summary>Answers as ANAF does for an empty inbox, and counts what it was asked.</summary>
    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        private readonly CountingHandler _handler = new();

        public int Calls => _handler.Calls;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);

        private sealed class CountingHandler : HttpMessageHandler
        {
            private int _calls;

            public int Calls => Volatile.Read(ref _calls);

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _calls);

                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"eroare":"Nu exista mesaje"}"""),
                });
            }
        }
    }
}
