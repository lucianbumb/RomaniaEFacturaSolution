# RomaniaEFacturaLibrary

Romanian e-Factura (ANAF SPV) integration for .NET web applications — send invoices, download
what arrives, and validate against CIUS-RO before anything leaves your machine.

> **Status: v3.0.0 in development.** The library is being rebuilt from scratch; v2.x on NuGet is
> not functional against the live ANAF API and should not be used. See the
> [v3.0.0 milestones](https://github.com/lucianbumb/RomaniaEFacturaSolution/labels/v3.0.0).

## The guarantee

**If it compiles and `Verify()` returns valid, ANAF will never reject the document for format
reasons.**

Validation runs fully offline — a C# port of the CIUS-RO rules, checked in tests against ANAF's
own validator. Everything that genuinely can fail at runtime (nobody has authorized this CIF yet,
the token expired, ANAF is down, rate limited, no SPV rights) comes back as a typed result you
branch on, not an exception you discover in production.

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

`InvoiceEditModel` carries its own validation rules, so ASP.NET model binding and Blazor
`EditForm` pick them up with no extra wiring.

## Repository layout

| Path | What it is |
|---|---|
| `src/RomaniaEFactura` | The library — the only thing published to NuGet |
| `tests/RomaniaEFactura.Tests` | Unit tests, plus the validator-oracle comparison |
| `tests/RomaniaEFactura.IntegrationTests` | Full lifecycle against the mock server |
| `samples/MockAnafServer` | A local stand-in for ANAF, so everything is testable without credentials |
| `samples/SampleWebApp` | Blazor Server app exercising every method — doubles as documentation |
| `docs/anaf-wire-formats.md` | How the ANAF API actually behaves. Read this before changing transport code |
| `documentation_efactura/` | ANAF's own published specifications |

## Contributing

`docs/anaf-wire-formats.md` is the source of truth for anything touching the wire. If ANAF's real
behaviour turns out to differ, fix that document first and let the mock and the client follow —
do not work around a discrepancy at the call site.

## License

MIT. See [LICENSE](LICENSE).
