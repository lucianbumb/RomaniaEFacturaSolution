using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using RomaniaEFactura.Persistence;

namespace RomaniaEFactura.Tests.Persistence;

/// <summary>
/// An <see cref="EFacturaDbContext"/> whose tables live in a schema of the test's choosing.
/// </summary>
/// <remarks>
/// So the PostgreSQL tests can run against one shared server without colliding, and can drop
/// everything they created afterwards.
/// </remarks>
public class SchemaScopedContext(DbContextOptions<EFacturaDbContext> options, string schema)
    : EFacturaDbContext(options)
{
    /// <summary>The schema this context's tables live in.</summary>
    public string Schema { get; } = schema;

    /// <summary>Builds the options, including the part that makes the schema actually take effect.</summary>
    public static DbContextOptions<EFacturaDbContext> Options(string connectionString, string schema) =>
        new DbContextOptionsBuilder<EFacturaDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrations", schema))
            // Without this every context of this type shares one cached model - the first one built
            // - so a second schema is ignored and both try to create tables in the first's. That
            // reads as "relation already exists" from a test that has never run before.
            .ReplaceService<IModelCacheKeyFactory, SchemaAwareModelCacheKeyFactory>()
            .Options;

    /// <summary>Creates the schema and this context's tables in it.</summary>
    public async Task CreateAsync()
    {
        await Database.ExecuteSqlRawAsync($"CREATE SCHEMA IF NOT EXISTS \"{Schema}\";");
        await ((RelationalDatabaseCreator)this.GetService<IDatabaseCreator>()).CreateTablesAsync();
    }

    /// <summary>Removes everything this context created.</summary>
    public Task DropAsync() => Database.ExecuteSqlRawAsync($"DROP SCHEMA IF EXISTS \"{Schema}\" CASCADE;");

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>Keys the cached model by schema as well as by context type.</summary>
    private sealed class SchemaAwareModelCacheKeyFactory : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) =>
            context is SchemaScopedContext scoped
                ? (context.GetType(), scoped.Schema, designTime)
                : (context.GetType(), string.Empty, designTime);
    }
}
