# Development instructions

Romanian e-Factura (ANAF SPV) integration library for .NET web applications.

> v3.0.0 is a ground-up rebuild. v2.x is not functional against the live ANAF API — do not treat
> any v2 code or documentation as a reference.

## The guarantee this library exists to make

**If it compiles and `Verify()` returns valid, ANAF will never reject the document for format
reasons.** Validation is fully offline. Everything that genuinely can fail at runtime — not
authorized, token expired, ANAF down, rate limited, no SPV rights — is a **typed result**, never
an exception. Exceptions are for programming errors only.

## Read this before touching transport code

`docs/anaf-wire-formats.md` is the source of truth for how the ANAF API behaves. Two rules from it
govern the whole codebase:

1. **ANAF returns errors with HTTP 200.** Never branch on `IsSuccessStatusCode`. All responses go
   through the single shared envelope reader that content-sniffs the body. A `descarcare` call can
   answer with a JSON error body where a ZIP was expected.
2. **Daily per-id call quotas** (`stareMesaj` ~20/day, `descarcare` ~10/day). Polling must run on a
   persisted budget with backoff, never a fixed interval.

If ANAF's real behaviour differs from that document, fix the document first and let the mock and
the client follow. Never work around a discrepancy at the call site, and never hardcode a
wire-format assumption anywhere else.

## Layout

| Path | Purpose |
|---|---|
| `src/RomaniaEFactura` | The library. The only project published to NuGet (package id `RomaniaEFacturaLibrary`) |
| `tests/RomaniaEFactura.Tests` | Unit tests; `HttpMessageHandler` doubles replaying `tests/fixtures/anaf-openapi` |
| `tests/RomaniaEFactura.IntegrationTests` | Full lifecycle against the mock server |
| `samples/MockAnafServer` | Local ANAF stand-in, so everything is testable without credentials |
| `samples/SampleWebApp` | Blazor Server app exercising every method |

## Conventions

- Target framework is `net10.0`. Warnings are errors in the library project.
- The public surface is one registration call, `builder.AddRomaniaEFactura(...)`, and one injected
  interface, `IRomaniaEFacturaService`. Segment internally; keep one facade externally.
- Token storage is keyed by CIF. `HttpContext` must never appear in the storage contract — that
  abstraction leak is what made v2 unusable from background jobs, which is where invoices are
  actually sent from.
- Edit models are flat and app-developer-friendly, not UBL-shaped. Mapping to UBL lives behind
  them, and that mapping is the value the library adds.
- Strip the `RO` prefix from CIF before every API call.

## Testing

- Every new test must fail against the unfixed code first. A test that passes before the fix
  proves nothing; where a test cannot go red, say so in its own doc comment.
- ANAF's `ROeFacturaValidator.jar` runs in tests as an oracle for the CIUS-RO engine. Java is a
  dev-time test dependency and is never shipped.
- CI runs on GitHub-hosted runners — see the comment in `.github/workflows/ci.yml` for why.
