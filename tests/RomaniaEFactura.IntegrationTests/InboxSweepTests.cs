using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Authentication;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.Persistence;
using RomaniaEFactura.Reconciliation;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura.IntegrationTests;

/// <summary>
/// Reading every authorized company's inbox in the background.
/// </summary>
/// <remarks>
/// <para>
/// Outbound reconciliation was already company-agnostic; inbound was not. <c>SyncInboxAsync</c>
/// existed and nothing called it, so a company's messages appeared only when somebody asked, and
/// whoever asked first paid for the whole sync.
/// </para>
/// <para>
/// The scheduling is per company rather than per sweep, which is the part worth testing: with a
/// hundred companies a shared interval means a hundred calls on every tick.
/// </para>
/// </remarks>
public class InboxSweepTests(MockAnafFixture fixture) : IClassFixture<MockAnafFixture>, IAsyncLifetime
{
    private const string Ours = "12345674";
    private const string Theirs = "19867705";

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EveryAuthorizedCompanyIsRead()
    {
        using var host = await BuildAsync(authorized: [Ours, Theirs]);

        var outcome = await Sweeper(host).RunOnceAsync();

        Assert.Equal(2, outcome.Companies);
        Assert.Equal(2, outcome.Swept);
    }

    [Fact]
    public async Task ACompanyThatIsNotDueIsSkipped()
    {
        // The whole reason each company carries its own schedule.
        using var host = await BuildAsync(authorized: [Ours, Theirs]);

        await Sweeper(host).RunOnceAsync();
        var second = await Sweeper(host).RunOnceAsync();

        Assert.Equal(2, second.Companies);
        Assert.Equal(0, second.Swept);
    }

    [Fact]
    public async Task ACompanyBecomesDueAgainAfterTheInterval()
    {
        using var host = await BuildAsync(authorized: [Ours]);

        await Sweeper(host).RunOnceAsync();
        await AgeCursorsAsync(host, by: TimeSpan.FromHours(1));
        var second = await Sweeper(host).RunOnceAsync();

        Assert.Equal(1, second.Swept);
    }

    [Fact]
    public async Task NothingAuthorizedIsNoWorkAtAll()
    {
        using var host = await BuildAsync(authorized: []);

        var outcome = await Sweeper(host).RunOnceAsync();

        Assert.Equal(0, outcome.Companies);
        Assert.False(outcome.DidWork);
    }

    [Fact]
    public async Task TheCursorAdvancesSoASecondReadDoesNotRepeatTheFirst()
    {
        using var host = await BuildAsync(authorized: [Ours]);
        await fixture.SeedIncomingMessageAsync(SampleInvoiceXml);

        var first = await Sweeper(host).RunOnceAsync();
        await AgeCursorsAsync(host, by: TimeSpan.FromHours(1));
        var second = await Sweeper(host).RunOnceAsync();

        Assert.Equal(1, first.Added);
        Assert.Equal(0, second.Added);
    }

    [Fact]
    public async Task AMessageResolvedThroughStareMesajIsNotRecordedTwice()
    {
        // Some messages carry only an id_solicitare, and their download identifier is discovered a
        // call later. The inbox sync asks the database which of the listed identifiers it already
        // holds - and this one is not in that listing, so it needs its own check. Without one, a
        // second sync inserts it again and collides on the primary key.
        using var host = await BuildAsync(authorized: [Ours]);
        await fixture.SeedIncomingMessageAsync(SampleInvoiceXml, hideId: true);

        var first = await Sweeper(host).RunOnceAsync();

        // The watermark alone would hide this: a second sweep lists only what arrived since, so the
        // message would not come back and nothing would be re-inserted. Rewinding it forces the
        // overlap the known-identifier set exists for, which is the only way this path is reached.
        await RewindCursorsAsync(host, by: TimeSpan.FromDays(1));
        var second = await Sweeper(host).RunOnceAsync();

        Assert.Equal(1, first.Added);
        Assert.Equal(0, second.Added);
        Assert.Equal(0, second.Failed);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EFacturaDbContext>();
        Assert.Single(await db.InboxMessages.Where(m => m.Cif == Ours).ToListAsync());
    }

    // ------------------------------------------------------- when it goes wrong

    [Fact]
    public async Task ACompanyWithoutAUsableAuthorizationIsDeferredRatherThanRetriedEveryPass()
    {
        // A company whose authorization has lapsed would otherwise be tried on every pass forever,
        // filling the log with a rights problem nothing is going to resolve on its own.
        using var host = await BuildAsync(authorized: [Ours], withTokens: false);

        var first = await Sweeper(host).RunOnceAsync();
        var second = await Sweeper(host).RunOnceAsync();

        Assert.Equal(1, first.Unauthorized);
        Assert.Equal(0, second.Swept);
        Assert.Equal(0, second.Unauthorized);
    }

    [Fact]
    public async Task TheFailureIsRecordedWhereSomebodyDiagnosingAQuietInboxWouldLook()
    {
        using var host = await BuildAsync(authorized: [Ours], withTokens: false);

        await Sweeper(host).RunOnceAsync();

        using var scope = host.Services.CreateScope();
        var cursor = await scope.ServiceProvider.GetRequiredService<EFacturaDbContext>()
            .InboxCursors.SingleAsync(c => c.Cif == Ours);

        Assert.Equal(1, cursor.ConsecutiveFailures);
        Assert.NotNull(cursor.LastError);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(50)]
    public void TheBackoffWidensAndIsCapped(int failures)
    {
        var backoff = InboxSweeper.Backoff(failures, TimeSpan.FromMinutes(15));

        Assert.True(backoff >= TimeSpan.FromMinutes(15));
        Assert.True(backoff <= TimeSpan.FromDays(1), $"Backoff of {backoff} exceeds the daily cap.");
    }

    // ------------------------------------------------------------- the harness

    private static InboxSweeper Sweeper(IHost host) => host.Services.GetRequiredService<InboxSweeper>();

    /// <summary>
    /// Moves the watermark back, so the next sweep lists messages it has already recorded.
    /// </summary>
    /// <remarks>
    /// Overlap is what the known-identifier set guards against, and the watermark normally prevents
    /// it — so a test of that guard has to create the overlap deliberately.
    /// </remarks>
    private static async Task RewindCursorsAsync(IHost host, TimeSpan by)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EFacturaDbContext>();

        foreach (var cursor in await db.InboxCursors.ToListAsync())
        {
            cursor.SyncedUpTo -= by;
            cursor.NextSyncAt -= by;
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Moves every cursor's next-due time into the past, as elapsed time would.</summary>
    private static async Task AgeCursorsAsync(IHost host, TimeSpan by)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EFacturaDbContext>();

        foreach (var cursor in await db.InboxCursors.ToListAsync())
        {
            cursor.NextSyncAt = cursor.NextSyncAt - by;
        }

        await db.SaveChangesAsync();
    }

    private async Task<IHost> BuildAsync(string[] authorized, bool withTokens = true)
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        builder.AddRomaniaEFactura(
            o =>
            {
                o.ClientId = "test-client";
                o.ClientSecret = "test-secret";
                o.RedirectUri = "https://app.example.ro/efactura/callback";
                o.Cif = Ours;
                o.ApiBaseAddress = new Uri(fixture.Server.BaseAddress, "test/FCTEL/rest");
                o.MinimumDelayBetweenCalls = TimeSpan.Zero;
                o.EnableReconciler = false;
                o.InboxSyncInterval = TimeSpan.FromMinutes(15);
            },
            db => db.UseSqlite(connection));

        builder.Services.AddSingleton<IHttpClientFactory>(new MockHttpClientFactory(fixture));

        var host = builder.Build();
        await host.StartAsync();

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EFacturaDbContext>();
            await db.Database.EnsureCreatedAsync();

            var store = scope.ServiceProvider.GetRequiredService<IEFacturaTokenStore>();
            foreach (var cif in authorized)
            {
                await store.SaveAsync(new EFacturaToken
                {
                    Cif = cif,
                    AccessToken = withTokens ? "mock-access-token-initial" : string.Empty,
                    RefreshToken = withTokens ? "mock-refresh-token" : string.Empty,
                    AccessTokenExpiresAt = withTokens
                        ? DateTimeOffset.UtcNow.AddHours(1)
                        : DateTimeOffset.UtcNow.AddHours(-1),
                });
            }
        }

        return host;
    }

    private const string SampleInvoiceXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"><ID>FCT-IN-1</ID></Invoice>
        """;
}
