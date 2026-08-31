using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RomaniaEFactura.Authentication;

/// <summary>
/// The two endpoints that carry a person through ANAF authorization.
/// </summary>
/// <remarks>
/// Shipped with the library rather than left to each application, because the callback is easy to
/// get subtly wrong — validating the state, binding the code to the right company, and not
/// redirecting to an attacker-chosen URL are all required and none is obvious.
/// </remarks>
public static class EFacturaAuthorizationEndpoints
{
    /// <summary>
    /// Maps <c>{prefix}/connect/{cif}</c> and <c>{prefix}/callback</c>.
    /// </summary>
    /// <param name="endpoints">The application's endpoint route builder.</param>
    /// <param name="prefix">
    /// The path to mount under. Must match the redirect URI registered with ANAF, which cannot be
    /// changed without re-registering the application.
    /// </param>
    public static IEndpointRouteBuilder MapEFacturaAuthorization(
        this IEndpointRouteBuilder endpoints,
        string prefix = "/efactura")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(prefix);

        // Sends the user to ANAF. There is no headless alternative: authorization requires a
        // qualified certificate presented by a real browser.
        group.MapGet("/connect/{cif}", (
            string cif,
            string? returnUrl,
            IAnafOAuthClient oauth) =>
            Results.Redirect(oauth.BuildAuthorizationUrl(cif, returnUrl).ToString()));

        group.MapGet("/callback", async (
            HttpContext context,
            string? code,
            string? state,
            string? error,
            IAnafOAuthClient oauth,
            IEFacturaTokenStore store,
            OAuthStateProtector stateProtector,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("RomaniaEFactura.Authorization");

            // The state is validated before anything else is trusted. It is the only thing tying
            // this callback to a request this application actually made, and it carries the
            // company the code will be bound to.
            var validated = stateProtector.Unprotect(state);
            if (validated is null)
            {
                logger.LogWarning("Rejected an ANAF callback whose state was missing, tampered with or expired.");
                return Results.BadRequest("The authorization state was invalid or has expired. Please start again.");
            }

            if (!string.IsNullOrEmpty(error))
            {
                logger.LogWarning("ANAF reported an authorization error for CIF {Cif}: {Error}", validated.Cif, error);
                return RedirectSafely(context, validated.ReturnUrl, $"efactura_error={Uri.EscapeDataString(error)}");
            }

            if (string.IsNullOrEmpty(code))
            {
                return Results.BadRequest("ANAF did not return an authorization code.");
            }

            var token = await oauth.ExchangeCodeAsync(code, validated.Cif, cancellationToken).ConfigureAwait(false);
            if (!token.IsSuccess)
            {
                logger.LogError("Exchanging the authorization code failed for CIF {Cif}: {Error}",
                    validated.Cif, token.Error);
                return RedirectSafely(context, validated.ReturnUrl, "efactura_error=token_exchange_failed");
            }

            await store.SaveAsync(token.Value, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Stored an ANAF authorization for CIF {Cif}.", validated.Cif);

            return RedirectSafely(context, validated.ReturnUrl, "efactura_connected=1");
        });

        return endpoints;
    }

    /// <summary>
    /// Redirects only within this application.
    /// </summary>
    /// <remarks>
    /// The return URL arrives inside the protected state, so it cannot be chosen by an attacker —
    /// but it is still checked here, so that a bug elsewhere cannot turn this endpoint into an
    /// open redirect.
    /// </remarks>
    private static IResult RedirectSafely(HttpContext context, string? returnUrl, string query)
    {
        var target = IsLocalUrl(returnUrl) ? returnUrl! : "/";
        var separator = target.Contains('?', StringComparison.Ordinal) ? "&" : "?";

        return Results.Redirect($"{target}{separator}{query}");
    }

    private static bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url)
        && url[0] == '/'
        // "//host" and "/\host" are protocol-relative and would leave the application.
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
