using Microsoft.Extensions.Logging;
using RomaniaEFactura.Transport;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Authentication;

/// <summary>
/// Supplies access tokens to the transport, refreshing them as they age.
/// </summary>
/// <remarks>
/// This is where the refresh actually happens, transparently, so no caller has to think about
/// token lifetimes. Refreshes for one company are serialized: several concurrent calls finding an
/// expired token would otherwise all refresh at once, and ANAF may invalidate the older refresh
/// tokens as it issues new ones.
/// </remarks>
public sealed class StoredTokenAccessTokenProvider(
    IEFacturaTokenStore store,
    IAnafOAuthClient oauthClient,
    ILogger<StoredTokenAccessTokenProvider> logger) : IAnafAccessTokenProvider
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> RefreshGates =
        new(StringComparer.Ordinal);

    /// <summary>The clock, overridable so tests can age a token.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public async Task<string?> GetAccessTokenAsync(string cif, CancellationToken cancellationToken = default)
    {
        var company = RomanianCif.Normalize(cif);
        if (string.IsNullOrEmpty(company)) return null;

        var token = await store.GetAsync(company, cancellationToken).ConfigureAwait(false);
        if (token is null) return null;
        if (token.IsAccessTokenUsable(Clock())) return token.AccessToken;

        var gate = RefreshGates.GetOrAdd(company, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may have refreshed while this one waited.
            token = await store.GetAsync(company, cancellationToken).ConfigureAwait(false);
            if (token is null) return null;
            if (token.IsAccessTokenUsable(Clock())) return token.AccessToken;

            if (!token.CanRefresh)
            {
                logger.LogWarning(
                    "The access token for CIF {Cif} has expired and there is no refresh token; "
                    + "someone must authorize again with a certificate.", company);
                return null;
            }

            var refreshed = await oauthClient.RefreshAsync(token, cancellationToken).ConfigureAwait(false);
            if (refreshed.IsSuccess)
            {
                await store.SaveAsync(refreshed.Value, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Refreshed the ANAF access token for CIF {Cif}.", company);
                return refreshed.Value.AccessToken;
            }

            // A refusal is not necessarily fatal: ANAF being unreachable says nothing about
            // whether the refresh token is still good, and discarding it on a transient failure
            // would force a certificate login that was never actually needed.
            if (refreshed.Error!.IsTransient)
            {
                logger.LogWarning(
                    "Could not refresh the token for CIF {Cif} ({Error}); the stored authorization is kept.",
                    company, refreshed.Error);
                return null;
            }

            logger.LogWarning(
                "ANAF rejected the refresh token for CIF {Cif} ({Error}); the authorization is being removed.",
                company, refreshed.Error);

            await store.RemoveAsync(company, cancellationToken).ConfigureAwait(false);
            return null;
        }
        finally
        {
            gate.Release();
        }
    }
}
