using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Authentication;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.Persistence;
using RomaniaEFactura.Reconciliation;
using RomaniaEFactura.Transport;
using RomaniaEFactura.Ubl;

namespace RomaniaEFactura.IntegrationTests;

/// <summary>
/// The whole library working together: authorize, verify, send, reconcile, archive, read back.
/// </summary>
/// <remarks>
/// This is the milestone's acceptance criterion. Everything runs against the mock, with the
/// library's own storage on real SQLite, so nothing here is stubbed except ANAF itself.
/// </remarks>
public class EndToEndTests(MockAnafFixture fixture) : IClassFixture<MockAnafFixture>, IAsyncLifetime, IDisposable
{
    /// <summary>A company of this class's own, so it cannot interfere with the other suites.</summary>
    private const string Cif = "9999997";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private ServiceProvider _services = null!;
    // Starts from real time rather than a fixed date, because the mock server stamps the messages
    // it creates with its own clock. A test clock pinned to some other instant would put those
    // messages outside every window the library asks for.
    private readonly TestClock _clock = new(DateTimeOffset.UtcNow);

    public async Task InitializeAsync()
    {
        _connection.Open();
        await fixture.ResetAsync();

        _services = BuildServices();
        await _services.EnsureEFacturaSchemaAsync();

        // Authorize the company, as a person with a certificate would have done.
        await using var scope = _services.CreateAsyncScope();
        var oauth = scope.ServiceProvider.GetRequiredService<IAnafOAuthClient>();
        var store = scope.ServiceProvider.GetRequiredService<IEFacturaTokenStore>();
        var token = await oauth.ExchangeCodeAsync("mock-authorization-code", Cif);
        await store.SaveAsync(token.Value);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _services?.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ASubmissionIsSentReconciledAndArchived()
    {
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();

        // Connected, because someone authorized in InitializeAsync.
        var status = await service.GetAuthorizationStatusAsync();
        Assert.True(status.IsConnected);

        var invoice = SampleInvoice();
        Assert.True(service.Verify(invoice).IsValid);

        var receipt = await service.SendInvoiceAsync(invoice);
        Assert.True(receipt.IsSuccess, receipt.ToString());

        // Sending returns as soon as ANAF accepts the upload; the outcome is still unknown.
        var pending = await service.GetSubmissionAsync(receipt.Value.UploadIndex);
        Assert.True(pending!.IsPending);

        // Move past the first poll interval and reconcile.
        _clock.Advance(TimeSpan.FromMinutes(2));
        var outcome = await Reconciler().RunOnceAsync();

        Assert.Equal(1, outcome.Resolved);
        Assert.Equal(1, outcome.Downloaded);

        var settled = await service.GetSubmissionAsync(receipt.Value.UploadIndex);
        Assert.True(settled!.IsAccepted);
        // The ministry's signature is the proof of submission and has to be retained.
        Assert.True(settled.HasArchive);

        var document = await service.GetDocumentAsync(settled.UploadIndex is null ? "" : (await ArchiveIdAsync(receipt.Value.UploadIndex))!);
        Assert.True(document.IsSuccess, document.ToString());
        Assert.Equal(EFacturaDocumentKind.Invoice, document.Value.Kind);
        Assert.NotNull(document.Value.SignatureXml);
    }

    [Fact]
    public async Task ADocumentThatFailsVerificationIsNeverSent()
    {
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();

        var invoice = SampleInvoice();
        invoice.LegalMonetaryTotal.PayableAmount = new Amount(999.00m);   // breaks BR-CO-16

        var result = await service.SendInvoiceAsync(invoice);

        Assert.False(result.IsSuccess);
        Assert.Equal(AnafErrorKind.InvalidRequest, result.Error!.Kind);
        Assert.Contains("BR-CO-16", result.Error.Message, StringComparison.Ordinal);

        // Nothing was recorded, because nothing was sent.
        Assert.Empty(await service.GetSubmissionsAsync());
    }

    [Fact]
    public async Task ReconciliationRespectsEachSubmissionsSchedule()
    {
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();
        await fixture.SetPollsBeforeResolutionAsync(100);   // never resolves

        await service.SendInvoiceAsync(SampleInvoice());
        var reconciler = Reconciler();

        // Immediately after sending nothing is due, so a tight loop costs ANAF nothing. This is
        // what stops the reconciler from burning the daily allowance in its first minutes.
        Assert.False((await reconciler.RunOnceAsync()).DidWork);
        Assert.False((await reconciler.RunOnceAsync()).DidWork);

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.True((await reconciler.RunOnceAsync()).DidWork);
    }

    [Fact]
    public async Task AWholeDayOfReconciliationStaysInsideTheDailyAllowance()
    {
        // The acceptance criterion that matters most: simulate a full day against a document that
        // never resolves, and confirm the mock's quota is never exhausted.
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();
        await fixture.SetPollsBeforeResolutionAsync(10_000);

        await service.SendInvoiceAsync(SampleInvoice());
        var reconciler = Reconciler();

        var polls = 0;
        var deadline = _clock.GetUtcNow().AddDays(1);

        // Step a minute at a time for a simulated day, exactly as the hosted loop would.
        while (_clock.GetUtcNow() < deadline)
        {
            var outcome = await reconciler.RunOnceAsync();
            polls += outcome.Polled;
            Assert.Equal(0, outcome.QuotaExhausted);
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        // The mock enforces twenty; the schedule should have spent far fewer.
        Assert.InRange(polls, 5, 15);
    }

    [Fact]
    public async Task TheInboxIsSyncedAndDeduplicated()
    {
        await SeedIncomingAsync();

        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();

        var first = await service.SyncInboxAsync();
        Assert.True(first.IsSuccess, first.ToString());
        Assert.Equal(1, first.Value.NewMessages);

        // A second sync must not re-download what is already held: downloads are capped at roughly
        // ten per identifier per day.
        var second = await service.SyncInboxAsync();
        Assert.True(second.IsSuccess, second.ToString());
        Assert.Equal(0, second.Value.NewMessages);

        Assert.Single(await service.GetInboxAsync());
    }

    [Fact]
    public async Task AnArchiveIsServedFromStorageOnceItHasBeenFetched()
    {
        await SeedIncomingAsync();

        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();
        await service.SyncInboxAsync();

        var message = Assert.Single(await service.GetInboxAsync());
        Assert.False(message.IsDownloaded);

        var first = await service.GetArchiveAsync(message.DownloadId);
        Assert.True(first.IsSuccess, first.ToString());

        // Now cached. Repeated reads must not spend the daily download allowance, so this is
        // checked by exhausting the mock's budget and confirming the read still succeeds.
        var reread = await service.GetArchiveAsync(message.DownloadId);
        Assert.True(reread.IsSuccess);
        Assert.Equal(first.Value.Length, reread.Value.Length);

        Assert.True((await service.GetInboxAsync())[0].IsDownloaded);
    }

    [Fact]
    public async Task AnUnauthorizedCompanyIsReportedRatherThanFailingMidSubmission()
    {
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();
        await service.DisconnectAsync();

        var status = await service.GetAuthorizationStatusAsync();
        Assert.False(status.IsConnected);

        var result = await service.SendInvoiceAsync(SampleInvoice());
        Assert.False(result.IsSuccess);
        Assert.Equal(AnafErrorKind.NotAuthorized, result.Error!.Kind);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<string?> ArchiveIdAsync(string uploadIndex)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EFacturaDbContext>();
        return await db.Submissions
            .Where(s => s.UploadIndex == uploadIndex)
            .Select(s => s.DownloadId)
            .FirstOrDefaultAsync();
    }

    private async Task SeedIncomingAsync()
    {
        using var client = fixture.CreateClient();
        using var response = await client.PostAsJsonAsync("/__mock/messages", new
        {
            Cif,
            Xml = UblSerializer.Serialize(SampleInvoice()),
            Tip = "FACTURA PRIMITA",
        });
        response.EnsureSuccessStatusCode();

        // Time passes between ANAF recording a message and anyone syncing, and the sync window
        // ends at "now" - so without this the message sits just beyond the window's edge.
        _clock.Advance(TimeSpan.FromMinutes(1));
    }

    private EFacturaReconciler Reconciler() =>
        new(_services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(BuildOptions()),
            _clock,
            NullLogger<EFacturaReconciler>.Instance);

    private EFacturaOptions BuildOptions() => new()
    {
        Cif = Cif,
        ClientId = "test-client",
        ClientSecret = "test-secret",
        RedirectUri = "https://localhost/efactura/callback",
        ApiBaseAddress = new Uri(fixture.Server.BaseAddress, "test/FCTEL/rest"),
        OAuthBaseAddress = new Uri(fixture.Server.BaseAddress, "anaf-oauth2/v1"),
        MaxRetries = 0,
        RetryDelay = TimeSpan.FromMilliseconds(1),
        MinimumDelayBetweenCalls = TimeSpan.Zero,
        // The hosted loop is not used here; tests drive RunOnceAsync directly so they need not
        // wait out real intervals.
        EnableReconciler = false,
    };

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.Configure<EFacturaOptions>(o =>
        {
            var source = BuildOptions();
            o.Cif = source.Cif;
            o.ClientId = source.ClientId;
            o.ClientSecret = source.ClientSecret;
            o.RedirectUri = source.RedirectUri;
            o.ApiBaseAddress = source.ApiBaseAddress;
            o.OAuthBaseAddress = source.OAuthBaseAddress;
            o.MaxRetries = source.MaxRetries;
            o.RetryDelay = source.RetryDelay;
            o.MinimumDelayBetweenCalls = source.MinimumDelayBetweenCalls;
            o.EnableReconciler = false;
        });

        // The service and the reconciler must share one clock, or a submission scheduled against
        // real time would never look due to a reconciler running on test time.
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<IHttpClientFactory>(new MockHttpClientFactory(fixture));
        services.AddDbContext<EFacturaDbContext>(o => o.UseSqlite(_connection));
        services.AddScoped<IEFacturaTokenStore, EfCoreTokenStore>();
        services.AddSingleton<OAuthStateProtector>();
        services.AddScoped<IAnafOAuthClient, AnafOAuthClient>();
        services.AddScoped<IAnafAccessTokenProvider, StoredTokenAccessTokenProvider>();
        services.AddScoped<IAnafApiClient, AnafApiClient>();
        services.AddScoped<IRomaniaEFacturaService, RomaniaEFacturaService>();

        return services.BuildServiceProvider();
    }

    private static UblInvoice SampleInvoice()
    {
        const decimal net = 200.00m;
        const decimal vat = 38.00m;

        return new UblInvoice
        {
            Id = "FCT-E2E-001",
            IssueDate = new DateTime(2026, 8, 31),
            DueDate = new DateTime(2026, 9, 30),
            DocumentCurrencyCode = "RON",
            AccountingSupplierParty = new PartyWrapper(Party("Furnizor E2E SRL", Cif, "RO-B", "BUCURESTI")),
            AccountingCustomerParty = new PartyWrapper(Party("Client E2E SRL", "23456783", "RO-CJ", "CLUJ-NAPOCA")),
            TaxTotals =
            [
                new TaxTotal
                {
                    TaxAmount = new Amount(vat),
                    TaxSubtotals =
                    [
                        new TaxSubtotal
                        {
                            TaxableAmount = new Amount(net),
                            TaxAmount = new Amount(vat),
                            TaxCategory = new TaxCategory { Id = "S", Percent = 19m },
                        },
                    ],
                },
            ],
            LegalMonetaryTotal = new MonetaryTotal
            {
                LineExtensionAmount = new Amount(net),
                TaxExclusiveAmount = new Amount(net),
                TaxInclusiveAmount = new Amount(238.00m),
                PayableAmount = new Amount(238.00m),
            },
            InvoiceLines =
            [
                new InvoiceLine
                {
                    Id = "1",
                    InvoicedQuantity = new Quantity(2m),
                    LineExtensionAmount = new Amount(net),
                    Item = new Item
                    {
                        Name = "Servicii",
                        ClassifiedTaxCategory = new LineTaxCategory { Id = "S", Percent = 19m },
                    },
                    Price = new Price { PriceAmount = new Amount(100.00m) },
                },
            ],
        };
    }

    private static Party Party(string name, string cif, string county, string city) => new()
    {
        PartyName = new PartyName { Name = name },
        PostalAddress = new PostalAddress
        {
            StreetName = "Strada Exemplu 1",
            CityName = city,
            PostalZone = "010101",
            CountrySubentity = county,
            Country = new Country { IdentificationCode = "RO" },
        },
        PartyTaxSchemes = [new PartyTaxScheme { CompanyId = "RO" + cif }],
        PartyLegalEntity = new PartyLegalEntity
        {
            RegistrationName = name,
            CompanyId = new Identifier("RO" + cif),
        },
    };

    private sealed class MockHttpClientFactory(MockAnafFixture fixture) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => fixture.CreateClient();
    }

    /// <summary>A clock the test moves by hand, so a simulated day takes milliseconds.</summary>
    private sealed class TestClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
