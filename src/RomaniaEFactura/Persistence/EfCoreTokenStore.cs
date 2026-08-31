using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using RomaniaEFactura.Authentication;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Persistence;

/// <summary>
/// The durable token store, encrypting both tokens at rest.
/// </summary>
/// <remarks>
/// <para>
/// This is the default because ANAF authorization is expensive to replace: it needs a person, a
/// qualified certificate and usually a physical token. Anything that loses it — an in-memory
/// cache, an eviction policy, a process restart — turns a routine deployment into a business
/// interruption.
/// </para>
/// <para>
/// The access token's expiry is recorded but never acted on as a lifetime for the row. The refresh
/// token outlives the access token by roughly nine months, and deleting it early was the specific
/// defect in the previous version.
/// </para>
/// </remarks>
public sealed class EfCoreTokenStore : IEFacturaTokenStore
{
    private readonly EFacturaDbContext _db;
    private readonly IDataProtector _protector;

    /// <summary>Creates the store.</summary>
    public EfCoreTokenStore(EFacturaDbContext db, IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);

        _db = db;
        // Versioned purpose string: changing it rotates every stored token rather than silently
        // producing plaintext that cannot be decrypted.
        _protector = dataProtectionProvider.CreateProtector("RomaniaEFactura.Tokens.v1");
    }

    /// <inheritdoc />
    public async Task<EFacturaToken?> GetAsync(string cif, CancellationToken cancellationToken = default)
    {
        var company = RomanianCif.Normalize(cif);
        var stored = await _db.Tokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Cif == company, cancellationToken)
            .ConfigureAwait(false);

        if (stored is null) return null;

        // Keys can be rotated away or a backup restored onto a host without them. That leaves the
        // row undecryptable, which is indistinguishable from having no authorization at all.
        try
        {
            return new EFacturaToken
            {
                Cif = stored.Cif,
                AccessToken = _protector.Unprotect(stored.ProtectedAccessToken),
                RefreshToken = _protector.Unprotect(stored.ProtectedRefreshToken),
                AccessTokenExpiresAt = stored.AccessTokenExpiresAt,
                ObtainedAt = stored.ObtainedAt,
                UpdatedAt = stored.UpdatedAt,
            };
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(EFacturaToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        var company = RomanianCif.Normalize(token.Cif);
        var existing = await _db.Tokens
            .FirstOrDefaultAsync(t => t.Cif == company, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            _db.Tokens.Add(new StoredToken
            {
                Cif = company,
                ProtectedAccessToken = _protector.Protect(token.AccessToken),
                ProtectedRefreshToken = _protector.Protect(token.RefreshToken),
                AccessTokenExpiresAt = token.AccessTokenExpiresAt,
                ObtainedAt = token.ObtainedAt,
                UpdatedAt = token.UpdatedAt,
            });
        }
        else
        {
            existing.ProtectedAccessToken = _protector.Protect(token.AccessToken);
            existing.ProtectedRefreshToken = _protector.Protect(token.RefreshToken);
            existing.AccessTokenExpiresAt = token.AccessTokenExpiresAt;
            existing.UpdatedAt = token.UpdatedAt;
            // ObtainedAt deliberately keeps its original value: it records when the company was
            // first authorized, which a refresh does not change.
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string cif, CancellationToken cancellationToken = default)
    {
        var company = RomanianCif.Normalize(cif);
        await _db.Tokens
            .Where(t => t.Cif == company)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListAuthorizedCifsAsync(
        CancellationToken cancellationToken = default) =>
        await _db.Tokens
            .AsNoTracking()
            .Select(t => t.Cif)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
