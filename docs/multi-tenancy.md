# Serving several companies from one deployment

The library was built for this from the start — every call takes a `cif` override, and the storage
is keyed by company — but the defaults suit a single-company application. This is what changes when
one deployment serves many, and why.

## Where the company comes from

`EFacturaOptions.Cif` names one company in `Program.cs`. An application where each of its own
registered businesses connects its own e-Factura authorization has no such company: the CIF belongs
to whichever business the request concerns.

Implement `IEFacturaCompanyProvider` and register it **scoped**:

```csharp
public sealed class BusinessProfileCompanyProvider(IHttpContextAccessor accessor) : IEFacturaCompanyProvider
{
    public string? GetCurrentCif() =>
        accessor.HttpContext?.User.FindFirst("business_cif")?.Value;
}

builder.Services.AddScoped<IEFacturaCompanyProvider, BusinessProfileCompanyProvider>();
```

Resolution is most specific first:

1. the `cif` argument, when a call passes one
2. `IEFacturaCompanyProvider`, when one is registered and returns a value
3. `EFacturaOptions.Cif`
4. otherwise the call fails, naming all three

Two consequences worth stating. **An explicit CIF always wins**, so a background job settling one
company's submission is unaffected by whatever the ambient scope says. And **the provider comes
before configuration**, so a request about one business never silently falls back to whichever
company happens to be configured — which is the failure that would be hardest to notice, because it
succeeds.

### Why it is synchronous

It is consulted from `BuildAuthorizationUrl` and from the transport's own resolution, neither of
which is async, and making them so would push an `await` into every caller for a value the host has
almost always already resolved. If yours needs I/O, do it once when the scope is created and return
the cached answer.

## Who may connect which company

`GET {prefix}/connect/{cif}` takes the company from the path. Requiring an authenticated user
establishes *that* somebody is signed in; on a platform it says nothing about **which** businesses
they may act for.

That gap is not theoretical. The callback writes an ANAF authorization into the token store, and the
store overwrites the row for a CIF unconditionally — so an ordinary member of one business could
bind their own ANAF identity to another business, and in doing so replace a working authorization
with one that has no rights over it. Undoing that needs the real certificate holder.

Only the host knows the mapping, so it supplies the answer:

```csharp
public sealed class MembershipConnectAuthorizer(IBusinessMembership memberships) : IEFacturaConnectAuthorizer
{
    public async ValueTask<bool> CanConnectAsync(ClaimsPrincipal user, string cif, CancellationToken ct) =>
        await memberships.IsAdministratorOfAsync(user, cif, ct);
}

builder.Services.AddScoped<IEFacturaConnectAuthorizer, MembershipConnectAuthorizer>();
```

It is checked **twice**: before the redirect, so somebody who was never going to be allowed is not
sent to ANAF and asked for a certificate first; and again at the callback, because entitlement can
be withdrawn during the round trip and a state minted while it held would otherwise still write a
token.

### The default

Register nothing and only `EFacturaOptions.Cif` may be connected. That is correct for a deployment
naming one company, and refusing everything else is the safe direction for one that has not thought
about it yet — including an application serving several companies that has not yet registered a real
authorizer. Allowing any authenticated user to connect any company is not offered as a default,
because that is the defect this exists to close.

## Returning to a user interface on another origin

The callback redirects only to local paths by default, which closes an open redirect. If your user
interface is served from a different origin — a separate SPA or PWA — name the origins it may
return to:

```csharp
options.AllowedReturnOrigins = ["https://app.example.ro"];
```

Matching is on the parsed scheme, host and port, so a host that merely *starts with* an allowed
origin is still refused.

## Reading every company's inbox

Nothing reads the SPV inbox on its own unless you ask for it:

```csharp
options.EnableInboxSync = true;
options.InboxSyncInterval = TimeSpan.FromMinutes(15);   // per company
```

**Off by default**, unlike the reconciler. The reconciler only calls ANAF about documents the
application itself submitted; the sweep polls on its own initiative, against an allowance belonging
to each company, so it is something to turn on deliberately rather than something an upgrade should
start doing.

The interval is **per company, not per sweep**. With a hundred companies a shared interval would
mean a hundred calls on every tick, so each carries its own next-due time in
`EFacturaInboxCursor`.

The sweep **lists and records; it does not download**. `descarcare` is capped at roughly ten calls
per identifier per day, and fetching every new message eagerly would spend a company allowance
before anybody asked to read one. Archives are fetched when a document is actually opened.

A company whose authorization has lapsed is **deferred rather than retried every pass** — the
interval doubles with consecutive failures up to a day, and the reason is recorded on the cursor
where somebody diagnosing a quiet inbox would look.

## What is already per-company, and needs nothing

- **Storage.** Tokens, submissions, the inbox and its cursors are all keyed by CIF, and every
  lookup by identifier is scoped by company — see [security.md](security.md).
- **Outbound reconciliation.** The reconciler settles submissions for every company, each with the
  authorization belonging to the company that made it.
- **Pacing and quotas.** ANAF throttles per company, and the transport serializes and paces calls
  per company rather than globally.
