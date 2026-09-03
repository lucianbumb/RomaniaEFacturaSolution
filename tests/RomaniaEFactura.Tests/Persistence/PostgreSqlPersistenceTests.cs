using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using RomaniaEFactura.Authentication;
using RomaniaEFactura.Persistence;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura.Tests.Persistence;

/// <summary>
/// The library's own storage, against PostgreSQL rather than SQLite.
/// </summary>
/// <remarks>
/// <para>
/// Everything else in the suite runs on SQLite. That is not the same database, and the differences
/// are not cosmetic:
/// </para>
/// <list type="bullet">
/// <item><description>
/// Npgsql maps <c>timestamp with time zone</c> and <b>refuses a <c>DateTime</c> whose kind is not
/// UTC</b>. The context converts every <see cref="DateTimeOffset"/> to a UTC <c>DateTime</c>, added
/// because SQLite cannot order a <c>DateTimeOffset</c> at all — so the workaround for one provider
/// is the thing the other is strict about.
/// </description></item>
/// <item><description>
/// Archives are <c>bytea</c> rather than a BLOB.
/// </description></item>
/// <item><description>
/// <c>ExecuteDeleteAsync</c>, used to remove an authorization, is translated by each provider
/// itself.
/// </description></item>
/// </list>
/// <para>
/// Each test uses its own schema so they can run in parallel against one server and leave nothing
/// behind.
/// </para>
/// </remarks>
[Collection("PostgreSql")]
public sealed class PostgreSqlPersistenceTests : IAsyncLifetime
{
    private readonly string _schema = "efactura_test_" + Guid.NewGuid().ToString("n")[..12];
    private EFacturaDbContext _db = null!;

    public async Task InitializeAsync()
    {
        if (!PostgreSql.IsConfigured) return;

        _db = CreateContext();

        // Not EnsureCreatedAsync. It creates nothing once the database exists, and the CI database
        // does - so the first test to run created its schema and the rest silently got none. It
        // passed on a fresh run and failed on a re-run, which is exactly how it was found.
        await _db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{_schema}\";");
        await ((RelationalDatabaseCreator)_db.GetService<IDatabaseCreator>()).CreateTablesAsync();
    }

    public async Task DisposeAsync()
    {
        if (!PostgreSql.IsConfigured) return;

        await _db.Database.ExecuteSqlRawAsync($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE;");
        await _db.DisposeAsync();
    }

    [PostgreSqlFact]
    public async Task TheSchemaIsCreatedFromTheModel()
    {
        // The path a host without its own migrations takes. If the model cannot be realised on
        // PostgreSQL at all, nothing below would run either — so this is the first thing to know.
        Assert.True(await _db.Database.CanConnectAsync());
        Assert.Empty(await _db.Tokens.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task ATokenSurvivesARoundTripEncrypted()
    {
        var store = CreateStore(_db);
        var obtained = new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.FromHours(2));

        await store.SaveAsync(new EFacturaToken
        {
            Cif = "12345674",
            AccessToken = "access-token",
            RefreshToken = "refresh-token",
            AccessTokenExpiresAt = obtained.AddHours(1),
            ObtainedAt = obtained,
            UpdatedAt = obtained,
        });

        var read = await store.GetAsync("12345674");

        Assert.NotNull(read);
        Assert.Equal("access-token", read.AccessToken);
        Assert.Equal("refresh-token", read.RefreshToken);

        // The instant has to survive, not merely the clock reading. Npgsql normalises to UTC, so a
        // value stored from a +02:00 offset comes back as the same moment expressed differently.
        Assert.Equal(obtained.ToUniversalTime(), read.ObtainedAt.ToUniversalTime());
    }

    [PostgreSqlFact]
    public async Task RemovingAnAuthorizationTranslatesOnThisProvider()
    {
        // ExecuteDeleteAsync is provider-translated, so it is worth exercising rather than assuming.
        var store = CreateStore(_db);
        await store.SaveAsync(NewToken("12345674"));

        await store.RemoveAsync("12345674");

        Assert.Null(await store.GetAsync("12345674"));
        Assert.Empty(await store.ListAuthorizedCifsAsync());
    }

    [PostgreSqlFact]
    public async Task AnArchiveSurvivesAsBytes()
    {
        var archive = new byte[4096];
        Random.Shared.NextBytes(archive);

        _db.Submissions.Add(new EFacturaSubmission
        {
            UploadIndex = "5001",
            Cif = "12345674",
            State = UploadState.Ok,
            Archive = archive,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var read = await _db.Submissions.SingleAsync(s => s.UploadIndex == "5001");

        Assert.Equal(archive, read.Archive);
    }

    [PostgreSqlFact]
    public async Task SubmissionsAreOrderedByTimeOnThisProvider()
    {
        // The converter exists because SQLite cannot order a DateTimeOffset. The ordering has to
        // still be right where the provider could have handled it natively.
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 3; i++)
        {
            _db.Submissions.Add(new EFacturaSubmission
            {
                UploadIndex = $"600{i}",
                Cif = "12345674",
                State = UploadState.InProgress,
                SubmittedAt = now.AddMinutes(-i),
                NextPollAt = now.AddMinutes(-i),
            });
        }

        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var due = await _db.Submissions
            .Where(s => s.State == UploadState.InProgress && s.NextPollAt <= now)
            .OrderBy(s => s.NextPollAt)
            .Select(s => s.UploadIndex)
            .ToListAsync();

        Assert.Equal(["6002", "6001", "6000"], due);
    }

    [PostgreSqlFact]
    public async Task TheInboxCursorRoundTripsItsSchedule()
    {
        var now = DateTimeOffset.UtcNow;

        _db.InboxCursors.Add(new EFacturaInboxCursor
        {
            Cif = "12345674",
            SyncedUpTo = now.AddDays(-1),
            LastSyncedAt = now,
            NextSyncAt = now.AddMinutes(15),
            ConsecutiveFailures = 2,
            LastError = "NoRights: nu aveti drept",
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var read = await _db.InboxCursors.SingleAsync();

        Assert.Equal(2, read.ConsecutiveFailures);
        Assert.Equal("NoRights: nu aveti drept", read.LastError);
        Assert.Equal(now.AddMinutes(15).ToUniversalTime(), read.NextSyncAt.ToUniversalTime(), TimeSpan.FromSeconds(1));
    }

    // ------------------------------------------------------------- the harness

    private static EFacturaToken NewToken(string cif) => new()
    {
        Cif = cif,
        AccessToken = "access",
        RefreshToken = "refresh",
        AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
    };

    private static EfCoreTokenStore CreateStore(EFacturaDbContext db) =>
        new(db, DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(Path.GetTempPath(), "romania-efactura-tests", "postgres"))));

    private EFacturaDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EFacturaDbContext>()
            .UseNpgsql(PostgreSql.ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrations", _schema))
            .Options;

        return new SchemaScopedContext(options, _schema);
    }

    /// <summary>Puts this test's tables in their own schema, so tests do not collide.</summary>
    private sealed class SchemaScopedContext(DbContextOptions<EFacturaDbContext> options, string schema)
        : EFacturaDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.HasDefaultSchema(schema);
        }
    }
}

/// <summary>
/// Keeps the PostgreSQL tests in one collection.
/// </summary>
/// <remarks>
/// They each use their own schema, so this is not about isolation — it is so a suite run against a
/// small server does not open a connection pool per test class at once.
/// </remarks>
[CollectionDefinition("PostgreSql")]
public sealed class PostgreSqlCollection;
