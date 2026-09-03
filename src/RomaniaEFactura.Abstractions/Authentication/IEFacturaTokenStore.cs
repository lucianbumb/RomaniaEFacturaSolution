namespace RomaniaEFactura.Authentication;

/// <summary>
/// Durable storage for ANAF authorizations, keyed by company.
/// </summary>
/// <remarks>
/// <para>
/// The contract is deliberately <c>(cif) → token</c> and nothing more. The previous version put
/// <c>HttpContext</c> into this interface, which made it unusable from worker services and
/// background jobs — and those are precisely where invoices get submitted, since the send and
/// reconcile cycle spans hours and cannot live inside a request.
/// </para>
/// <para>
/// Implementations must persist across process restarts and must never discard a refresh token
/// merely because the access token has expired.
/// </para>
/// </remarks>
public interface IEFacturaTokenStore
{
    /// <summary>
    /// Returns the stored authorization for a company, or <see langword="null"/> when the company
    /// has never been authorized or the authorization was revoked.
    /// </summary>
    Task<EFacturaToken?> GetAsync(string cif, CancellationToken cancellationToken = default);

    /// <summary>Stores an authorization, replacing any existing one for the same company.</summary>
    Task SaveAsync(EFacturaToken token, CancellationToken cancellationToken = default);

    /// <summary>Removes a company's authorization. The next call will report it unauthorized.</summary>
    Task RemoveAsync(string cif, CancellationToken cancellationToken = default);

    /// <summary>Lists the companies that currently have an authorization stored.</summary>
    /// <remarks>
    /// Needed by the reconciler, which has to work through every authorized company without a
    /// request to tell it which one it is serving.
    /// </remarks>
    Task<IReadOnlyList<string>> ListAuthorizedCifsAsync(CancellationToken cancellationToken = default);
}
