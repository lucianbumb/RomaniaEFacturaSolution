using Microsoft.AspNetCore.DataProtection;
using RomaniaEFactura.Authentication;

namespace RomaniaEFactura.Tests.Authentication;

/// <summary>
/// The OAuth <c>state</c> parameter, which is the only thing binding a callback to a request this
/// application actually made.
/// </summary>
/// <remarks>
/// Both the previous version and the reference application used a bare <c>cif|returnUrl</c>
/// string. The state travels through the user's browser, so anything readable from it is also
/// writable: an attacker could bind a captured authorization code to a company of their choosing,
/// or point the post-callback redirect anywhere.
/// </remarks>
public class OAuthStateProtectorTests
{
    [Fact]
    public void AProtectedStateRoundTrips()
    {
        var protector = CreateProtector();

        var state = protector.Unprotect(protector.Protect("12345674", "/invoices"));

        Assert.NotNull(state);
        Assert.Equal("12345674", state!.Cif);
        Assert.Equal("/invoices", state.ReturnUrl);
    }

    [Fact]
    public void TheStateIsNotReadableAsPlainText()
    {
        var protector = CreateProtector();

        var protectedState = protector.Protect("12345674", "/invoices");

        // If the CIF were readable it would also be writable.
        Assert.DoesNotContain("12345674", protectedState, StringComparison.Ordinal);
        Assert.DoesNotContain("/invoices", protectedState, StringComparison.Ordinal);
    }

    [Fact]
    public void ATamperedStateIsRejected()
    {
        var protector = CreateProtector();
        var protectedState = protector.Protect("12345674", "/invoices");

        // Flip a character; the payload no longer authenticates.
        var tampered = protectedState[..^2] + (protectedState[^2] == 'A' ? 'B' : 'A') + protectedState[^1];

        Assert.Null(protector.Unprotect(tampered));
    }

    [Fact]
    public void AStateForgedWithDifferentKeysIsRejected()
    {
        // An attacker can generate a well-formed state; they cannot sign it as this application.
        var attacker = CreateProtector(keyRing: "attacker");
        var application = CreateProtector();

        Assert.Null(application.Unprotect(attacker.Protect("99999999", "https://evil.example")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-protected-state")]
    public void AMissingOrNonsensicalStateIsRejectedWithoutThrowing(string? state)
    {
        // A callback endpoint must handle these gracefully; they mean an attacker or a stale
        // bookmark, neither of which is a server error.
        Assert.Null(CreateProtector().Unprotect(state));
    }

    [Fact]
    public void AnExpiredStateIsRejectedEvenThoughItIsAuthentic()
    {
        var protector = CreateProtector();
        var protectedState = protector.Protect("12345674", "/invoices");

        // Move past the window rather than waiting it out.
        protector.Clock = () => DateTimeOffset.UtcNow.Add(OAuthStateProtector.Lifetime).AddMinutes(1);

        Assert.Null(protector.Unprotect(protectedState));
    }

    [Fact]
    public void EachStateIsUniqueSoOneCannotBeReplayed()
    {
        var protector = CreateProtector();

        var first = protector.Protect("12345674", "/invoices");
        var second = protector.Protect("12345674", "/invoices");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void TheCifIsNormalisedBeforeItIsProtected()
    {
        var protector = CreateProtector();

        var state = protector.Unprotect(protector.Protect("12345674", null));

        Assert.Equal("12345674", state!.Cif);
    }

    private static OAuthStateProtector CreateProtector(string keyRing = "state") =>
        new(DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(Path.GetTempPath(), "romania-efactura-tests", keyRing))));
}
