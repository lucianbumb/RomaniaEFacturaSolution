using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace RomaniaEFactura.Persistence;

/// <summary>
/// The library's own database context, holding ANAF authorizations.
/// </summary>
/// <remarks>
/// Kept separate from the host application's context so its migrations are independent: an
/// application should be able to take a library upgrade without entangling it with its own schema
/// history.
/// </remarks>
public class EFacturaDbContext(DbContextOptions<EFacturaDbContext> options) : DbContext(options)
{
    /// <summary>Stored ANAF authorizations, one per company.</summary>
    public DbSet<StoredToken> Tokens => Set<StoredToken>();

    /// <summary>Documents submitted to ANAF, tracked until their outcome is known.</summary>
    public DbSet<EFacturaSubmission> Submissions => Set<EFacturaSubmission>();

    /// <summary>Messages seen in the SPV inbox, recorded so each is downloaded only once.</summary>
    public DbSet<EFacturaInboxMessage> InboxMessages => Set<EFacturaInboxMessage>();

    /// <summary>How far the inbox has been read, per company.</summary>
    public DbSet<EFacturaInboxCursor> InboxCursors => Set<EFacturaInboxCursor>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        // SQLite cannot ORDER BY a DateTimeOffset, and the library's queries are ordered by time,
        // so a consumer on SQLite would hit NotSupportedException at runtime. Every timestamp here
        // is UTC by construction, so storing them as UTC DateTime costs nothing and sorts
        // correctly on every provider. The public model keeps DateTimeOffset.
        var utcTimestamp = new ValueConverter<DateTimeOffset, DateTime>(
            value => value.UtcDateTime,
            value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

        var nullableUtcTimestamp = new ValueConverter<DateTimeOffset?, DateTime?>(
            value => value == null ? null : value.Value.UtcDateTime,
            value => value == null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)));

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset)) property.SetValueConverter(utcTimestamp);
                else if (property.ClrType == typeof(DateTimeOffset?)) property.SetValueConverter(nullableUtcTimestamp);
            }
        }

        modelBuilder.Entity<StoredToken>(entity =>
        {
            entity.ToTable("EFacturaTokens");

            // One authorization per company, so the CIF is the natural key.
            entity.HasKey(t => t.Cif);
            entity.Property(t => t.Cif).HasMaxLength(20);

            // Both token columns hold ciphertext, which is considerably longer than the token.
            entity.Property(t => t.ProtectedAccessToken).IsRequired();
            entity.Property(t => t.ProtectedRefreshToken).IsRequired();

            entity.Property(t => t.AccessTokenExpiresAt).IsRequired();
            entity.Property(t => t.ObtainedAt).IsRequired();
            entity.Property(t => t.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<EFacturaSubmission>(entity =>
        {
            entity.ToTable("EFacturaSubmissions");
            entity.HasKey(s => s.UploadIndex);
            entity.Property(s => s.UploadIndex).HasMaxLength(64);
            entity.Property(s => s.Cif).HasMaxLength(20).IsRequired();
            entity.Property(s => s.DocumentId).HasMaxLength(200);
            entity.Property(s => s.DownloadId).HasMaxLength(64);
            entity.Property(s => s.LastError).HasMaxLength(2000);
            entity.Ignore(s => s.IsSettled);

            // The reconciler's query is "what is due, oldest first", so it is indexed for that.
            entity.HasIndex(s => new { s.State, s.NextPollAt });
        });

        modelBuilder.Entity<EFacturaInboxMessage>(entity =>
        {
            entity.ToTable("EFacturaInboxMessages");
            entity.HasKey(m => m.DownloadId);
            entity.Property(m => m.DownloadId).HasMaxLength(64);
            entity.Property(m => m.Cif).HasMaxLength(20).IsRequired();
            entity.Property(m => m.Type).HasMaxLength(100);
            entity.Property(m => m.RequestId).HasMaxLength(64);
            entity.Property(m => m.SupplierCif).HasMaxLength(20);
            entity.Property(m => m.CustomerCif).HasMaxLength(20);
            entity.Ignore(m => m.IsDownloaded);

            entity.HasIndex(m => new { m.Cif, m.CreatedAt });
        });

        modelBuilder.Entity<EFacturaInboxCursor>(entity =>
        {
            entity.ToTable("EFacturaInboxCursors");
            entity.HasKey(c => c.Cif);
            entity.Property(c => c.Cif).HasMaxLength(20);
            entity.Property(c => c.LastError).HasMaxLength(2000);

            // The sweep asks "which companies are due", so that is what is indexed.
            entity.HasIndex(c => c.NextSyncAt);
        });
    }
}

/// <summary>
/// The persisted form of an authorization, with both tokens encrypted.
/// </summary>
/// <remarks>
/// The tokens are stored as ciphertext so a database backup, a log of a query, or read access to
/// the table does not hand over the ability to file invoices as the company.
/// </remarks>
public sealed class StoredToken
{
    /// <summary>The company, normalised without the RO prefix.</summary>
    public required string Cif { get; set; }

    /// <summary>The access token, encrypted.</summary>
    public required string ProtectedAccessToken { get; set; }

    /// <summary>The refresh token, encrypted.</summary>
    public required string ProtectedRefreshToken { get; set; }

    /// <summary>
    /// When the access token expires. Recorded for scheduling only — it must never cause the row,
    /// and with it the refresh token, to be deleted.
    /// </summary>
    public required DateTimeOffset AccessTokenExpiresAt { get; set; }

    /// <summary>When the authorization was first granted.</summary>
    public required DateTimeOffset ObtainedAt { get; set; }

    /// <summary>When the tokens were last refreshed.</summary>
    public required DateTimeOffset UpdatedAt { get; set; }
}
