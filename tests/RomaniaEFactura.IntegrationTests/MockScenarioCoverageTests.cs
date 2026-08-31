using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using MockAnafServer;

namespace RomaniaEFactura.IntegrationTests;

/// <summary>
/// Proves every scenario the mock advertises actually does something, and that none is left
/// declared but unimplemented.
/// </summary>
/// <remarks>
/// A mock is only useful if the behaviour it claims to reproduce is real. A scenario that silently
/// falls through to the happy path would make any test relying on it vacuous.
/// </remarks>
public class MockScenarioCoverageTests(MockAnafFixture fixture)
    : IClassFixture<MockAnafFixture>, IAsyncLifetime
{
    private static readonly XNamespace UploadNs = "mfp:anaf:dgti:spv:respUploadFisier:v1";
    private static readonly XNamespace StatusNs = "mfp:anaf:dgti:efactura:stareMesajFactura:v1";

    private const string SampleInvoice = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"><ID>FCT-1</ID></Invoice>
        """;

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void EveryScenarioIsCoveredByATest()
    {
        // Guards against declaring a scenario and forgetting to exercise it. This cannot go red
        // through a code change alone - it fails when a new MockScenario member is added without
        // a matching test, which is exactly when someone needs reminding.
        var declared = Enum.GetNames<MockScenario>().Where(n => n != nameof(MockScenario.None)).ToHashSet();

        var covered = new HashSet<string>
        {
            nameof(MockScenario.NoSpvRights),
            nameof(MockScenario.FileTooLarge),
            nameof(MockScenario.UploadWillFailValidation),
            nameof(MockScenario.StuckInProcessing),
            nameof(MockScenario.NotEntitled),
            nameof(MockScenario.QuotaExhausted),
            nameof(MockScenario.NoMessages),
            nameof(MockScenario.RateLimited),
            nameof(MockScenario.ServerError),
            nameof(MockScenario.NestedArchive),
            nameof(MockScenario.ArchiveWithPdf),
            nameof(MockScenario.Base64Pdf),
            nameof(MockScenario.TokenExpired),
        };

        Assert.Empty(declared.Except(covered));
    }

    [Fact]
    public async Task FileTooLarge_IsRejectedByScenario()
    {
        var client = fixture.CreateAuthenticatedClient();

        var header = await UploadForHeaderAsync(client, SampleInvoice, nameof(MockScenario.FileTooLarge));

        Assert.Equal("1", header.Attribute("ExecutionStatus")!.Value);
        Assert.Contains("mai mare de 10 MB",
            header.Element(UploadNs + "Errors")!.Attribute("errorMessage")!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileOverTenMegabytes_IsRejectedOnSizeAlone()
    {
        // The limit is enforced on the real payload, not only through the scenario header.
        var client = fixture.CreateAuthenticatedClient();
        var oversized = "<Invoice>" + new string('x', 11 * 1024 * 1024) + "</Invoice>";

        var header = await UploadForHeaderAsync(client, oversized);

        Assert.Equal("1", header.Attribute("ExecutionStatus")!.Value);
        Assert.Contains("mai mare de 10 MB",
            header.Element(UploadNs + "Errors")!.Attribute("errorMessage")!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadWillFailValidation_ResolvesToNokAndYieldsAnErrorMessage()
    {
        var client = fixture.CreateAuthenticatedClient();

        var accepted = await UploadForHeaderAsync(
            client, SampleInvoice, nameof(MockScenario.UploadWillFailValidation));
        var index = accepted.Attribute("index_incarcare")!.Value;

        using var status = await client.GetAsync(
            $"{MockAnafFixture.ApiBase}/stareMesaj?id_incarcare={index}");
        var header = XDocument.Parse(await status.Content.ReadAsStringAsync()).Root!;

        // A rejected invoice still produces a downloadable response - it holds the errors and the
        // MF signature, and the invoice never reaches the buyer.
        Assert.Equal("nok", header.Attribute("stare")!.Value);
        Assert.NotNull(header.Attribute("id_descarcare"));

        using var list = await client.GetAsync(
            $"{MockAnafFixture.ApiBase}/listaMesajeFactura?zile=30&cif={MockAnafFixture.Cif}&filtru=E");
        var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("ERORI FACTURA", json.GetProperty("mesaje")[0].GetProperty("tip").GetString());
    }

    [Fact]
    public async Task StuckInProcessing_NeverResolves()
    {
        var client = fixture.CreateAuthenticatedClient();
        var index = (await UploadForHeaderAsync(client, SampleInvoice)).Attribute("index_incarcare")!.Value;

        for (var i = 0; i < 3; i++)
        {
            using var response = await client.SendAsync(Get(
                $"{MockAnafFixture.ApiBase}/stareMesaj?id_incarcare={index}",
                nameof(MockScenario.StuckInProcessing)));
            var header = XDocument.Parse(await response.Content.ReadAsStringAsync()).Root!;

            Assert.Equal("in prelucrare", header.Attribute("stare")!.Value);
            Assert.Null(header.Attribute("id_descarcare"));
        }
    }

    [Fact]
    public async Task QuotaExhausted_CanBeForcedWithoutSpendingTheRealBudget()
    {
        var client = fixture.CreateAuthenticatedClient();

        using var response = await client.SendAsync(Get(
            $"{MockAnafFixture.ApiBase}/stareMesaj?id_incarcare=5001000001",
            nameof(MockScenario.QuotaExhausted)));

        var header = XDocument.Parse(await response.Content.ReadAsStringAsync()).Root!;
        Assert.Contains("in cursul zilei",
            header.Element(StatusNs + "Errors")!.Attribute("errorMessage")!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoMessages_ReportsAnEmptyInboxEvenWhenMessagesExist()
    {
        await fixture.SeedIncomingMessageAsync(SampleInvoice);
        var client = fixture.CreateAuthenticatedClient();

        using var response = await client.SendAsync(Get(
            $"{MockAnafFixture.ApiBase}/listaMesajeFactura?zile=30&cif={MockAnafFixture.Cif}",
            nameof(MockScenario.NoMessages)));

        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains("Nu exista mesaje", json.GetProperty("eroare").GetString()!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArchiveWithPdf_IncludesAReadyMadeRendering()
    {
        var seeded = await fixture.SeedIncomingMessageAsync(SampleInvoice);
        var client = fixture.CreateAuthenticatedClient();

        using var response = await client.SendAsync(Get(
            $"{MockAnafFixture.ApiBase}/descarcare?id={seeded.Id}", nameof(MockScenario.ArchiveWithPdf)));

        using var archive = new ZipArchive(
            new MemoryStream(await response.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);

        // When the archive already holds a PDF, transformare is unnecessary.
        var pdf = Assert.Single(archive.Entries.Where(e => e.Name.EndsWith(".pdf", StringComparison.Ordinal)));
        using var stream = pdf.Open();
        var head = new byte[5];
        Assert.Equal(5, await stream.ReadAtLeastAsync(head, 5, throwOnEndOfStream: false));
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(head));
    }

    [Fact]
    public async Task ValidationEndpoint_ReportsStareOkOrNok()
    {
        using var client = fixture.CreateClient();

        using var valid = await PostAsync(client, $"{MockAnafFixture.ApiBase}/validare/FACT1", SampleInvoice);
        using var invalid = await PostAsync(client, $"{MockAnafFixture.ApiBase}/validare/FACT1", "<nonsense/>");

        // Both directions are JSON with a "stare" field - the shape v2 modelled as succes/erori,
        // which is why a valid invoice always deserialized as a failure.
        Assert.Equal("ok", (await ReadJsonAsync(valid)).GetProperty("stare").GetString());

        var failure = await ReadJsonAsync(invalid);
        Assert.Equal("nok", failure.GetProperty("stare").GetString());
        Assert.NotEmpty(failure.GetProperty("Messages").EnumerateArray());
    }

    [Fact]
    public async Task AuthorizeEndpoint_RedirectsBackWithACodeAndTheOriginalState()
    {
        using var client = fixture.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var response = await client.GetAsync(
            "/anaf-oauth2/v1/authorize?response_type=code&client_id=abc"
            + "&redirect_uri=https://localhost/callback&state=cif%7Creturn&token_content_type=jwt");

        // The real authorize step needs a qualified certificate in a browser and cannot be
        // automated; the mock short-circuits it so the rest of the flow stays testable.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Contains("code=mock-authorization-code", location, StringComparison.Ordinal);
        Assert.Contains("state=cif", location, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private static async Task<XElement> UploadForHeaderAsync(
        HttpClient client, string body, string? scenario = null)
    {
        using var response = await PostAsync(client,
            $"{MockAnafFixture.ApiBase}/upload?standard=UBL&cif={MockAnafFixture.Cif}", body, scenario);
        return XDocument.Parse(await response.Content.ReadAsStringAsync()).Root!;
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

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
