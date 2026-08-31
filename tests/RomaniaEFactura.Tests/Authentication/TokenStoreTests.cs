using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using RomaniaEFactura.Authentication;
using RomaniaEFactura.Persistence;

namespace RomaniaEFactura.Tests.Authentication;

/// <summary>
/// The durable token store, including the two behaviours the previous version got wrong.
/// </summary>
public class TokenStoreTests : IDisposable
{
    private readonly SqliteConnectionScope _scope = new();

    public void Dispose()
    {
        _scope.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AnAuthorizationSurvivesAProcessRestart()
    {
        // The store is exercised through two independent contexts over one database, which is what
        // a restart looks like from the data's point of view.
        var stored = SampleToken();
        await using (var db = _scope.CreateContext())
        {
            await new EfCoreTokenStore(db, Protection()).SaveAsync(stored);
        }

        await using (var db = _scope.CreateContext())
        {
            var restored = await new EfCoreTokenStore(db, Protection()).GetAsync(stored.Cif);

            Assert.NotNull(restored);
            Assert.Equal(stored.AccessToken, restored!.AccessToken);
            Assert.Equal(stored.RefreshToken, restored.RefreshToken);
        }
    }

    [Fact]
    public async Task AnExpiredAccessTokenDoesNotTakeTheRefreshTokenWithIt()
    {
        // The defect this milestone exists to fix. v2 held both tokens in one cache entry with a
        // thirty-minute sliding expiration, so half an hour of inactivity destroyed a refresh
        // token good for another nine months - and recovering it needs a person with a qualified
        // certificate.
        var expired = SampleToken();
        expired.AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(-30);

        await using var db = _scope.CreateContext();
        var store = new EfCoreTokenStore(db, Protection());
        await store.SaveAsync(expired);

        var restored = await store.GetAsync(expired.Cif);

        Assert.NotNull(restored);
        Assert.False(restored!.IsAccessTokenUsable(DateTimeOffset.UtcNow));
        Assert.True(restored.CanRefresh);
        Assert.Equal(expired.RefreshToken, restored.RefreshToken);
    }

    [Fact]
    public async Task TokensAreNotStoredInPlaintext()
    {
        var token = SampleToken();

        await using var db = _scope.CreateContext();
        await new EfCoreTokenStore(db, Protection()).SaveAsync(token);

        // Read the row directly: a database backup or a stray query log must not hand over the
        // ability to file invoices as the company.
        var row = await db.Tokens.AsNoTracking().SingleAsync();

        Assert.DoesNotContain(token.AccessToken, row.ProtectedAccessToken, StringComparison.Ordinal);
        Assert.DoesNotContain(token.RefreshToken, row.ProtectedRefreshToken, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SavingAgainRefreshesTheTokensButKeepsWhenTheCompanyWasFirstAuthorized()
    {
        var original = SampleToken();
        await using var db = _scope.CreateContext();
        var store = new EfCoreTokenStore(db, Protection());
        await store.SaveAsync(original);

        var refreshed = SampleToken();
        refreshed.AccessToken = "new-access-token";
        refreshed.UpdatedAt = DateTimeOffset.UtcNow.AddDays(90);

        await store.SaveAsync(refreshed);
        var result = await store.GetAsync(original.Cif);

        Assert.Equal("new-access-token", result!.AccessToken);
        Assert.Equal(original.ObtainedAt, result.ObtainedAt);
    }

    [Fact]
    public async Task TheCifIsNormalisedSoThePrefixedAndBareFormsAreOneCompany()
    {
        await using var db = _scope.CreateContext();
        var store = new EfCoreTokenStore(db, Protection());
        await store.SaveAsync(SampleToken());

        Assert.NotNull(await store.GetAsync("RO12345674"));
        Assert.NotNull(await store.GetAsync("12345674"));
    }

    [Fact]
    public async Task RemovingAnAuthorizationMakesTheCompanyUnauthorizedAgain()
    {
        await using var db = _scope.CreateContext();
        var store = new EfCoreTokenStore(db, Protection());
        await store.SaveAsync(SampleToken());

        await store.RemoveAsync("12345674");

        Assert.Null(await store.GetAsync("12345674"));
        Assert.Empty(await store.ListAuthorizedCifsAsync());
    }

    [Fact]
    public async Task AuthorizedCompaniesCanBeListedWithoutARequest()
    {
        // The reconciler runs in the background and has to find its own work.
        await using var db = _scope.CreateContext();
        var store = new EfCoreTokenStore(db, Protection());
        await store.SaveAsync(SampleToken());
        var second = SampleToken();
        await store.SaveAsync(new EFacturaToken
        {
            Cif = "23456783",
            AccessToken = second.AccessToken,
            RefreshToken = second.RefreshToken,
            AccessTokenExpiresAt = second.AccessTokenExpiresAt,
        });

        var cifs = await store.ListAuthorizedCifsAsync();

        Assert.Equal(["12345674", "23456783"], cifs.OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ARowThatCannotBeDecryptedReadsAsNoAuthorizationRatherThanThrowing()
    {
        // Happens when protection keys are rotated away, or a backup is restored onto a host that
        // does not have them. The company simply has to authorize again; it is not a crash.
        await using var db = _scope.CreateContext();
        await new EfCoreTokenStore(db, Protection()).SaveAsync(SampleToken());

        var withDifferentKeys = new EfCoreTokenStore(db, Protection(keyRing: "different"));

        Assert.Null(await withDifferentKeys.GetAsync("12345674"));
    }

    private static EFacturaToken SampleToken() => new()
    {
        Cif = "12345674",
        AccessToken = "access-token-value",
        RefreshToken = "refresh-token-value",
        AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(90),
        ObtainedAt = DateTimeOffset.UtcNow.AddDays(-1),
        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
    };

    private static IDataProtectionProvider Protection(string keyRing = "default") =>
        DataProtectionProvider.Create(
            new DirectoryInfo(Path.Combine(Path.GetTempPath(), "romania-efactura-tests", keyRing)));
}
