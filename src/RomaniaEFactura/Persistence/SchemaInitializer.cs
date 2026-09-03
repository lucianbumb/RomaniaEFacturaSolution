using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RomaniaEFactura.Persistence;

/// <summary>
/// Creates the library's own table in whatever database the host has configured.
/// </summary>
/// <remarks>
/// <para>
/// The library deliberately ships no migrations. A migration is provider-specific — one generated
/// for SQLite emits SQL that will not run on SQL Server — so committing a set for any single
/// provider would work for some consumers and quietly mislead the rest, while the presence of a
/// Migrations folder implies it works for everyone.
/// </para>
/// <para>
/// Instead this applies migrations if the host has generated any against
/// <see cref="EFacturaDbContext"/>, and otherwise creates the table directly. A consumer who wants
/// the schema under their own migration history can generate migrations against this context in
/// their own project, where the provider is known, and this method will then apply them.
/// </para>
/// </remarks>
public static class SchemaInitializer
{
    /// <summary>
    /// Ensures the token table exists. Safe to call on every start.
    /// </summary>
    /// <param name="services">The application's service provider.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    public static async Task EnsureEFacturaSchemaAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EFacturaDbContext>();
        var logger = scope.ServiceProvider
            .GetService<ILoggerFactory>()
            ?.CreateLogger(typeof(SchemaInitializer).FullName!);

        // A provider without a relational creator - the in-memory provider, for instance - has
        // nothing to create.
        if (db.Database.GetService<IDatabaseCreator>() is not RelationalDatabaseCreator creator)
        {
            logger?.LogDebug("The configured EF Core provider is not relational; no schema work to do.");
            return;
        }

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken).ConfigureAwait(false)).ToList();

        if (pending.Count > 0 || applied.Count > 0)
        {
            // The host generated migrations against this context, so their history is authoritative.
            logger?.LogInformation(
                "Applying {Count} pending e-Factura migration(s).", pending.Count);
            await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        // No migrations anywhere: create just this context's tables, leaving any other schema in
        // the database untouched.
        //
        // Asked about this context's own tables rather than through RelationalDatabaseCreator's
        // HasTablesAsync, which answers "does this database contain any tables at all". That is the
        // same question only for a database dedicated to the library - every test, and the sample
        // app. Against an application's existing database it is true because of the host's own
        // tables, so the library's were never created and every call afterwards failed with a
        // missing relation, under a log line saying the schema already existed.
        if (await HasOwnTablesAsync(db, cancellationToken).ConfigureAwait(false))
        {
            logger?.LogDebug("The e-Factura tables are already present.");
            return;
        }

        logger?.LogInformation("Creating the e-Factura tables.");
        await creator.CreateTablesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether this context's own tables exist.
    /// </summary>
    /// <remarks>
    /// By querying one rather than by reading metadata: there is no cross-provider metadata query.
    /// <c>INFORMATION_SCHEMA</c> is absent on SQLite, which keeps its catalogue in
    /// <c>sqlite_master</c>, so anything portable has to ask the database a question it will answer
    /// either way. A provider error here means the table is not there, which is the only thing this
    /// needs to distinguish.
    /// </remarks>
    private static async Task<bool> HasOwnTablesAsync(EFacturaDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.Tokens.AnyAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DbException)
        {
            return false;
        }
    }
}
