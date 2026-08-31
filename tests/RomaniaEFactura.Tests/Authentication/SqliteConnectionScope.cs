using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RomaniaEFactura.Persistence;

namespace RomaniaEFactura.Tests.Authentication;

/// <summary>
/// A SQLite database held open for the life of a test.
/// </summary>
/// <remarks>
/// A real relational provider rather than the in-memory one, so the mapping, keys and
/// ExecuteDelete are genuinely exercised. The connection is kept open because an in-memory SQLite
/// database is discarded when its last connection closes — which would make the restart test
/// pass for the wrong reason, by finding nothing either time.
/// </remarks>
public sealed class SqliteConnectionScope : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteConnectionScope()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    /// <summary>Creates a fresh context over the same database, as a restarted process would.</summary>
    public EFacturaDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<EFacturaDbContext>().UseSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();
}
