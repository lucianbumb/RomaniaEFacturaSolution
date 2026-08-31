using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura.Authentication;

/// <summary>Talks to ANAF's OAuth 2.0 endpoints.</summary>
public interface IAnafOAuthClient
{
    /// <summary>
    /// Builds the URL a person must visit to authorize a company.
    /// </summary>
    /// <remarks>
    /// This step cannot be automated: ANAF requires a qualified digital certificate, normally on a
    /// physical token, presented by a real browser. Any design that assumes authorization can be
    /// obtained headlessly is wrong.
    /// </remarks>
    /// <param name="cif">The company to authorize.</param>
    /// <param name="returnUrl">Where to send the person once the callback completes.</param>
    /// <param name="user">
    /// Who is starting the round trip, carried in the protected state so the callback can refuse
    /// one completed by somebody else.
    /// </param>
    Uri BuildAuthorizationUrl(string cif, string? returnUrl = null, string? user = null);

    /// <summary>Exchanges the callback's authorization code for tokens.</summary>
    Task<AnafResult<EFacturaToken>> ExchangeCodeAsync(
        string code,
        string cif,
        CancellationToken cancellationToken = default);

    /// <summary>Obtains a fresh access token using a refresh token.</summary>
    Task<AnafResult<EFacturaToken>> RefreshAsync(
        EFacturaToken token,
        CancellationToken cancellationToken = default);
}

/// <summary>The default <see cref="IAnafOAuthClient"/>.</summary>
public sealed class AnafOAuthClient(
    IHttpClientFactory httpClientFactory,
    OAuthStateProtector stateProtector,
    IOptions<EFacturaOptions> options,
    ILogger<AnafOAuthClient> logger) : IAnafOAuthClient
{
    private readonly EFacturaOptions _options = options.Value;

    /// <summary>The clock, overridable so tests can age a token.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public Uri BuildAuthorizationUrl(string cif, string? returnUrl = null, string? user = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cif);

        // Checked here because the callback stores a token under whatever this says. A value that
        // is not a company would become a row nothing can ever match a call against.
        var normalized = Normalize(cif);
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri,
            ["scope"] = "efactura",
            // The state is protected, so a forged callback cannot choose the company or the
            // post-callback redirect.
            ["state"] = stateProtector.Protect(normalized, returnUrl, user),
            // ANAF requires this, and returns an opaque token without it.
            ["token_content_type"] = "jwt",
        };

        var queryString = string.Join("&", query.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        return new Uri($"{_options.ResolvedOAuthBaseAddress.AbsoluteUri.TrimEnd('/')}/authorize?{queryString}");
    }

    /// <inheritdoc />
    public Task<AnafResult<EFacturaToken>> ExchangeCodeAsync(
        string code,
        string cif,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(cif);

        return RequestTokenAsync(
            Normalize(cif),
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = _options.RedirectUri,
                ["token_content_type"] = "jwt",
            },
            existingRefreshToken: null,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<AnafResult<EFacturaToken>> RefreshAsync(
        EFacturaToken token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        if (!token.CanRefresh)
        {
            return Task.FromResult(AnafResult<EFacturaToken>.Failure(new AnafError(
                AnafErrorKind.NotAuthorized,
                $"No refresh token is stored for CIF {token.Cif}; someone must authorize again with a certificate.")));
        }

        return RequestTokenAsync(
            token.Cif,
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = token.RefreshToken,
                ["token_content_type"] = "jwt",
            },
            // ANAF does not always return a new refresh token; keep the existing one if it does not.
            existingRefreshToken: token.RefreshToken,
            cancellationToken);
    }

    /// <summary>
    /// Strips the country prefix ANAF's API rejects, and refuses anything that is not a company.
    /// </summary>
    private static string Normalize(string cif)
    {
        var normalized = Validation.RomanianCif.Normalize(cif);

        return Validation.RomanianCif.IsValid(normalized)
            ? normalized
            : throw new ArgumentException(
                $"'{cif}' is not a valid Romanian fiscal code - the control digit does not match.",
                nameof(cif));
    }

    private async Task<AnafResult<EFacturaToken>> RequestTokenAsync(
        string cif,
        Dictionary<string, string> form,
        string? existingRefreshToken,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"{_options.ResolvedOAuthBaseAddress.AbsoluteUri.TrimEnd('/')}/token"))
        {
            Content = new FormUrlEncodedContent(form),
        };

        // ANAF expects the client credentials as HTTP Basic, not in the body.
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}")));

        RawAnafResponse raw;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.Timeout);

            var client = httpClientFactory.CreateClient(AnafApiClient.HttpClientName);
            using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
            raw = await AnafEnvelope.BufferAsync(response, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException
                                   && !cancellationToken.IsCancellationRequested)
        {
            return AnafResult<EFacturaToken>.Failure(new AnafError(
                AnafErrorKind.ServiceUnavailable, $"ANAF's token endpoint is unreachable: {ex.Message}"));
        }
        finally
        {
            request.Dispose();
        }

        if (AnafEnvelope.DetectError(raw) is { } error)
        {
            logger.LogWarning("ANAF refused the token request for CIF {Cif}: {Error}", cif, error);
            return AnafResult<EFacturaToken>.Failure(error);
        }

        try
        {
            using var document = JsonDocument.Parse(raw.Text);
            var root = document.RootElement;

            var accessToken = root.TryGetProperty("access_token", out var access) ? access.GetString() : null;
            if (string.IsNullOrEmpty(accessToken))
            {
                return AnafResult<EFacturaToken>.Failure(new AnafError(
                    AnafErrorKind.Unreadable, "ANAF's token response carried no access_token."));
            }

            var refreshToken = root.TryGetProperty("refresh_token", out var refresh)
                ? refresh.GetString()
                : null;

            // Losing the refresh token here would be as costly as never having had one, so a
            // response that omits it keeps whatever was already stored.
            refreshToken = string.IsNullOrEmpty(refreshToken) ? existingRefreshToken : refreshToken;

            if (string.IsNullOrEmpty(refreshToken))
            {
                return AnafResult<EFacturaToken>.Failure(new AnafError(
                    AnafErrorKind.Unreadable, "ANAF's token response carried no refresh_token."));
            }

            var expiresIn = root.TryGetProperty("expires_in", out var expires)
                            && expires.ValueKind == JsonValueKind.Number
                ? expires.GetInt64()
                : 3600;

            var now = Clock();
            return AnafResult<EFacturaToken>.Success(new EFacturaToken
            {
                Cif = cif,
                AccessToken = accessToken,
                AccessTokenExpiresAt = now.AddSeconds(expiresIn),
                RefreshToken = refreshToken,
                ObtainedAt = now,
                UpdatedAt = now,
            });
        }
        catch (JsonException ex)
        {
            return AnafResult<EFacturaToken>.Failure(new AnafError(
                AnafErrorKind.Unreadable, $"ANAF's token response could not be parsed: {ex.Message}"));
        }
    }
}
