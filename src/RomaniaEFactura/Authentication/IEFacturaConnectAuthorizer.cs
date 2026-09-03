using System.Security.Claims;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Authentication;

/// <summary>
/// Decides whether a signed-in person may connect a particular company to ANAF.
/// </summary>
/// <remarks>
/// <para>
/// Requiring an authenticated user establishes <em>that</em> somebody is signed in. On a
/// deployment serving one company that is the whole question. On one serving many it is only half
/// of it: the CIF arrives in the path, so without this any authenticated user could name any
/// company.
/// </para>
/// <para>
/// What that buys an attacker is not theoretical. The callback writes an ANAF authorization into
/// the token store, and the store overwrites the row for a CIF unconditionally — so an ordinary
/// member of one business can bind their own ANAF identity to another business, and in doing so
/// replace a working authorization with one that has no rights over it. Undoing that needs the
/// real certificate holder.
/// </para>
/// <para>
/// The library cannot answer this itself: only the host knows how a signed-in person maps to the
/// businesses they may act for.
/// </para>
/// </remarks>
public interface IEFacturaConnectAuthorizer
{
    /// <summary>Whether <paramref name="user"/> may connect <paramref name="cif"/>.</summary>
    /// <param name="user">The signed-in person.</param>
    /// <param name="cif">The company they named, normalised.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    ValueTask<bool> CanConnectAsync(
        ClaimsPrincipal user,
        string cif,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The authorizer used when a host registers none: only the configured company may be connected.
/// </summary>
/// <remarks>
/// <para>
/// Correct for the case it serves — a deployment that names one company in configuration has
/// exactly one company anybody should ever be connecting — and refusing everything else is the
/// safe direction for every other case.
/// </para>
/// <para>
/// It refuses everything when no company is configured, which is what an application serving
/// several looks like before it has registered a real authorizer. That is deliberate: the
/// alternative default is to allow any authenticated user to connect any company, and that is the
/// defect this exists to close, not a convenience worth keeping.
/// </para>
/// </remarks>
internal sealed class ConfiguredCompanyConnectAuthorizer(IOptions<EFacturaOptions> options)
    : IEFacturaConnectAuthorizer
{
    private readonly EFacturaOptions _options = options.Value;

    public ValueTask<bool> CanConnectAsync(
        ClaimsPrincipal user,
        string cif,
        CancellationToken cancellationToken = default)
    {
        var configured = RomanianCif.Normalize(_options.Cif);

        return ValueTask.FromResult(
            !string.IsNullOrEmpty(configured)
            && string.Equals(configured, RomanianCif.Normalize(cif), StringComparison.Ordinal));
    }
}
