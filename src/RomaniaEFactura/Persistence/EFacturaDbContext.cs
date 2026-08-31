using Microsoft.EntityFrameworkCore;

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

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

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
