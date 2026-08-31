namespace RomaniaEFactura.Authentication;

/// <summary>
/// An ANAF authorization held for one company.
/// </summary>
/// <remarks>
/// <para>
/// The access token and the refresh token have very different lifetimes — roughly 90 days and 365
/// days — and, critically, <b>very different costs when lost</b>. An expired access token is
/// replaced silently by a refresh; a lost refresh token requires a human to log in again with a
/// qualified certificate on a physical token, which cannot be automated and may take days to
/// arrange.
/// </para>
/// <para>
/// The previous version stored both in a single cache entry with a thirty-minute sliding
/// expiration, so half an hour of inactivity destroyed the refresh token and forced exactly that.
/// Nothing here may expire, evict or overwrite a refresh token as a side effect of the access
/// token ageing.
/// </para>
/// </remarks>
public sealed class EFacturaToken
{
    /// <summary>The company this authorization belongs to, normalised without the RO prefix.</summary>
    public required string Cif { get; init; }

    /// <summary>The bearer token sent with API calls.</summary>
    public required string AccessToken { get; set; }

    /// <summary>When the access token stops being accepted.</summary>
    public required DateTimeOffset AccessTokenExpiresAt { get; set; }

    /// <summary>
    /// The token used to obtain a new access token without human involvement. Long-lived, and
    /// expensive to replace.
    /// </summary>
    public required string RefreshToken { get; set; }

    /// <summary>When this authorization was first granted.</summary>
    public DateTimeOffset ObtainedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When the tokens were last refreshed.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// A margin applied before the recorded expiry, so a token is never sent in the moments when
    /// it might expire mid-flight.
    /// </summary>
    public static TimeSpan ExpiryMargin => TimeSpan.FromMinutes(5);

    /// <summary>Whether the access token can still be used.</summary>
    public bool IsAccessTokenUsable(DateTimeOffset now) => now < AccessTokenExpiresAt - ExpiryMargin;

    /// <summary>
    /// Whether a refresh is possible. Independent of <see cref="IsAccessTokenUsable"/> — an
    /// authorization with a stale access token is still perfectly serviceable.
    /// </summary>
    public bool CanRefresh => !string.IsNullOrEmpty(RefreshToken);
}
