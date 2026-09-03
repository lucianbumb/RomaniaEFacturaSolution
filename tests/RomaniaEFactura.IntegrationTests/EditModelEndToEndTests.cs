using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RomaniaEFactura.Authentication;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.EditModels;
using RomaniaEFactura.Persistence;
using RomaniaEFactura.Transport;
using RomaniaEFactura.Ubl;

namespace RomaniaEFactura.IntegrationTests;

/// <summary>
/// The path an application actually takes: fill in a model, verify it, send it.
/// </summary>
/// <remarks>
/// The unit suite proves the model maps to a document ANAF's own validator accepts. This proves
/// the rest of the journey — that what the mapper produces survives serialization, upload and
/// round-tripping back out of the archive as the same invoice.
/// </remarks>
public class EditModelEndToEndTests(MockAnafFixture fixture)
    : IClassFixture<MockAnafFixture>, IAsyncLifetime, IDisposable
{
    /// <summary>A company of this class's own, so it cannot interfere with the other suites.</summary>
    private const string Cif = "9999989";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TestClock _clock = new(DateTimeOffset.UtcNow);
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        _connection.Open();
        await fixture.ResetAsync();

        _services = BuildServices();
        await _services.EnsureEFacturaSchemaAsync();

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
    public async Task AnInvoiceFilledInAsAModelIsAcceptedByAnaf()
    {
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();

        var invoice = SampleInvoice();

        // Nothing here states a total, a VAT amount or a line net amount.
        Assert.True(service.Verify(invoice).IsValid);

        var receipt = await service.SendInvoiceAsync(invoice);

        Assert.True(receipt.IsSuccess, receipt.ToString());
        Assert.Equal(Cif, receipt.Value.Cif);

        var recorded = await service.GetSubmissionAsync(receipt.Value.UploadIndex);
        Assert.Equal(invoice.Number, recorded!.DocumentId);
    }

    [Fact]
    public async Task TheDocumentAnafStoresCarriesTheDerivedTotals()
    {
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();
        var db = scope.ServiceProvider.GetRequiredService<EFacturaDbContext>();

        var invoice = SampleInvoice();
        var receipt = await service.SendInvoiceAsync(invoice);
        Assert.True(receipt.IsSuccess, receipt.ToString());

        _clock.Advance(TimeSpan.FromMinutes(2));
        await scope.ServiceProvider
            .GetRequiredService<Reconciliation.EFacturaReconciler>()
            .RunOnceAsync();

        var downloadId = await db.Submissions
            .Where(s => s.UploadIndex == receipt.Value.UploadIndex)
            .Select(s => s.DownloadId)
            .SingleAsync();

        var document = await service.GetDocumentAsync(downloadId!);
        Assert.True(document.IsSuccess, document.ToString());

        // Read back out of the archive ANAF returned, so this is the document as ANAF holds it —
        // not the one held in memory on this side.
        var stored = UblSerializer.DeserializeInvoice(document.Value.Xml!);

        Assert.Equal(invoice.Number, stored.Id.Value);
        Assert.Equal(invoice.LineTotal, stored.LegalMonetaryTotal.LineExtensionAmount.Value);
        Assert.Equal(invoice.TaxInclusiveTotal, stored.LegalMonetaryTotal.TaxInclusiveAmount.Value);
        Assert.Equal(invoice.PayableAmount, stored.LegalMonetaryTotal.PayableAmount.Value);
        Assert.Equal(invoice.VatTotal, stored.TaxTotals[0].TaxAmount.Value);
    }

    [Fact]
    public async Task AModelThatFailsItsOwnRulesIsNeverSent()
    {
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();

        var invoice = SampleInvoice();
        invoice.Buyer.TaxId = "12345675";   // one digit off a valid control digit

        var result = await service.SendInvoiceAsync(invoice);

        Assert.False(result.IsSuccess);
        Assert.Equal(AnafErrorKind.InvalidRequest, result.Error!.Kind);
        // Nothing was recorded, because nothing left the building.
        Assert.Empty(await service.GetSubmissionsAsync());
    }

    [Fact]
    public async Task ACreditNoteFilledInAsAModelIsSentAsACreditNote()
    {
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();

        var creditNote = new CreditNoteEditModel
        {
            Number = "CN-2026-500",
            IssueDate = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime),
            Seller = Seller(),
            Buyer = Buyer(),
            PrecedingDocuments = [new DocumentReferenceEditModel { Number = "FCT-2026-500" }],
            Lines =
            [
                new DocumentLineEditModel
                {
                    Name = "Storno servicii",
                    Quantity = 1m,
                    UnitPrice = 100.00m,
                    VatRate = 19m,
                },
            ],
        };

        var receipt = await service.SendCreditNoteAsync(creditNote);

        Assert.True(receipt.IsSuccess, receipt.ToString());

        // The standard parameter has to say CN; sent as UBL, ANAF would try to parse a credit
        // note as an invoice.
        Assert.Equal("CN", (await fixture.LastUploadAsync()).Standard);
    }

    [Fact]
    public async Task ABuyerMessageIsSentAsRasp()
    {
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();

        var receipt = await service.SendBuyerMessageAsync(new BuyerMessageEditModel
        {
            UploadIndex = "3828",
            Message = "Cantitatea livrată nu corespunde comenzii.",
        });

        Assert.True(receipt.IsSuccess, receipt.ToString());
        Assert.Equal("RASP", (await fixture.LastUploadAsync()).Standard);
    }

    [Fact]
    public async Task AnInvalidBuyerMessageIsNeverSent()
    {
        await using var scope = _services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();

        var result = await service.SendBuyerMessageAsync(new BuyerMessageEditModel
        {
            UploadIndex = "not-a-number",
            Message = "Ceva",
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(AnafErrorKind.InvalidRequest, result.Error!.Kind);
    }

    private InvoiceEditModel SampleInvoice() => new()
    {
        Number = "FCT-2026-500",
        IssueDate = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime),
        DueDate = DateOnly.FromDateTime(_clock.GetUtcNow().AddDays(30).UtcDateTime),
        Seller = Seller(),
        Buyer = Buyer(),
        Payment = new PaymentEditModel
        {
            MeansCode = "31",
            Iban = "RO49AAAA1B31007593840000",
            AccountHolder = "Furnizor Test SRL",
        },
        Lines =
        [
            new DocumentLineEditModel
            {
                Name = "Servicii de consultanta",
                Quantity = 3m,
                UnitPrice = 133.3333m,
                VatRate = 19m,
            },
            new DocumentLineEditModel
            {
                Name = "Licenta software",
                Quantity = 2m,
                UnitPrice = 250.00m,
                VatRate = 19m,
                DiscountAmount = 50.00m,
                DiscountReason = "Reducere volum",
            },
        ],
    };

    private static PartyEditModel Seller() => new()
    {
        Name = "Furnizor Test SRL",
        TaxId = "12345674",
        VatNumber = "RO12345674",
        Address = new AddressEditModel
        {
            Street = "Strada Exemplu 1",
            City = "SECTOR1",
            County = "RO-B",
            CountryCode = "RO",
        },
    };

    private static PartyEditModel Buyer() => new()
    {
        Name = "Client Test SA",
        TaxId = "23456783",
        VatNumber = "RO23456783",
        Address = new AddressEditModel
        {
            Street = "Bulevardul Clientului 20",
            City = "Cluj-Napoca",
            County = "RO-CJ",
            CountryCode = "RO",
        },
    };

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDataProtection();

        services.Configure<EFacturaOptions>(options =>
        {
            options.Cif = Cif;
            options.ClientId = "test-client";
            options.ClientSecret = "test-secret";
            options.RedirectUri = "https://localhost/efactura/callback";
            options.ApiBaseAddress = new Uri(fixture.Server.BaseAddress, "test/FCTEL/rest");
            options.OAuthBaseAddress = new Uri(fixture.Server.BaseAddress, "anaf-oauth2/v1");
            options.MaxRetries = 0;
            options.RetryDelay = TimeSpan.FromMilliseconds(1);
            options.MinimumDelayBetweenCalls = TimeSpan.Zero;
            options.EnableReconciler = false;
        });

        // The service and the reconciler must share one clock, or a submission scheduled against
        // real time would never look due to a reconciler running on test time.
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<IHttpClientFactory>(new MockHttpClientFactory(fixture));
        services.AddDbContext<EFacturaDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<IEFacturaTokenStore, EfCoreTokenStore>();
        services.AddSingleton<OAuthStateProtector>();
        services.AddScoped<IAnafOAuthClient, AnafOAuthClient>();
        services.AddScoped<IAnafAccessTokenProvider, StoredTokenAccessTokenProvider>();
        services.AddScoped<IAnafApiClient, AnafApiClient>();
        services.AddScoped<RomaniaEFactura.Lookup.IAnafCompanyLookupClient, RomaniaEFactura.Lookup.AnafCompanyLookupClient>();
        services.AddScoped<IRomaniaEFacturaService, RomaniaEFacturaService>();
        services.AddScoped<Reconciliation.EFacturaReconciler>();

        return services.BuildServiceProvider();
    }
}
