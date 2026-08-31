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

The guarantee is stated for the edit models deliberately. `Verify(UblInvoice)` — the path for
callers who build UBL directly — runs the same engine, but a UBL document can express things the
edit models cannot, and the rule port is not yet complete for those
([#18](https://github.com/lucianbumb/RomaniaEFacturaSolution/issues/18)).

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

## Repository layout

| Path | What it is |
|---|---|
| `src/RomaniaEFactura` | The library — the only thing published to NuGet |
| `tests/RomaniaEFactura.Tests` | Unit tests, plus the validator-oracle comparison |
| `tests/RomaniaEFactura.IntegrationTests` | Full lifecycle against the mock server |
| `samples/MockAnafServer` | A local stand-in for ANAF, so everything is testable without credentials |
| `samples/SampleWebApp` | Blazor Server app exercising every method — doubles as documentation |
| `docs/edit-models.md` | Filling in an invoice: what the model derives, and the Romanian rules that surprise |
| `docs/anaf-wire-formats.md` | How the ANAF API actually behaves. Read this before changing transport code |
| `documentation_efactura/` | ANAF's own published specifications |

## Contributing

`docs/anaf-wire-formats.md` is the source of truth for anything touching the wire. If ANAF's real
behaviour turns out to differ, fix that document first and let the mock and the client follow —
do not work around a discrepancy at the call site.

## License

MIT. See [LICENSE](LICENSE).
