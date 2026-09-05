# RomaniaEFacturaLibrary

Romanian e-Factura (ANAF SPV) integration for .NET web applications — build invoices, validate them
against CIUS-RO before anything leaves your machine, send them, and read what arrives.

> **Status: v3.0.0 in development.** The library is being rebuilt from scratch; v2.x on NuGet is
> not functional against the live ANAF API and should not be used. Nothing is published yet: the
> run against ANAF's own test environment ([#10](https://github.com/lucianbumb/RomaniaEFacturaSolution/issues/10))
> is the gate, because it is the only thing that proves the mock server is faithful.

## The guarantee

**Fill in an `InvoiceEditModel`, let `Verify()` accept it, and ANAF will not reject the document
for format reasons.**

Validation runs fully offline — a C# port of the CIUS-RO rules, checked in CI against ANAF's own
`ROeFacturaValidator.jar` over a corpus of valid and deliberately invalid documents. Everything that
genuinely can fail at runtime — nobody has authorized this company yet, the token expired, ANAF is
down, the daily allowance is spent, no SPV rights — comes back as a typed result you branch on, not
an exception you discover in production.

Arithmetic rules are **unrepresentable rather than merely detected**. You enter quantities and
prices; the model derives line net amounts, the seven document totals and the VAT breakdown, so
`BR-CO-10` through `BR-CO-16` cannot be failed.

## Installing

Prerelease builds are published to **GitHub Packages** from a tag. nuget.org waits for the live
ANAF run ([#10](https://github.com/lucianbumb/RomaniaEFacturaSolution/issues/10)), because that is
what proves the mock server is faithful.

```powershell
dotnet add package RomaniaEFacturaLibrary --version 3.0.0-alpha.1
```

GitHub Packages needs a classic PAT with `read:packages` to restore, even for a public package —
[docs/publishing.md](docs/publishing.md) has the `NuGet.config`, including the package source
mapping that stops a hiccup there from failing an unrelated nuget.org restore.

## Getting started

```csharp
// Program.cs
builder.AddRomaniaEFactura(
    options =>
    {
        options.ClientId     = builder.Configuration["EFactura:ClientId"];
        options.ClientSecret = builder.Configuration["EFactura:ClientSecret"];
        options.RedirectUri  = "https://app.example.ro/efactura/callback";
        options.Cif          = "12345674";           // omit when each request names its own
    },
    db => db.UseNpgsql(builder.Configuration.GetConnectionString("EFactura")));

app.UseAuthentication();
app.UseAuthorization();
app.MapEFacturaAuthorization();                       // /efactura/connect/{cif} and /callback

await app.Services.EnsureEFacturaSchemaAsync();       // or bring your own migrations
```

```csharp
// anywhere you need it
public class InvoicePage(IRomaniaEFacturaService efactura)
{
    public async Task Send(InvoiceEditModel model)
    {
        var report = efactura.Verify(model);          // offline, deterministic
        if (!report.IsValid) { /* BR-coded errors, ready to render */ return; }

        var result = await efactura.SendInvoiceAsync(model);
    }
}
```

Configuration is checked at **startup**, not on the first ANAF call — a missing client secret
otherwise arrives as a 401 that reads exactly like an expired authorization.

## What it does

### Building an invoice

`InvoiceEditModel`, `CreditNoteEditModel` and `BuyerMessageEditModel` are flat and
app-developer-shaped, not UBL-shaped. They carry their own DataAnnotations, so ASP.NET Core model
binding picks them up with no extra wiring.

In Blazor use `<EFacturaValidator />` in place of `<DataAnnotationsValidator />` — the built-in one
validates the model object and stops, which would leave every invoice line unchecked.

The model derives what EN16931 makes you state and then checks: line net amounts, the seven
document totals, the VAT breakdown grouped by category *and* rate. See
[docs/edit-models.md](docs/edit-models.md).

### Validating it

Two stages. DataAnnotations for field rules, then the CIUS-RO engine over the mapped UBL — including
the Romanian rules that surprise people: the Bucharest sector rule (`BR-RO-100`), the control digit
on a fiscal code, and roughly thirty-five length limits taken from ANAF's Schematron rather than
invented.

`Verify(UblInvoice)` runs the same engine for callers who build UBL directly.

**What it cannot check is a field the library does not model.** Two remain — document attachments
(BT-124, BT-125) and card payment (BT-88) — and they are
[enumerated and tested](docs/cius-ro-coverage.md) rather than left implicit.

### Sending and settling

| | |
|---|---|
| `SendInvoiceAsync` | From an edit model or from UBL |
| `SendCreditNoteAsync` | Same, as `FCN` |
| `SendBuyerMessageAsync` | A buyer disputing an invoice inside e-Factura (RASP) |
| `SendRawXmlAsync` | An escape hatch, carrying no guarantee |
| `GetSubmissionAsync` | Where a submission got to, from local records — free to call |

Submission is not one round trip: ANAF accepts an upload in seconds but takes minutes to hours to
decide. A background reconciler settles each submission on **its own widening schedule** and stores
the signed archive, because the ministry's signature is the proof of submission.

### Reading the inbox

| | |
|---|---|
| `SyncInboxAsync` | Reads the SPV inbox from a stored watermark |
| `GetInboxAsync` | Messages known for a company |
| `GetDocumentAsync` | Parsed and **discriminated**: invoice, credit note, debit note, error report, buyer message |
| `GetArchiveAsync` | The raw ZIP, for archival |
| `RenderPdfAsync` | Through ANAF's converter, or the PDF already inside the archive |

The archive behind a message is frequently not an invoice, which is why the result is discriminated
rather than assumed — the previous version deserialized everything as an invoice and failed with a
parser error on anything else.

Turn on `EnableInboxSync` and a background sweep reads every authorized company's inbox, scheduled
per company. It lists and records; it does not download, because `descarcare` is capped at roughly
ten calls per identifier per day.

### Looking a company up before invoicing it

ANAF's taxpayer register is a separate, unauthenticated service, so this works before anybody has
connected a company:

```csharp
var result = await efactura.LookupCompanyAsync("RO12345674");
if (result.IsSuccess && result.Value is { } company) invoice.Buyer = company.ToPartyEditModel();
```

It fills in the name, address and county code, and answers three questions that decide how a
document is built rather than merely describing the company:

| Property | What it decides |
|---|---|
| `IsRegisteredForEFactura` | B2B, or `uploadb2c`. Sending it the wrong way is refused, not delivered |
| `IsVatRegistered` | Whether a VAT identifier belongs on the document at all |
| `IsInactive` | On the register of inactive taxpayers |

`LookupCompaniesAsync` takes up to a hundred per request and batches beyond that, honouring ANAF's
one-request-per-second limit. See [docs/company-lookup.md](docs/company-lookup.md).

### Serving several companies from one deployment

Every call takes a `cif` override and all storage is keyed by company. For a platform where each
registered business connects its own authorization:

```csharp
builder.AddRomaniaEFactura(
    options =>
    {
        // No single company to name here; each request brings its own.
        options.AllowedReturnOrigins = ["https://app.example.ro"];   // if your UI is elsewhere
    },
    db => db.UseNpgsql(connectionString));

// The CIF comes from whatever identifies the business on this request
builder.Services.AddScoped<IEFacturaCompanyProvider, BusinessProfileCompanyProvider>();

// Only somebody entitled to a company may connect it
builder.Services.AddScoped<IEFacturaConnectAuthorizer, MembershipConnectAuthorizer>();
```

Resolution is the argument, then the scope's provider, then configuration, then a failure naming
all three. See [docs/multi-tenancy.md](docs/multi-tenancy.md).

### Connecting a company

Somebody has to authorize the company at ANAF once, in a browser, with a qualified digital
certificate — there is no headless path. The library ships the two endpoints that carry them
through it.

**They require an authenticated user**, and mapping them in an application that has registered no
authorization services fails at startup. The callback writes an ANAF authorization into the token
store, and one left open to anonymous callers can be replaced by anyone holding their own
certificate.

## Two things about ANAF that shape everything

**Failure arrives inside HTTP 200**, on every endpoint. Upload answers `ExecutionStatus="1"`;
`stareMesaj` an `Errors` element; the list endpoints an `eroare` field; and `descarcare` a JSON
error body where a ZIP was expected. Branching on `IsSuccessStatusCode` reads every one of them as
success — which is what v2 did. One shared envelope reader decides, by content, for the whole
library.

**Daily allowances are per identifier** — roughly twenty `stareMesaj` calls per document per day and
ten `descarcare`. A fixed poll interval burns the budget within an hour and then goes blind, so
every schedule in the library widens.

[docs/anaf-wire-formats.md](docs/anaf-wire-formats.md) is the source of truth for both.

## Security

Summarised here; the reasoning is in [docs/security.md](docs/security.md).

- Both ANAF tokens **encrypted at rest** with `IDataProtector`; the refresh token never expires as a
  side effect of the access token ageing.
- The OAuth `state` is **signed and encrypted**, carries a nonce, expires after 15 minutes, and is
  **bound to the person who started it**.
- The connect and callback endpoints **require an authenticated user**, and who may connect which
  company is your decision through `IEFacturaConnectAuthorizer`.
- Post-callback redirects are **local-only** unless you name an allowed origin, matched on parsed
  scheme, host and port.
- Every base address must be **https** unless its host is loopback, checked at startup.
- Downloaded XML is parsed with **DTD processing prohibited** and no resolver.
- Downloaded archives are **bounded** at 64 MB and 256 entries across nesting.
- Every lookup by identifier is **scoped by company**, locally and remotely.

## Two packages

```
RomaniaEFacturaLibrary.Abstractions   IRomaniaEFacturaService, the edit models and their CIUS-RO
                                      validation, the UBL types, the result types
RomaniaEFacturaLibrary                everything else, and the one you register
```

Install `RomaniaEFacturaLibrary` and you get both. The split matters for a layered application whose
domain or application layer may not depend on HTTP or persistence: that layer references
`Abstractions` and can still hold the invoice rules, while composition references the full package.

A test asserts `Abstractions` declares no ASP.NET Core, EF Core, DI, options or logging dependency —
reading the **project file** as well as the compiled assembly, because a `PackageReference` nobody
has used yet is invisible to the latter while still landing in every consumer's dependency graph.

## Persistence

Four tables, in whatever database you configure. Exercised in CI against **SQLite and PostgreSQL**,
because the two differ exactly where this schema is unusual: SQLite cannot order by
`DateTimeOffset`, and Npgsql refuses a `DateTime` whose kind is not UTC.

No migrations ship — a migration is provider-specific, so committing one set would work for some
consumers and quietly mislead the rest. `EnsureEFacturaSchemaAsync` creates the tables, or applies
your own migrations if you generated them. See [docs/persistence.md](docs/persistence.md).

## How the claims are checked

- **A stateful mock ANAF** reproduces every documented failure example, quota exhaustion, the
  `in prelucrare` loop, nested ZIPs, base64 PDFs and the sixty-day rejection — so the whole
  lifecycle is testable without credentials.
- **ANAF's own validator as a test oracle** proves generated documents are correct. CI fails the
  build if those tests skip, so "checked against ANAF" cannot quietly become a no-op.
- **Every fix is mutated back out** before it is committed. That practice has caught tests passing
  for the wrong reason and a dependency guard that guarded nothing.
- **555 tests**, 0 warnings, plus PostgreSQL tests against a real server in CI.

## Repository layout

| Path | What it is |
|---|---|
| `src/RomaniaEFactura.Abstractions` | Contracts, edit models and validation. No ASP.NET Core, no EF Core |
| `src/RomaniaEFactura` | The implementation: transport, persistence, endpoints, DI |
| `tests/RomaniaEFactura.Tests` | Unit tests, plus the validator-oracle comparison |
| `tests/RomaniaEFactura.IntegrationTests` | Full lifecycle against the mock server |
| `tests/RomaniaEFactura.LiveTests` | The run against ANAF itself. Inert unless deliberately configured |
| `samples/MockAnafServer` | A local stand-in for ANAF, so everything is testable without credentials |
| `samples/SampleWebApp` | Blazor Server app exercising every method — doubles as documentation |
| `docs/edit-models.md` | Filling in an invoice: what the model derives, and the Romanian rules that surprise |
| `docs/cius-ro-coverage.md` | Which CIUS-RO rules are enforced, and which fields are not modelled |
| `docs/company-lookup.md` | Asking ANAF about a company before invoicing it |
| `docs/multi-tenancy.md` | Serving several companies from one deployment |
| `docs/security.md` | What the library protects, what the host application owns, and why |
| `docs/persistence.md` | The library's own tables, providers tested, and migrations |
| `docs/publishing.md` | Cutting a release, and the `NuGet.config` a consumer needs |
| `docs/anaf-wire-formats.md` | How the ANAF API actually behaves. Read this before changing transport code |
| `docs/live-run.md` | Running against ANAF's real test environment — needs credentials and a certificate |
| `documentation_efactura/` | ANAF's own published specifications |

## Trying it without credentials

Two terminals — the first stays running:

```powershell
dotnet run --project samples/MockAnafServer
```

```powershell
dotnet run --project samples/SampleWebApp
```

The sample points at the mock by default and walks the whole lifecycle: sign in, connect a company,
look one up, build and verify an invoice, send it, watch it settle, read the inbox, render a PDF.
It doubles as the documentation — a test fails if any interface method is unreachable from it.

## Contributing

`docs/anaf-wire-formats.md` is the source of truth for anything touching the wire. If ANAF's real
behaviour turns out to differ, fix that document first and let the mock and the client follow —
do not work around a discrepancy at the call site.

## License

MIT. See [LICENSE](LICENSE).
