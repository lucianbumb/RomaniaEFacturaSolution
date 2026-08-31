using Microsoft.Extensions.Options;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Configuration;

/// <summary>
/// Checks the configuration at startup, so a mistake in it is not discovered as an ANAF error
/// hours later.
/// </summary>
/// <remarks>
/// <para>
/// Most of these would otherwise surface wearing somebody else's clothes. An empty
/// <see cref="EFacturaOptions.ClientSecret"/> produces an <c>Authorization: Basic</c> header built
/// from <c>client-id:</c>, and ANAF answers 401 — which reads exactly like an expired
/// authorization, and sends whoever is debugging it to the certificate holder rather than to
/// <c>appsettings.json</c>.
/// </para>
/// <para>
/// The base-address rules are the reason this is worth doing at all rather than merely tidy. Those
/// overrides exist so the library can be pointed at the mock server, and they will just as happily
/// point a production deployment at a plaintext address — taking the bearer token, and on the OAuth
/// address the client secret, with them.
/// </para>
/// </remarks>
internal sealed class EFacturaOptionsValidator : IValidateOptions<EFacturaOptions>
{
    public ValidateOptionsResult Validate(string? name, EFacturaOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add("EFactura:ClientId is required. It is issued when the application is registered with ANAF.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add(
                "EFactura:ClientSecret is required. Without it ANAF answers 401, which is "
                + "indistinguishable from an expired authorization.");
        }

        ValidateRedirectUri(options, failures);

        // Deliberately optional. A deployment serving several companies passes the CIF per call,
        // which the whole interface is built for; only a value that is present has to be right.
        if (!string.IsNullOrWhiteSpace(options.Cif) && !RomanianCif.IsValid(options.Cif))
        {
            failures.Add(
                $"EFactura:Cif '{options.Cif}' is not a valid Romanian fiscal code — the control "
                + "digit does not match.");
        }

        ValidateBaseAddress(options.ApiBaseAddress, nameof(EFacturaOptions.ApiBaseAddress), failures);
        ValidateBaseAddress(options.OAuthBaseAddress, nameof(EFacturaOptions.OAuthBaseAddress), failures);

        if (options.Timeout <= TimeSpan.Zero)
        {
            failures.Add("EFactura:Timeout must be positive.");
        }

        if (options.MaxRetries < 0)
        {
            failures.Add("EFactura:MaxRetries cannot be negative.");
        }

        if (options.ReconcileInterval <= TimeSpan.Zero)
        {
            failures.Add(
                "EFactura:ReconcileInterval must be positive; PeriodicTimer rejects anything else "
                + "and the reconciler would fail on its first tick.");
        }

        if (options.ReconcileBatchSize < 1)
        {
            failures.Add("EFactura:ReconcileBatchSize must be at least 1, or no submission is ever settled.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRedirectUri(EFacturaOptions options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.RedirectUri))
        {
            failures.Add(
                "EFactura:RedirectUri is required, and must match the value registered with ANAF exactly.");
            return;
        }

        if (!Uri.TryCreate(options.RedirectUri, UriKind.Absolute, out var redirect))
        {
            failures.Add($"EFactura:RedirectUri '{options.RedirectUri}' is not an absolute URI.");
            return;
        }

        if (!IsSecureOrLoopback(redirect))
        {
            failures.Add(
                $"EFactura:RedirectUri '{options.RedirectUri}' is not https. ANAF returns the "
                + "authorization code to it, and over plaintext that code is readable in transit.");
        }
    }

    private static void ValidateBaseAddress(Uri? address, string setting, List<string> failures)
    {
        if (address is null) return;

        if (!address.IsAbsoluteUri)
        {
            failures.Add($"EFactura:{setting} must be an absolute URI.");
            return;
        }

        if (!IsSecureOrLoopback(address))
        {
            failures.Add(
                $"EFactura:{setting} '{address}' is not https. Every call to it carries the bearer "
                + "token, and the OAuth address carries the client secret as well. Override it with "
                + "a plaintext address only against a loopback host, which is what the mock server is.");
        }
    }

    /// <summary>
    /// Whether an address may carry a credential.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Loopback is the seam rather than a blanket https requirement, because pointing the library
    /// at the mock server is a supported and necessary thing to do — and a loopback address is not
    /// reachable from off the machine, so there is no transit to protect.
    /// </para>
    /// <para>
    /// The scheme is checked first, and that is not tidiness. <c>Uri.IsLoopback</c> is
    /// <see langword="true"/> for a <c>file://</c> URI, and on Unix a leading slash parses as one:
    /// <c>Uri.TryCreate("/efactura/callback", UriKind.Absolute, ...)</c> succeeds there and fails
    /// on Windows. Without this line a relative redirect URI — and any <c>file://</c> address —
    /// would be accepted on Linux and refused on Windows, which is how CI found it.
    /// </para>
    /// </remarks>
    private static bool IsSecureOrLoopback(Uri address)
    {
        if (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp) return false;

        return address.Scheme == Uri.UriSchemeHttps || address.IsLoopback;
    }
}
