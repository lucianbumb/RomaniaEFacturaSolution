namespace RomaniaEFactura.Tests.Persistence;

/// <summary>
/// Whether a PostgreSQL server is available to test against, and how to reach it.
/// </summary>
/// <remarks>
/// <para>
/// The library's persistence had only ever been exercised against SQLite, and the first
/// application to adopt it runs PostgreSQL. Npgsql differs in ways this schema touches directly —
/// it maps <c>timestamp with time zone</c> and refuses a <c>DateTime</c> whose kind is not UTC,
/// where the context's converter exists precisely because SQLite cannot order a
/// <see cref="DateTimeOffset"/> at all.
/// </para>
/// <para>
/// Configured by environment variable rather than started by the test, following the same shape as
/// the live ANAF run: a dependency CI can provide and a workstation may not. CI supplies a service
/// container and then asserts these did not skip, so "not configured" cannot quietly become "not
/// tested".
/// </para>
/// </remarks>
public static class PostgreSql
{
    /// <summary>The environment variable naming the server.</summary>
    public const string ConnectionStringVariable = "EFACTURA_TEST_POSTGRES";

    /// <summary>The connection string, or null when none is configured.</summary>
    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } value
            ? value
            : null;

    /// <summary>Whether a server is available.</summary>
    public static bool IsConfigured => ConnectionString is not null;

    /// <summary>Why the tests were skipped, phrased so a reader knows what to do about it.</summary>
    public static string SkipReason =>
        $"No PostgreSQL server is configured. Set {ConnectionStringVariable} to a connection "
        + "string to run these; CI supplies one through a service container.";
}

/// <summary>A fact that runs only when a PostgreSQL server is configured.</summary>
public sealed class PostgreSqlFactAttribute : FactAttribute
{
    /// <summary>Marks the test skipped unless a server is available.</summary>
    public PostgreSqlFactAttribute()
    {
        if (!PostgreSql.IsConfigured) Skip = PostgreSql.SkipReason;
    }
}
