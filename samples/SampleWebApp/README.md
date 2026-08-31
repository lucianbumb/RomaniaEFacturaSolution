# Sample web app

A Blazor Server application exercising every method on `IRomaniaEFacturaService`. It is the
library's documentation as much as its demonstration, and the check that the API is pleasant to
consume rather than merely correct.

## Running it

Two projects, in this order:

```powershell
dotnet run --project samples/MockAnafServer/MockAnafServer.csproj --urls http://localhost:5049
```

```powershell
dotnet run --project samples/SampleWebApp/SampleWebApp.csproj --urls http://localhost:5203
```

Then open <http://localhost:5203> and click **Authorize with ANAF** on the Connection page. The
library requires an authenticated user on its two endpoints, so the sample signs you in first
through a stand-in `/sign-in` — a real application uses its own identity system, and
[docs/security.md](../../docs/security.md) explains why the requirement is not optional. Against
the mock that completes immediately; against the real service it asks for a qualified certificate.

`appsettings.json` points `ApiBaseAddress` and `OAuthBaseAddress` at the mock. Remove both to talk
to ANAF's test environment — nothing else changes. Put real credentials in user secrets, never in
`appsettings.json`.

## What each page shows

| Page | What it demonstrates |
|---|---|
| **Connection** | The authorize flow, and "not authorized yet" as a first-class state rather than an error discovered mid-send. Also `DisconnectAsync`. |
| **New invoice** | `InvoiceEditModel` in an `EditForm`, live derived totals and VAT breakdown, `Verify` with BR codes, `SendInvoiceAsync`. |
| **Credit notes & messages** | `SendCreditNoteAsync`, `SendBuyerMessageAsync` (RASP), and `SendRawXmlAsync` — the unverified escape hatch. |
| **Sent** | `GetSubmissionsAsync` and `GetSubmissionAsync` from local records, plus forcing a reconciliation pass. |
| **Inbox** | `SyncInboxAsync` with its watermark and dedup, `GetInboxAsync` with filtering. |
| **Document** | `GetDocumentAsync` (discriminated), `GetArchiveAsync`, `RenderPdfAsync`. |

A test — `SampleAppCoverageTests` — fails if the interface grows a method no page reaches, so the
claim above stays true rather than being true once.

## Three things worth noticing

**The validator is not the built-in one.** `EditForm` uses `<EFacturaValidator />`, not
`<DataAnnotationsValidator />`. Blazor's own validator checks the model object and stops, so bound
to an invoice it would validate the number and the currency and silently ignore every rule on every
line — a form using it enables its send button on an invoice with a nameless line. Clear an item
name on the New invoice page and watch the message appear beside *that line's* field.

**The city becomes a dropdown for Bucharest.** Choose county `RO-B` and the city field turns into a
list of `SECTOR1`…`SECTOR6`. Rule BR-RO-100 rejects `Bucuresti` outright, and a free-text box there
is the single most likely way to have an otherwise perfect invoice refused.

**"Reconcile now" often reports that nothing was due.** That is the design, not a fault. ANAF caps
status checks at roughly twenty a day per document, so the schedule widens — 1, 2, 5, 10, 20, 40
minutes, then hours — and each pending row shows when it will next be checked. Leave the app running
and the background reconciler settles the document without anyone clicking anything.

## Not shown

The sample uses SQLite and creates its schema on start, which a real deployment would do once rather
than on every boot. It also runs against a single company taken from configuration; the interface
takes a per-call `cif` override throughout, which the Connection page exposes.
