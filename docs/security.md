# Security notes

What this library protects, what it leaves to the host application, and the reasoning behind the
choices that are not obvious.

## Connecting a company to ANAF

`MapEFacturaAuthorization` mounts two endpoints:

| Endpoint | What it does |
|---|---|
| `GET {prefix}/connect/{cif}` | Redirects to ANAF to start an authorization |
| `GET {prefix}/callback` | Exchanges the code and **writes the authorization into the token store** |

### They require an authenticated user, by default

The callback is a state-changing endpoint on the application's most valuable stored credential.
`EfCoreTokenStore.SaveAsync` overwrites the row for a CIF unconditionally, and an ANAF
authorization is expensive to replace — a person, a qualified digital certificate, usually a
physical token.

Left open, anyone on the internet holding **their own** valid ANAF certificate can walk the
ordinary flow against your deployment and replace the stored authorization for any company it
serves. The application then makes every ANAF call under an identity that has no SPV rights for
that company: uploads fail, the reconciler cannot settle submissions, the inbox stops syncing, and
the library reports `NoRights` — which reads like a configuration problem rather than an intrusion.
Undoing it needs the real certificate holder. In Romania, invoicing through e-Factura carries legal
deadlines, so a sustained denial here is not merely inconvenient.

The protected `state` parameter does not cover this. It stops a callback being **forged**, which is
a different attack from walking the real flow against an endpoint that asks nothing of the caller.

So the default is to require an authenticated user, and mapping the endpoints in an application
with no authorization services **fails at startup** rather than on the first click:

```csharp
builder.Services.AddAuthentication(/* your scheme */);
builder.Services.AddAuthorization();
...
app.UseAuthentication();
app.UseAuthorization();

app.MapEFacturaAuthorization();
```

### Narrow it further

Connecting a company is an administrative act, and "any authenticated user" is rarely the right
audience. Name a policy:

```csharp
app.MapEFacturaAuthorization(options => options.Policy = "efactura-administrators");
```

The method returns the `RouteGroupBuilder`, so anything else the application wants — rate limiting,
an extra endpoint filter, a CORS policy — applies as usual:

```csharp
app.MapEFacturaAuthorization().RequireRateLimiting("connect");
```

### The escape hatch

An application with genuinely no user accounts can mount them open:

```csharp
app.MapEFacturaAuthorization(options => options.AllowAnonymousAccess = true);
```

It is a named setting rather than the default so that turning it on is a decision somebody made,
and shows up in a review as one. Read the paragraphs above before using it.

### The round trip is bound to the person who started it

The protected state records who began the flow, and the callback refuses one that comes back as
somebody else. Without that, any user who can sign in could start an authorization, capture the
state, and hand an administrator a link that quietly completes the **attacker's** ANAF
authorization under one of the application's companies. Requiring authentication alone does not
stop that; the two defences are for different attackers.

An application that builds the URL itself through `IRomaniaEFacturaService.BuildAuthorizationUrl`
should pass the `user` argument for the same reason. A state that records nobody binds nothing —
it is still accepted, because refusing it would break a legitimate caller, and the authorization
requirement is what protects that case.

## Reading a downloaded archive

`EFacturaArchiveReader.Read` stops once an archive has expanded past a budget — by default 64 MB
across every entry and every level of nesting, and at most 256 entries. Both are far above anything
real: ANAF caps an upload at 10 MB, and an archive holds one document, its signature and sometimes
a PDF.

The check happens **during** the copy rather than before it. A ZIP records its own uncompressed
sizes, and whoever built the archive wrote them, so consulting `ZipArchiveEntry.Length` first would
trust the thing being defended against — while reading the entry whole in order to measure it is
the allocation the limit exists to prevent. DEFLATE reaches roughly a thousand to one, so a 42 KB
file expands to more memory than the process has.

The archive normally arrives from ANAF over TLS, so this is hardening rather than a reachable hole.
It is worth having because `EFacturaArchiveReader` is public API, and an application that lets
somebody upload an archive for inspection is one feature away.

```csharp
// Only if you know better than the default.
var document = EFacturaArchiveReader.Read(bytes, new ArchiveLimits { MaxTotalUncompressedBytes = 8L * 1024 * 1024 });
```

## Configuration is checked at startup

`AddRomaniaEFactura` validates its options with `ValidateOnStart`, so a mistake stops the
application rather than surfacing as an ANAF error later. The rule that earns its keep:

> **A base address override must be https, unless its host is loopback.**

`ApiBaseAddress` and `OAuthBaseAddress` exist so the library can be pointed at the mock server.
The same knob will just as happily point a production deployment at a plaintext address, and every
call carries the bearer token — the OAuth address carries the client secret as HTTP Basic as well.
Loopback is the seam rather than a blanket https requirement, because pointing the library at the
mock is supported and necessary, and a loopback address has no transit to protect. `RedirectUri` is
held to the same rule: ANAF returns the authorization code to it.

The rest are ordinary but worth naming, because each otherwise arrives wearing somebody else's
clothes. An empty `ClientSecret` produces an `Authorization: Basic` header built from
`client-id:`, and ANAF answers **401** — indistinguishable from an expired authorization, which
sends whoever is debugging it to the certificate holder rather than to `appsettings.json`. A
`ReconcileInterval` of zero kills the reconciler on its first tick, inside a background service
where nothing reports that it stopped.

`Cif` is deliberately **optional**: a deployment serving several companies passes it per call,
which the interface supports throughout. A value that is present must have a correct control digit.

All failures are reported together, so a misconfigured deployment costs one restart rather than one
per mistake.

## The CIF is checked before it leaves the machine

`RomanianCif.Normalize` strips whitespace and an `RO` prefix and returns the rest verbatim, so a
malformed fiscal code used to travel to ANAF and come back as a sentence in Romanian — spending a
call from the daily allowance to say what the control digit already said. It is now checked at
three boundaries: the transport, the OAuth client, and the `connect/{cif}` endpoint, which answers
**400** rather than faulting.

The authorization path is the one that matters. `ExchangeCodeAsync` stores a token under whatever
the caller named, so an unchecked value became a row no call could ever match against.

### What this is not

The per-company pacing state (`CompanyGates`, `LastCallPerCompany`, `RefreshGates`) is held in
process-wide dictionaries that never evict, keyed by CIF. That looks like unbounded growth driven
by caller-controlled input, and it is not: every path to a `GetOrAdd` runs **after** a lookup that
returns early when the company has no stored authorization. An entry costs a completed ANAF
authorization — a qualified certificate — so the dictionaries are bounded by the companies the
deployment actually serves.

That ordering is load-bearing and easy to reverse by accident, so it is measured rather than
argued: `CompanyGateTests.AnUnauthorizedCompanyLeavesNoTrace` drives fifty unauthorized companies
and asserts the dictionary is unchanged.

## Serving several companies from one deployment

The per-call `cif` override exists so one deployment can serve several companies. They share one
database, so **every lookup by identifier is scoped by company**:

```csharp
await efactura.GetArchiveAsync(downloadId, cif: theTenantsCif);
await efactura.GetDocumentAsync(downloadId, cif: theTenantsCif);
await efactura.RenderPdfAsync(downloadId, cif: theTenantsCif);
await efactura.GetSubmissionAsync(uploadIndex, cif: theTenantsCif);
```

Omit the CIF and the configured one is used, which is what a single-tenant deployment wants and
leaves it unaffected.

The scoping has to be local, and this is the part worth understanding. ANAF enforces rights on a
download, so asking it for another company's document is refused — but the library **caches
archives on purpose**, because downloads are capped at roughly ten per identifier per day, and a
cached archive never reaches ANAF. Whatever check exists happens in the local query or it does not
happen at all. ANAF's download identifiers are short numeric strings, so enumerating them is no
obstacle.

If your application has no configured CIF and passes one on every call, these methods will throw
rather than guess. That is deliberate: an application serving several companies has to say whose
document it is asking for.

## What the library already does

- **Tokens are encrypted at rest** with `IDataProtector`, under a versioned purpose string, and a
  row that cannot be decrypted is treated as no authorization rather than as an error.
- **The refresh token never expires as a side effect** of the access token ageing, and a transient
  refusal from ANAF does not discard it — only an outright rejection does.
- **The OAuth state is encrypted and signed**, carries a nonce, and expires after 15 minutes.
- **Redirects after the callback are local-only**, checked at the redirect rather than trusted
  because they arrived inside the protected state.
- **Downloaded XML is parsed with DTD processing prohibited and no resolver**, so a document from
  ANAF cannot reference external entities.
- **The client secret travels as HTTP Basic to ANAF's token endpoint only**, and `AnafError`
  overrides `ToString()` to print just the kind and the message, keeping the raw response body it
  carries for diagnostics out of every log line the library writes. A logging sink configured to
  destructure objects rather than format them can still reach that property; it holds ANAF error
  bodies, which is why it exists, so treat it as you would any other diagnostic payload.

## What the host application owns

- **Where data protection keys live.** The library calls `AddDataProtection()` with the host's
  defaults. A multi-instance deployment must give them shared, persisted storage, or each instance
  will be unable to read the others' stored tokens.
- **Who may reach the pages that send invoices.** The library protects its own two endpoints; every
  other call goes through `IRomaniaEFacturaService`, and authorizing those is the application's.
- **The database.** Tokens are encrypted in it; invoice content is not, because the application
  needs to read it.
