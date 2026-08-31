using System.Collections.Concurrent;
using RomaniaEFactura.Authentication;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Persistence;

/// <summary>
/// A token store held in process memory.
/// </summary>
/// <remarks>
/// For tests and local development only, and never the default. An ANAF authorization costs a
/// person, a qualified certificate and usually a physical token to obtain, so losing it on every
/// restart is not an acceptable production behaviour — that is precisely the trap the previous
/// version fell into. Use <see cref="EfCoreTokenStore"/> for anything real.
/// </remarks>
public sealed class InMemoryTokenStore : IEFacturaTokenStore
{
    private readonly ConcurrentDictionary<string, EFacturaToken> _tokens = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<EFacturaToken?> GetAsync(string cif, CancellationToken cancellationToken = default) =>
        Task.FromResult(_tokens.TryGetValue(RomanianCif.Normalize(cif), out var token) ? token : null);

    /// <inheritdoc />
    public Task SaveAsync(EFacturaToken token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        _tokens[RomanianCif.Normalize(token.Cif)] = token;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveAsync(string cif, CancellationToken cancellationToken = default)
    {
        _tokens.TryRemove(RomanianCif.Normalize(cif), out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListAuthorizedCifsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([.. _tokens.Keys]);
}
