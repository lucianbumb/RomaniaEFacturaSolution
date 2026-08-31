using System.IO.Compression;
using Microsoft.Extensions.Logging.Abstractions;
using MockAnafServer;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura.IntegrationTests;

/// <summary>
/// Drives the transport client against the mock, end to end and through every documented failure.
/// </summary>
/// <remarks>
/// The point of these tests is that ANAF's failures arrive as typed results rather than
/// exceptions. A caller has to render "nobody has authorized this company yet" on a page; it is an
/// ordinary state, not a bug.
/// </remarks>
public class AnafApiClientTests(MockAnafFixture fixture) : IClassFixture<MockAnafFixture>, IAsyncLifetime
{
    private const string SampleInvoice = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"><ID>FCT-1</ID></Invoice>
        """;

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------- happy path

    [Fact]
    public async Task FullLifecycle_UploadPollDownload()
    {
        var client = CreateClient();

        var upload = await client.UploadAsync(SampleInvoice);
        Assert.True(upload.IsSuccess, upload.ToString());
        Assert.Matches("^[0-9]+$", upload.Value.UploadIndex);

        var status = await client.GetStatusAsync(upload.Value.UploadIndex);
        Assert.True(status.IsSuccess, status.ToString());
        Assert.Equal(UploadState.Ok, status.Value.State);
        Assert.True(status.Value.IsComplete);

        var archive = await client.DownloadArchiveAsync(status.Value.DownloadId!);
        Assert.True(archive.IsSuccess, archive.ToString());

        using var zip = new ZipArchive(new MemoryStream(archive.Value), ZipArchiveMode.Read);
        Assert.Contains(zip.Entries, e => e.Name.StartsWith("semnatura_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetStatus_ReportsInProgressUntilTheDocumentResolves()
    {
        await fixture.SetPollsBeforeResolutionAsync(1);
        var client = CreateClient();
        var upload = await client.UploadAsync(SampleInvoice);

        var first = await client.GetStatusAsync(upload.Value.UploadIndex);
        var second = await client.GetStatusAsync(upload.Value.UploadIndex);

        Assert.Equal(UploadState.InProgress, first.Value.State);
        Assert.False(first.Value.IsComplete);
        Assert.Null(first.Value.DownloadId);

        Assert.Equal(UploadState.Ok, second.Value.State);
        Assert.NotNull(second.Value.DownloadId);
    }

    [Fact]
    public async Task RejectedDocument_ResolvesToNokButStillHasAResponseToDownload()
    {
        var client = CreateClient(scenario: nameof(MockScenario.UploadWillFailValidation));

        var upload = await client.UploadAsync(SampleInvoice);
        var status = await client.GetStatusAsync(upload.Value.UploadIndex);

        Assert.Equal(UploadState.Nok, status.Value.State);
        // A rejection is still downloadable - the archive holds the errors and the MF signature.
        Assert.NotNull(status.Value.DownloadId);
    }

    // -------------------------------------------- failures are results, not exceptions

    [Fact]
    public async Task Upload_WithoutSpvRights_IsATypedResultNotAnException()
    {
        var client = CreateClient(scenario: nameof(MockScenario.NoSpvRights));

        var result = await client.UploadAsync(SampleInvoice);

        // ANAF reported this inside an HTTP 200; the envelope reader is what catches it.
        Assert.False(result.IsSuccess);
        Assert.Equal(AnafErrorKind.NoRights, result.Error!.Kind);
        Assert.Contains("SPV", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Download_WhenNotEntitled_PreservesAnafsReasonInsteadOfAZipParsingFailure()
    {
        var client = CreateClient(scenario: nameof(MockScenario.NotEntitled));

        var result = await client.DownloadArchiveAsync("3001000001");

        // The defect this milestone exists to fix: ANAF answers 200 with a JSON error body where a
        // ZIP was expected. Handing that to ZipArchive throws InvalidDataException and loses the
        // reason entirely.
        Assert.False(result.IsSuccess);
        Assert.Equal(AnafErrorKind.NoRights, result.Error!.Kind);
        Assert.Contains("Nu aveti dreptul sa descarcati", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_WhenQuotaIsSpent_IsDistinguishedFromARateLimit()
    {
        var client = CreateClient(scenario: nameof(MockScenario.QuotaExhausted));

        var result = await client.GetStatusAsync("5001000001");

        // A spent daily budget is not a rate limit: it does not clear in seconds, so retrying is
        // pointless and the distinction has to survive into the caller.
        Assert.False(result.IsSuccess);
        Assert.Equal(AnafErrorKind.QuotaExhausted, result.Error!.Kind);
        Assert.False(result.Error.IsTransient);
    }

    [Fact]
    public async Task WithoutAToken_ReportsNotAuthorizedWithoutCallingAnaf()
    {
        var client = CreateClient(token: null);

        var result = await client.UploadAsync(SampleInvoice);

        // Nobody having authorized this company yet is an ordinary state a page must render.
        Assert.False(result.IsSuccess);
        Assert.Equal(AnafErrorKind.NotAuthorized, result.Error!.Kind);
    }

    [Fact]
    public async Task ExpiredToken_ReportsNotAuthorized()
    {
        var client = CreateClient(scenario: nameof(MockScenario.TokenExpired));

        var result = await client.GetStatusAsync("5001000001");

        Assert.False(result.IsSuccess);
        Assert.Equal(AnafErrorKind.NotAuthorized, result.Error!.Kind);
    }

    [Fact]
    public async Task ServerError_IsRetriedAndThenReportedAsUnavailable()
    {
        var client = CreateClient(scenario: nameof(MockScenario.ServerError), maxRetries: 1);

        var result = await client.GetStatusAsync("5001000001");

        Assert.False(result.IsSuccess);
        Assert.Equal(AnafErrorKind.ServiceUnavailable, result.Error!.Kind);
        Assert.True(result.Error.IsTransient);
    }

    [Fact]
    public async Task RateLimit_IsRetriedAndThenReportedAsTransient()
    {
        var client = CreateClient(scenario: nameof(MockScenario.RateLimited), maxRetries: 1);

        var result = await client.GetStatusAsync("5001000001");

        Assert.False(result.IsSuccess);
        Assert.Equal(AnafErrorKind.RateLimited, result.Error!.Kind);
        Assert.True(result.Error.IsTransient);
    }

    // ------------------------------------------------------------ message list

    [Fact]
    public async Task EmptyInbox_IsAnEmptyListNotAnError()
    {
        var client = CreateClient();

        var result = await client.ListMessagesAsync(days: 30);

        // ANAF reports an empty inbox through the same "eroare" field it uses for real failures.
        // Treating that as a fault would make a quiet day look like an outage.
        Assert.True(result.IsSuccess, result.ToString());
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task ListMessages_ParsesEveryFieldIncludingAnafsTimestampFormat()
    {
        await fixture.SeedIncomingMessageAsync(SampleInvoice);
        var client = CreateClient();

        var result = await client.ListMessagesAsync(days: 30);

        var message = Assert.Single(result.Value);
        Assert.False(message.NeedsIdResolution);
        Assert.Equal(MockAnafFixture.Cif, message.Cif);
        Assert.Equal("FACTURA PRIMITA", message.Type);
        Assert.NotNull(message.SupplierCif);

        // data_creare is yyyyMMddHHmm as a string, never an ISO date.
        Assert.NotNull(message.CreatedAt);
        Assert.Equal(DateTimeOffset.UtcNow.Year, message.CreatedAt!.Value.Year);
    }

    [Fact]
    public async Task MessageWithoutAnId_IsFlaggedSoItCanBeResolvedThroughStatus()
    {
        var seeded = await fixture.SeedIncomingMessageAsync(SampleInvoice, hideId: true);
        var client = CreateClient();

        var list = await client.ListMessagesAsync(days: 30);
        var message = Assert.Single(list.Value);

        Assert.True(message.NeedsIdResolution);
        Assert.Null(message.Id);

        // The client surfaces the request id so the caller can resolve the real download id.
        var status = await client.GetStatusAsync(message.RequestId);
        Assert.Equal(seeded.Id, status.Value.DownloadId);
    }

    [Fact]
    public async Task PaginatedList_ClampsAStartOlderThanSixtyDaysInsteadOfLettingAnafRefuse()
    {
        await fixture.SeedIncomingMessageAsync(SampleInvoice);
        var client = CreateClient();

        // Ninety days back would be rejected outright by ANAF; the client clamps it.
        var result = await client.ListMessagesAsync(
            DateTimeOffset.UtcNow.AddDays(-90), DateTimeOffset.UtcNow);

        Assert.True(result.IsSuccess, result.ToString());
        Assert.Single(result.Value.Messages);
    }

    [Fact]
    public async Task ListMessages_RejectsADayCountAnafWouldRefuse()
    {
        var client = CreateClient();

        // A programming error, so this throws rather than returning a result.
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.ListMessagesAsync(days: 61));
    }

    [Fact]
    public async Task Filter_IsPassedThroughToAnaf()
    {
        await fixture.SeedIncomingMessageAsync(SampleInvoice, tip: "FACTURA PRIMITA");
        var client = CreateClient();

        var received = await client.ListMessagesAsync(days: 30, filter: MessageFilter.Received);
        var sent = await client.ListMessagesAsync(days: 30, filter: MessageFilter.Sent);

        Assert.Single(received.Value);
        Assert.Empty(sent.Value);
    }

    // -------------------------------------------------------------- CIF handling

    [Fact]
    public async Task RoPrefix_IsStrippedBeforeCallingAnaf()
    {
        await fixture.SeedIncomingMessageAsync(SampleInvoice);
        var client = CreateClient();

        // ANAF's API rejects the prefixed form, so the client must normalise it.
        var result = await client.ListMessagesAsync(days: 30, cif: "RO" + MockAnafFixture.Cif);

        Assert.True(result.IsSuccess, result.ToString());
        Assert.Single(result.Value);
    }

    [Fact]
    public async Task WithoutACifAnywhere_TheMistakeIsReportedAsAProgrammingError()
    {
        var client = CreateClient(cif: string.Empty);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.UploadAsync(SampleInvoice));
    }

    // ------------------------------------------------- validation and rendering

    [Fact]
    public async Task Validate_ReadsTheStareShapeRatherThanTheOneV2Assumed()
    {
        var client = CreateClient();

        var valid = await client.ValidateAsync(SampleInvoice);
        var invalid = await client.ValidateAsync("<nonsense/>");

        Assert.True(valid.IsSuccess);
        Assert.True(valid.Value.IsValid);

        Assert.True(invalid.IsSuccess);
        Assert.False(invalid.Value.IsValid);
        Assert.NotEmpty(invalid.Value.Messages);
    }

    [Fact]
    public async Task RenderPdf_AcceptsRawBytes()
    {
        var client = CreateClient();

        var result = await client.RenderPdfAsync(SampleInvoice, skipValidation: true);

        Assert.True(result.IsSuccess, result.ToString());
        Assert.Equal("%PDF-"u8.ToArray(), result.Value[..5]);
    }

    [Fact]
    public async Task RenderPdf_AlsoAcceptsBase64()
    {
        var client = CreateClient(scenario: nameof(MockScenario.Base64Pdf));

        var result = await client.RenderPdfAsync(SampleInvoice, skipValidation: true);

        // ANAF sometimes answers with base64 text instead of bytes; both must work.
        Assert.True(result.IsSuccess, result.ToString());
        Assert.Equal("%PDF-"u8.ToArray(), result.Value[..5]);
    }

    // ---------------------------------------------------------------- helpers

    private AnafApiClient CreateClient(
        string? token = "mock-access-token-initial",
        string? scenario = null,
        string? cif = MockAnafFixture.Cif,
        int maxRetries = 0)
    {
        var options = Options.Create(new EFacturaOptions
        {
            Cif = cif ?? string.Empty,
            // Pointing the client at the mock is the whole reason the base address is overridable.
            ApiBaseAddress = new Uri(fixture.Server.BaseAddress, "test/FCTEL/rest"),
            MaxRetries = maxRetries,
            RetryDelay = TimeSpan.FromMilliseconds(1),
            MinimumDelayBetweenCalls = TimeSpan.Zero,
        });

        return new AnafApiClient(
            new MockHttpClientFactory(fixture, scenario),
            new StubTokenProvider(token),
            options,
            NullLogger<AnafApiClient>.Instance);
    }

    private sealed class StubTokenProvider(string? token) : IAnafAccessTokenProvider
    {
        public Task<string?> GetAccessTokenAsync(string cif, CancellationToken cancellationToken = default) =>
            Task.FromResult(token);
    }

    /// <summary>Hands the client the in-process mock's <see cref="HttpClient"/>.</summary>
    private sealed class MockHttpClientFactory(MockAnafFixture fixture, string? scenario) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = fixture.CreateClient();
            if (scenario is not null) client.DefaultRequestHeaders.Add("X-Mock-Scenario", scenario);
            return client;
        }
    }
}
