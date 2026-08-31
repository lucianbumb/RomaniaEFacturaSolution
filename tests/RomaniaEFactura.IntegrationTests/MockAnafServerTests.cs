using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace RomaniaEFactura.IntegrationTests;

/// <summary>
/// Verifies the mock reproduces the ANAF behaviours recorded in <c>docs/anaf-wire-formats.md</c>.
/// </summary>
/// <remarks>
/// The mock is only worth having if it is faithful, so these tests assert the shapes a client will
/// actually have to parse — particularly the failures, which ANAF returns inside HTTP 200.
/// </remarks>
public class MockAnafServerTests(MockAnafFixture fixture) : IClassFixture<MockAnafFixture>, IAsyncLifetime
{
    private static readonly XNamespace UploadNs = "mfp:anaf:dgti:spv:respUploadFisier:v1";
    private static readonly XNamespace StatusNs = "mfp:anaf:dgti:efactura:stareMesajFactura:v1";

    private const string SampleInvoice = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"><ID>FCT-1</ID></Invoice>
        """;

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ------------------------------------------------------------- lifecycle

    [Fact]
    public async Task FullLifecycle_UploadToSignedArchive()
    {
        var client = fixture.CreateAuthenticatedClient();

        // upload -> index_incarcare
        var index = await UploadAsync(client);
        Assert.Matches("^[0-9]+$", index);

        // stareMesaj -> id_descarcare
        using var status = await client.GetAsync(
            $"{MockAnafFixture.ApiBase}/stareMesaj?id_incarcare={index}");
        var header = await ParseXmlAsync(status);

        Assert.Equal("ok", header.Attribute("stare")!.Value);
        var downloadId = header.Attribute("id_descarcare")!.Value;

        // descarcare -> ZIP holding the document and the MF signature
        using var download = await client.GetAsync($"{MockAnafFixture.ApiBase}/descarcare?id={downloadId}");
        download.EnsureSuccessStatusCode();

        var entries = await ReadArchiveEntriesAsync(download);
        Assert.Contains(entries.Keys, n => n.EndsWith(".xml", StringComparison.Ordinal)
                                        && !n.StartsWith("semnatura_", StringComparison.Ordinal));
        Assert.Contains(entries.Keys, n => n.StartsWith("semnatura_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StareMesaj_ReportsInProcessingUntilTheDocumentResolves()
    {
        await fixture.SetPollsBeforeResolutionAsync(2);
        var client = fixture.CreateAuthenticatedClient();
        var index = await UploadAsync(client);

        var states = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            using var response = await client.GetAsync(
                $"{MockAnafFixture.ApiBase}/stareMesaj?id_incarcare={index}");
            states.Add((await ParseXmlAsync(response)).Attribute("stare")!.Value);
        }

        Assert.Equal(["in prelucrare", "in prelucrare", "ok"], states);
    }

    // ------------------------------------ errors arrive with HTTP 200 (Rule 1)

    [Fact]
    public async Task Upload_WithoutSpvRights_Returns200WithExecutionStatusOne()
    {
        var client = fixture.CreateAuthenticatedClient();

        using var response = await PostAsync(client,
            $"{MockAnafFixture.ApiBase}/upload?standard=UBL&cif={MockAnafFixture.Cif}",
            SampleInvoice, MockScenarioHeader.NoSpvRights);

        // The rejection is inside a success status: a client branching on IsSuccessStatusCode
        // reads this as an accepted upload.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var header = await ParseXmlAsync(response);
        Assert.Equal("1", header.Attribute("ExecutionStatus")!.Value);
        Assert.Null(header.Attribute("index_incarcare"));
        Assert.Contains("Nu aveti drept in SPV",
            header.Element(UploadNs + "Errors")!.Attribute("errorMessage")!.Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Descarcare_WhenNotEntitled_Returns200WithJsonWhereAZipWasExpected()
    {
        var client = fixture.CreateAuthenticatedClient();

        using var response = await client.SendAsync(Get(
            $"{MockAnafFixture.ApiBase}/descarcare?id=3001000001", MockScenarioHeader.NotEntitled));

        // The trap this mock exists to reproduce: JSON on a 200, where a ZIP was expected.
        // A client that hands this straight to ZipArchive gets an opaque InvalidDataException.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("{", body.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("Nu aveti dreptul sa descarcati", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StareMesaj_WhenNotEntitled_Returns200WithErrorsElement()
    {
        var client = fixture.CreateAuthenticatedClient();

        using var response = await client.SendAsync(Get(
            $"{MockAnafFixture.ApiBase}/stareMesaj?id_incarcare=5001000001", MockScenarioHeader.NotEntitled));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var header = await ParseXmlAsync(response);
        Assert.NotNull(header.Element(StatusNs + "Errors"));
        Assert.Null(header.Attribute("stare"));
    }

    [Fact]
    public async Task MessageList_WhenEmpty_ReportsItThroughTheSameErrorFieldAsAFailure()
    {
        var client = fixture.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            $"{MockAnafFixture.ApiBase}/listaMesajeFactura?zile=30&cif={MockAnafFixture.Cif}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);

        // "Nu exista mesaje" means empty, not broken. A client treating any "eroare" as a fault
        // reports a failure every time there is simply no post.
        Assert.Contains("Nu exista mesaje", json.GetProperty("eroare").GetString()!, StringComparison.Ordinal);
    }

    // ------------------------------------------------------- documented quirks

    [Fact]
    public async Task MessageList_CanOmitTheId_ForcingAStareMesajRoundTrip()
    {
        var seeded = await fixture.SeedIncomingMessageAsync(SampleInvoice, hideId: true);
        var client = fixture.CreateAuthenticatedClient();

        using var response = await client.GetAsync(
            $"{MockAnafFixture.ApiBase}/listaMesajeFactura?zile=30&cif={MockAnafFixture.Cif}");
        var message = (await ReadJsonAsync(response)).GetProperty("mesaje")[0];

        Assert.False(message.TryGetProperty("id", out _));
        var solicitare = message.GetProperty("id_solicitare").GetString()!;

        // The download identifier has to be resolved through stareMesaj before anything can be
        // fetched — behaviour ANAF documents nowhere.
        using var status = await client.GetAsync(
            $"{MockAnafFixture.ApiBase}/stareMesaj?id_incarcare={solicitare}");
        var resolved = (await ParseXmlAsync(status)).Attribute("id_descarcare")!.Value;

        Assert.Equal(seeded.Id, resolved);
    }

    [Fact]
    public async Task MessageList_RejectsAStartTimeOlderThanSixtyDays()
    {
        var client = fixture.CreateAuthenticatedClient();
        var start = DateTimeOffset.UtcNow.AddDays(-90).ToUnixTimeMilliseconds();
        var end = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var response = await client.GetAsync(
            $"{MockAnafFixture.ApiBase}/listaMesajePaginatieFactura"
            + $"?startTime={start}&endTime={end}&cif={MockAnafFixture.Cif}&pagina=1");

        var json = await ReadJsonAsync(response);
        Assert.Contains("nu poate fi mai vechi de 60 de zile",
            json.GetProperty("eroare").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Descarcare_CanReturnANestedArchive()
    {
        var seeded = await fixture.SeedIncomingMessageAsync(SampleInvoice);
        var client = fixture.CreateAuthenticatedClient();

        using var response = await client.SendAsync(Get(
            $"{MockAnafFixture.ApiBase}/descarcare?id={seeded.Id}", MockScenarioHeader.NestedArchive));

        var entries = await ReadArchiveEntriesAsync(response);
        var inner = Assert.Single(entries);
        Assert.EndsWith(".zip", inner.Key, StringComparison.Ordinal);

        // A client has to recurse to reach the document.
        using var nested = new ZipArchive(new MemoryStream(inner.Value), ZipArchiveMode.Read);
        Assert.Contains(nested.Entries, e => e.Name.EndsWith(".xml", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Transformare_CanReturnBase64RatherThanRawPdfBytes()
    {
        var client = fixture.CreateAuthenticatedClient();

        using var response = await PostAsync(client,
            $"{MockAnafFixture.ApiBase}/transformare/FACT1/DA", SampleInvoice, MockScenarioHeader.Base64Pdf);

        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("JVBER", body, StringComparison.Ordinal);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(Convert.FromBase64String(body)), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- quotas (Rule 2)

    [Fact]
    public async Task StareMesaj_StopsAnsweringOnceTheDailyQuotaIsSpent()
    {
        var client = fixture.CreateAuthenticatedClient();
        await fixture.SetPollsBeforeResolutionAsync(1000);   // never resolves, so every call polls
        var index = await UploadAsync(client);

        string? lastError = null;
        for (var i = 0; i < 21; i++)
        {
            using var response = await client.GetAsync(
                $"{MockAnafFixture.ApiBase}/stareMesaj?id_incarcare={index}");
            var header = await ParseXmlAsync(response);
            lastError = header.Element(StatusNs + "Errors")?.Attribute("errorMessage")?.Value;
        }

        // A fixed poll interval burns the day's budget and then goes blind — which is why the
        // reconciler needs a persisted per-identifier budget rather than a timer.
        Assert.NotNull(lastError);
        Assert.Contains("descarcari de mesaj in cursul zilei", lastError!, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- transport

    [Theory]
    [InlineData(MockScenarioHeader.RateLimited, HttpStatusCode.TooManyRequests)]
    [InlineData(MockScenarioHeader.ServerError, HttpStatusCode.InternalServerError)]
    [InlineData(MockScenarioHeader.TokenExpired, HttpStatusCode.Unauthorized)]
    public async Task TransportFailures_UseRealStatusCodes(string scenario, HttpStatusCode expected)
    {
        var client = fixture.CreateAuthenticatedClient();

        using var response = await client.SendAsync(Get(
            $"{MockAnafFixture.ApiBase}/stareMesaj?id_incarcare=5001000001", scenario));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task RequestsWithoutABearerToken_Are401()
    {
        using var client = fixture.CreateClient();

        using var response = await client.GetAsync(
            $"{MockAnafFixture.ApiBase}/listaMesajeFactura?zile=30&cif={MockAnafFixture.Cif}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_WithoutRequiredParameters_IsAGenuine400WithADifferentShape()
    {
        var client = fixture.CreateAuthenticatedClient();

        using var response = await PostAsync(client, $"{MockAnafFixture.ApiBase}/upload", SampleInvoice);

        // The one place a real error status appears — and in a third body shape, so a client
        // cannot assume one content type per endpoint.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.Equal(400, json.GetProperty("status").GetInt32());
        Assert.Contains("obligatorii", json.GetProperty("message").GetString()!, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ oauth

    [Fact]
    public async Task TokenEndpoint_RequiresBasicAuthentication()
    {
        using var client = fixture.CreateClient();

        using var response = await client.PostAsync("/anaf-oauth2/v1/token",
            new FormUrlEncodedContent([new("grant_type", "authorization_code"), new("code", "x")]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TokenEndpoint_ReturnsLongLivedTokens()
    {
        using var client = fixture.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/anaf-oauth2/v1/token")
        {
            Content = new FormUrlEncodedContent(
            [
                new("grant_type", "authorization_code"),
                new("code", "mock-authorization-code"),
                new("token_content_type", "jwt"),
            ]),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("client:secret")));

        using var response = await client.SendAsync(request);
        var json = await ReadJsonAsync(response);

        Assert.False(string.IsNullOrEmpty(json.GetProperty("access_token").GetString()));
        Assert.False(string.IsNullOrEmpty(json.GetProperty("refresh_token").GetString()));

        // About 90 days, matching the real lifetime — which is why a refresh token that is
        // discarded when the access token expires is such an expensive bug.
        Assert.Equal(7_776_000, json.GetProperty("expires_in").GetInt32());
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<string> UploadAsync(HttpClient client)
    {
        using var response = await PostAsync(client,
            $"{MockAnafFixture.ApiBase}/upload?standard=UBL&cif={MockAnafFixture.Cif}", SampleInvoice);
        response.EnsureSuccessStatusCode();

        return (await ParseXmlAsync(response)).Attribute("index_incarcare")!.Value;
    }

    private static Task<HttpResponseMessage> PostAsync(
        HttpClient client, string url, string body, string? scenario = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/plain"),
        };
        if (scenario is not null) request.Headers.Add("X-Mock-Scenario", scenario);
        return client.SendAsync(request);
    }

    private static HttpRequestMessage Get(string url, string? scenario = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (scenario is not null) request.Headers.Add("X-Mock-Scenario", scenario);
        return request;
    }

    private static async Task<XElement> ParseXmlAsync(HttpResponseMessage response) =>
        XDocument.Parse(await response.Content.ReadAsStringAsync()).Root!;

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<Dictionary<string, byte[]>> ReadArchiveEntriesAsync(HttpResponseMessage response)
    {
        using var archive = new ZipArchive(
            new MemoryStream(await response.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);

        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            entries[entry.Name] = buffer.ToArray();
        }

        return entries;
    }
}

/// <summary>Scenario names, kept as constants so a theory can use them.</summary>
internal static class MockScenarioHeader
{
    public const string NoSpvRights = "NoSpvRights";
    public const string NotEntitled = "NotEntitled";
    public const string NestedArchive = "NestedArchive";
    public const string Base64Pdf = "Base64Pdf";
    public const string RateLimited = "RateLimited";
    public const string ServerError = "ServerError";
    public const string TokenExpired = "TokenExpired";
}
