using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RomaniaEFactura.Authentication;
using RomaniaEFactura.Persistence;

namespace RomaniaEFactura.Tests.Authentication;

/// <summary>
/// Schema creation on a database the library does not own.
/// </summary>
/// <remarks>
/// The library ships no migrations, because a migration is provider-specific and committing one
/// set would mislead every consumer on a different provider. These tests cover the helper that
/// replaces them.
/// </remarks>
public class SchemaInitializerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public SchemaInitializerTests() => _connection.Open();

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task TheSchemaIsCreatedOnAFreshDatabase()
    {
        var services = BuildServices();

        await services.EnsureEFacturaSchemaAsync();

        // Proven by using it, not by inspecting metadata.
        await using var scope = services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IEFacturaTokenStore>();
        await store.SaveAsync(new EFacturaToken
        {
            Cif = "12345674",
            AccessToken = "a",
            RefreshToken = "r",
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(90),
        });

        Assert.NotNull(await store.GetAsync("12345674"));
    }

    [Fact]
    public async Task CallingItAgainIsHarmless()
    {
        // It runs on every application start, so it has to be idempotent.
        var services = BuildServices();

        await services.EnsureEFacturaSchemaAsync();
        await services.EnsureEFacturaSchemaAsync();

        await using var scope = services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider
            .GetRequiredService<IEFacturaTokenStore>()
            .ListAuthorizedCifsAsync());
    }

    [Fact]
    public async Task ANonRelationalProviderIsLeftAlone()
    {
        // Nothing to create, and no exception either.
        var services = new ServiceCollection()
            .AddDataProtection().Services
            .AddDbContext<EFacturaDbContext>(o => o.UseInMemoryDatabase("schema-test"))
            .BuildServiceProvider();

        await services.EnsureEFacturaSchemaAsync();
    }

    private ServiceProvider BuildServices() =>
        new ServiceCollection()
            .AddDataProtection().Services
            .AddDbContext<EFacturaDbContext>(o => o.UseSqlite(_connection))
            .AddScoped<IEFacturaTokenStore, EfCoreTokenStore>()
            .BuildServiceProvider();
}
