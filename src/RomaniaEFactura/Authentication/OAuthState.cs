using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace RomaniaEFactura.Authentication;

/// <summary>What the OAuth <c>state</c> parameter carries across the round trip to ANAF.</summary>
/// <param name="Cif">Which company is being authorized.</param>
/// <param name="ReturnUrl">Where to send the user once the callback completes.</param>
/// <param name="Nonce">Makes each state unique, so one cannot be replayed.</param>
/// <param name="IssuedAt">When the state was created, used to reject stale callbacks.</param>
/// <param name="User">
/// Who started the round trip, so a callback returning as somebody else can be refused. Null when
/// nobody was signed in, which is the case only where the endpoints were mounted anonymously.
/// </param>
public sealed record OAuthState(
    string Cif,
    string? ReturnUrl,
    string Nonce,
    DateTimeOffset IssuedAt,
    string? User = null);

/// <summary>
/// Protects the OAuth <c>state</c> parameter so a callback cannot be forged.
/// </summary>
/// <remarks>
/// <para>
/// The state travels through the user's browser and comes back as a query parameter, so anything
/// readable from it is also writable. Earlier implementations — v2's and the reference
/// application's alike — used a bare <c>cif|returnUrl</c> string, which let an attacker choose
/// both: they could bind a captured authorization code to a company of their choosing, or point
/// the post-callback redirect anywhere.
/// </para>
/// <para>
/// Encrypting and signing it with <see cref="IDataProtector"/> closes both. Tampering fails to
/// unprotect rather than yielding a different valid-looking state.
/// </para>
/// </remarks>
public sealed class OAuthStateProtector(IDataProtectionProvider dataProtectionProvider)
{
    /// <summary>How long an authorization round trip may take before its state is refused.</summary>
    public static TimeSpan Lifetime => TimeSpan.FromMinutes(15);

    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector("RomaniaEFactura.OAuthState.v1");

    /// <summary>The clock, overridable so tests can age a state.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>Creates a protected state for an authorization request.</summary>
    /// <param name="cif">The company being authorized.</param>
    /// <param name="returnUrl">Where to send the person once the callback completes.</param>
    /// <param name="user">
    /// Who is starting the round trip. Carried so the callback can refuse a state completed by a
    /// different person, which is what stops one authenticated user handing another a link that
    /// binds the first user's ANAF identity to the application.
    /// </param>
    public string Protect(string cif, string? returnUrl, string? user = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cif);

        var state = new OAuthState(
            cif,
            returnUrl,
            Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)),
            Clock(),
            user);

        return _protector.Protect(JsonSerializer.Serialize(state));
    }

    /// <summary>
    /// Recovers a state from a callback, returning <see langword="null"/> when it was tampered
    /// with, was not issued by this application, or has expired.
    /// </summary>
    /// <remarks>
    /// Returns null rather than throwing: a bad state is an attacker or a stale bookmark, both of
    /// which a callback endpoint has to handle gracefully rather than as a server error.
    /// </remarks>
    public OAuthState? Unprotect(string? protectedState)
    {
        if (string.IsNullOrWhiteSpace(protectedState)) return null;

        string payload;
        try
        {
            payload = _protector.Unprotect(protectedState);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Tampered, forged, or protected with different keys.
            return null;
        }

        OAuthState? state;
        try
        {
            state = JsonSerializer.Deserialize<OAuthState>(payload);
        }
        catch (JsonException)
        {
            return null;
        }

        if (state is null) return null;

        // A state that has been sitting around too long is refused even if it is authentic.
        return Clock() - state.IssuedAt > Lifetime ? null : state;
    }
}
