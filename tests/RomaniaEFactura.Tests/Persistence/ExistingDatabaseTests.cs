using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RomaniaEFactura.Persistence;

namespace RomaniaEFactura.Tests.Persistence;

/// <summary>
/// Adding the library to a database that already belongs to an application.
/// </summary>
/// <remarks>
/// <para>
/// Every other persistence test starts from an empty database, which is the one case where the
/// distinction below does not matter — and so the case that hid this for as long as it existed.
/// </para>
/// <para>
/// The initializer used to ask <c>RelationalDatabaseCreator.HasTablesAsync()</c>, which answers
/// "does this database contain any tables at all". Against an application's own database that is
/// true because of the host's tables, so the library's were never created, and every call
/// afterwards failed with a missing relation — under a log line stating that the schema already
/// existed.
/// </para>
/// </remarks>
public class ExistingDatabaseTests
{
    [Fact]
    public async Task TheTablesAreCreatedAlongsideAnApplicationsOwn()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        // Somebody else's table, as an application adopting the library would have.
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE Businesses (Id INTEGER PRIMARY KEY, Name TEXT);";
            await command.ExecuteNonQueryAsync();
        }

        await using var provider = BuildProvider(connection);
        await provider.EnsureEFacturaSchemaAsync();

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EFacturaDbContext>();

        // The assertion the old code failed: querying a table that was never created throws.
        Assert.Empty(await db.Tokens.ToListAsync());
        Assert.Empty(await db.Submissions.ToListAsync());
        Assert.Empty(await db.InboxMessages.ToListAsync());
        Assert.Empty(await db.InboxCursors.ToListAsync());
    }

    [Fact]
    public async Task AnEmptyDatabaseStillWorks()
    {
        // The case that always worked, kept so the fix cannot break it.
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using var provider = BuildProvider(connection);
        await provider.EnsureEFacturaSchemaAsync();

        using var scope = provider.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<EFacturaDbContext>().Tokens.ToListAsync());
    }

    [Fact]
    public async Task CallingItTwiceIsHarmless()
    {
        // Documented as safe to call on every start, so it has to be.
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        await using var provider = BuildProvider(connection);
        await provider.EnsureEFacturaSchemaAsync();
        await provider.EnsureEFacturaSchemaAsync();

        using var scope = provider.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<EFacturaDbContext>().Tokens.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task TheSameHoldsOnPostgreSql()
    {
        // The provider the finding actually came from, and the one the first consumer runs.
        var schema = "efactura_existing_" + Guid.NewGuid().ToString("n")[..12];

        var services = new ServiceCollection();
        services.AddDbContext<EFacturaDbContext>(o =>
            o.UseNpgsql(PostgreSql.ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrations", schema)));

        await using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EFacturaDbContext>();
            await db.Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{schema}\";");
            await db.Database.ExecuteSqlRawAsync(
                $"CREATE TABLE IF NOT EXISTS \"{schema}\".\"Businesses\" (\"Id\" int primary key);");
        }

        try
        {
            await provider.EnsureEFacturaSchemaAsync();

            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EFacturaDbContext>();
            Assert.Empty(await db.Tokens.ToListAsync());
        }
        finally
        {
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EFacturaDbContext>();
            await db.Database.ExecuteSqlRawAsync($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;");
        }
    }

    private static ServiceProvider BuildProvider(Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddDbContext<EFacturaDbContext>(o => o.UseSqlite(connection));

        return services.BuildServiceProvider();
    }
}
