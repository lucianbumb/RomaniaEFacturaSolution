# RomaniaEFacturaLibrary

Romanian e-Factura (ANAF SPV) integration for .NET web applications — send invoices, download
what arrives, and validate against CIUS-RO before anything leaves your machine.

> **Status: v3.0.0 in development.** The library is being rebuilt from scratch; v2.x on NuGet is
> not functional against the live ANAF API and should not be used. See the
> [v3.0.0 milestones](https://github.com/lucianbumb/RomaniaEFacturaSolution/labels/v3.0.0).

## The guarantee

**Fill in an `InvoiceEditModel`, let `Verify()` accept it, and ANAF will not reject the document
for format reasons.**

Validation runs fully offline — a C# port of the CIUS-RO rules, checked in tests against ANAF's
own validator over a corpus of valid and deliberately-invalid documents. Everything that genuinely
can fail at runtime (nobody has authorized this CIF yet, the token expired, ANAF is down, rate
limited, no SPV rights) comes back as a typed result you branch on, not an exception you discover
in production.

`Verify(UblInvoice)` — the path for callers who build UBL directly — runs the same engine and the
same CIUS-RO limits. What it cannot check is a field the library does not model: a tax
representative (BG-11), item attributes (BG-32) and document attachments have no representation, so
nothing validates them. Those exclusions are enumerated and tested rather than left implicit.

## Intended usage

```csharp
// Program.cs
builder.AddRomaniaEFactura(options =>
{
    options.ClientId     = builder.Configuration["EFactura:ClientId"];
    options.ClientSecret = builder.Configuration["EFactura:ClientSecret"];
    options.Cif          = "12345678";
});
```

```csharp
// anywhere you need it
public class InvoicePage(IRomaniaEFacturaService efactura)
{
    public async Task Send(InvoiceEditModel model)
    {
        var report = efactura.Verify(model);        // offline, deterministic
        if (!report.IsValid) { /* BR-coded errors, ready to render */ return; }

        var result = await efactura.SendInvoiceAsync(model);
    }
}
```

`InvoiceEditModel` carries its own validation rules, so ASP.NET Core model binding picks them up
with no extra wiring. In Blazor, use the `<EFacturaValidator />` component in place of
`<DataAnnotationsValidator />` — the built-in one validates the model object and stops, which would
leave every invoice line unchecked. See [docs/edit-models.md](docs/edit-models.md).

It also computes what EN16931 makes you state and then checks: line net amounts, the seven document
totals, and the VAT breakdown. You enter quantities and prices; the arithmetic rules become
impossible to fail rather than merely caught.

### Connecting a company

Somebody has to authorize the company at ANAF once, in a browser, with a qualified digital
certificate — there is no headless path. The library ships the two endpoints that carry them
through it:

```csharp
app.UseAuthentication();
app.UseAuthorization();

// Mounts /efactura/connect/{cif} and /efactura/callback.
app.MapEFacturaAuthorization(options => options.Policy = "efactura-administrators");
```

**They require an authenticated user**, and mapping them in an application that has registered no
authorization services fails at startup. The callback writes an ANAF authorization into the token
store, and an authorization left open to anonymous callers can be replaced by anyone holding their
own certificate. [docs/security.md](docs/security.md) explains what that costs and how to narrow
access further.

### Looking a company up

ANAF's taxpayer register is a separate, unauthenticated service, so this works before anybody has
connected a company:

```csharp
var result = await efactura.LookupCompanyAsync("RO12345674");
if (result.IsSuccess && result.Value is { } company) invoice.Buyer = company.ToPartyEditModel();
```

It fills in the name and address, and answers the three questions that decide how a document is
built: whether the company is in the **RO e-Factura register** (B2B or B2C), whether it is
registered for VAT, and whether it is inactive. See [docs/company-lookup.md](docs/company-lookup.md).

### Serving several companies

One deployment can serve many companies, each connecting its own authorization. Implement
`IEFacturaCompanyProvider` so the CIF comes from whatever identifies the business on the current
request, and `IEFacturaConnectAuthorizer` so only somebody entitled to a company can connect it.
See [docs/multi-tenancy.md](docs/multi-tenancy.md).

## Repository layout

| Path | What it is |
|---|---|
| `src/RomaniaEFactura` | The library — the only thing published to NuGet |
| `tests/RomaniaEFactura.Tests` | Unit tests, plus the validator-oracle comparison |
| `tests/RomaniaEFactura.IntegrationTests` | Full lifecycle against the mock server |
| `samples/MockAnafServer` | A local stand-in for ANAF, so everything is testable without credentials |
| `samples/SampleWebApp` | Blazor Server app exercising every method — doubles as documentation |
| `tests/RomaniaEFactura.LiveTests` | The run against ANAF itself. Inert unless deliberately configured |
| `docs/edit-models.md` | Filling in an invoice: what the model derives, and the Romanian rules that surprise |
| `docs/live-run.md` | Running against ANAF's real test environment — needs credentials and a certificate |
| `docs/anaf-wire-formats.md` | How the ANAF API actually behaves. Read this before changing transport code |
| `docs/security.md` | What the library protects, what the host application owns, and why |
| `docs/multi-tenancy.md` | Serving several companies from one deployment |
| `docs/company-lookup.md` | Asking ANAF about a company before invoicing it |
| `docs/persistence.md` | The library's own tables, providers tested, and migrations |
| `documentation_efactura/` | ANAF's own published specifications |

## Contributing

`docs/anaf-wire-formats.md` is the source of truth for anything touching the wire. If ANAF's real
behaviour turns out to differ, fix that document first and let the mock and the client follow —
do not work around a discrepancy at the call site.

## License

MIT. See [LICENSE](LICENSE).
