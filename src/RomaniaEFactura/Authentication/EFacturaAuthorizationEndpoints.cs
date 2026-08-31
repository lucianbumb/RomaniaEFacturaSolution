using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace RomaniaEFactura.Authentication;

/// <summary>
/// How the authorization endpoints are mounted and protected.
/// </summary>
public sealed class EFacturaAuthorizationEndpointOptions
{
    /// <summary>
    /// The path to mount under.
    /// </summary>
    /// <remarks>
    /// Must match the redirect URI registered with ANAF, which cannot be changed without
    /// re-registering the application.
    /// </remarks>
    public string Prefix { get; set; } = "/efactura";

    /// <summary>
    /// The authorization policy a caller must satisfy. Leave it unset to require only that the
    /// request is authenticated.
    /// </summary>
    /// <remarks>
    /// Worth setting. Connecting a company is an administrative act, and every authenticated user
    /// of an application is rarely the right audience for it.
    /// </remarks>
    public string? Policy { get; set; }

    /// <summary>
    /// Mounts the endpoints with no authorization requirement at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only for an application that has no user accounts.</b> The callback writes an ANAF
    /// authorization into the token store, so leaving it open lets anyone holding their own
    /// qualified certificate replace the stored authorization for any company the application
    /// serves — after which every ANAF call is made under an identity with no rights for that
    /// company, and a person with the real certificate has to authorize again to undo it.
    /// </para>
    /// <para>
    /// It is a separate setting rather than the default precisely so that turning it on is a
    /// decision somebody made, and shows up in a review as one.
    /// </para>
    /// </remarks>
    public bool AllowAnonymousAccess { get; set; }
}

/// <summary>
/// The two endpoints that carry a person through ANAF authorization.
/// </summary>
/// <remarks>
/// Shipped with the library rather than left to each application, because the callback is easy to
/// get subtly wrong — validating the state, binding the code to the right company, refusing a
/// callback that comes back as somebody else, and not redirecting to an attacker-chosen URL are
/// all required and none is obvious.
/// </remarks>
public static class EFacturaAuthorizationEndpoints
{
    /// <summary>
    /// Maps <c>{prefix}/connect/{cif}</c> and <c>{prefix}/callback</c>, requiring an authenticated
    /// user unless that is explicitly turned off.
    /// </summary>
    /// <param name="endpoints">The application's endpoint route builder.</param>
    /// <param name="configure">Adjusts the path and how the endpoints are protected.</param>
    /// <returns>
    /// The route group, so further conventions — rate limiting, a CORS policy, an additional
    /// filter — can be applied by the consumer.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The application registered no authorization services and did not ask for anonymous access.
    /// </exception>
    public static RouteGroupBuilder MapEFacturaAuthorization(
        this IEndpointRouteBuilder endpoints,
        Action<EFacturaAuthorizationEndpointOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var settings = new EFacturaAuthorizationEndpointOptions();
        configure?.Invoke(settings);

        var group = endpoints.MapGroup(settings.Prefix);

        if (settings.AllowAnonymousAccess)
        {
            group.AllowAnonymous();
        }
        else
        {
            RequireAuthenticatedUser(endpoints, group, settings.Policy);
        }

        // Sends the user to ANAF. There is no headless alternative: authorization requires a
        // qualified certificate presented by a real browser.
        group.MapGet("/connect/{cif}", (
            HttpContext context,
            string cif,
            string? returnUrl,
            IAnafOAuthClient oauth) =>
            Results.Redirect(oauth.BuildAuthorizationUrl(cif, returnUrl, UserKey(context)).ToString()));

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

            // A state issued to one person must not be completed by another. Without this, any
            // authenticated user could hand an administrator a link that quietly binds the
            // attacker's ANAF identity to a company of the application's.
            if (validated.User is not null
                && !string.Equals(validated.User, UserKey(context), StringComparison.Ordinal))
            {
                logger.LogWarning(
                    "Rejected an ANAF callback for CIF {Cif}: it was started by a different user.", validated.Cif);
                return Results.BadRequest("This authorization was started by a different user. Please start again.");
            }

            if (!string.IsNullOrEmpty(error))
            {
                logger.LogWarning("ANAF reported an authorization error for CIF {Cif}: {Error}", validated.Cif, error);
                return RedirectSafely(validated.ReturnUrl, $"efactura_error={Uri.EscapeDataString(error)}");
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
                return RedirectSafely(validated.ReturnUrl, "efactura_error=token_exchange_failed");
            }

            await store.SaveAsync(token.Value, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Stored an ANAF authorization for CIF {Cif}.", validated.Cif);

            return RedirectSafely(validated.ReturnUrl, "efactura_connected=1");
        });

        return group;
    }

    /// <summary>
    /// Requires an authenticated user, failing at startup rather than on the first request when
    /// the application has no authorization services at all.
    /// </summary>
    /// <remarks>
    /// Without this check the same mistake surfaces as an <c>InvalidOperationException</c> from
    /// the routing middleware the first time somebody clicks "connect" — which in practice is
    /// after deployment, and reads as a library fault rather than a missing
    /// <c>AddAuthorization</c>.
    /// </remarks>
    private static void RequireAuthenticatedUser(
        IEndpointRouteBuilder endpoints,
        RouteGroupBuilder group,
        string? policy)
    {
        if (endpoints.ServiceProvider.GetService<IAuthorizationService>() is null)
        {
            throw new InvalidOperationException(
                "MapEFacturaAuthorization requires an authenticated user, but this application has "
                + "registered no authorization services. Call AddAuthentication and AddAuthorization, "
                + "with UseAuthentication and UseAuthorization in the pipeline — or, only if the "
                + "application genuinely has no user accounts, set AllowAnonymousAccess, having read "
                + "what it gives up.");
        }

        if (policy is null)
        {
            group.RequireAuthorization();
        }
        else
        {
            group.RequireAuthorization(policy);
        }
    }

    /// <summary>
    /// Identifies the person driving the flow, stably enough to compare across the round trip to
    /// ANAF. Null when nobody is signed in.
    /// </summary>
    private static string? UserKey(HttpContext context)
    {
        var user = context.User;
        if (user?.Identity?.IsAuthenticated != true) return null;

        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? user.FindFirst("sub")?.Value
               ?? user.Identity.Name;
    }

    /// <summary>
    /// Redirects only within this application.
    /// </summary>
    /// <remarks>
    /// The return URL arrives inside the protected state, so it cannot be chosen by an attacker —
    /// but it is still checked here, so that a bug elsewhere cannot turn this endpoint into an
    /// open redirect.
    /// </remarks>
    private static IResult RedirectSafely(string? returnUrl, string query)
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
