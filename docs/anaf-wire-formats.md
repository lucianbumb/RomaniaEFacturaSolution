# ANAF e-Factura wire formats

The authoritative reference for how the ANAF RO e-Factura API actually behaves on the wire.
Both `samples/MockAnafServer` and the library's transport client are written against this
document; when reality and this document disagree, fix this document first and let both sides
follow.

## Provenance

Every statement here is traceable to one of:

| Source | What it establishes |
|---|---|
| **ANAF OpenAPI specs** — `tests/fixtures/anaf-openapi/*.json`, extracted from the Swagger pages linked in `documentation_efactura/ANAF_eFactura_Documentation.md` | Request parameters, and every documented success and failure response, with examples |
| **Production-proven code** — a working download implementation that ran against production ANAF | Behaviours ANAF does not document anywhere |
| **Live probes** | Response shapes for the public `validare` endpoint |

Where something is inferred rather than observed, it is marked **UNCONFIRMED** and carries the
open question. Nothing else in the codebase may hardcode a wire-format assumption; it belongs
here.

## Hosts

| Purpose | Host |
|---|---|
| OAuth2 (test and production alike) | `https://logincert.anaf.ro/anaf-oauth2/v1` |
| API, production | `https://api.anaf.ro/prod/FCTEL/rest` |
| API, test | `https://api.anaf.ro/test/FCTEL/rest` |
| Unauthenticated `validare` / `transformare` | `https://webservicesp.anaf.ro/prod/FCTEL/rest` |

There is **no separate test IdP** — OAuth uses the same host for both environments.

The `validare` and `transformare` endpoints exist on both `api.anaf.ro` (OAuth-protected) and
`webservicesp.anaf.ro` (unauthenticated). Calling the `api.anaf.ro` variant without a bearer token
returns `401`; this is the specific defect that made v2's upload path unreachable.

---

## Rule 1 — errors arrive with HTTP 200

**This is the single most important fact about this API.** Every endpoint signals failure inside a
`200 OK` response, in that endpoint's own format:

| Endpoint | Failure looks like |
|---|---|
| `upload`, `uploadb2c` | `200` + `ExecutionStatus="1"` + one or more `<Errors errorMessage="…"/>` |
| `stareMesaj` | `200` + `<Errors errorMessage="…"/>` |
| `listaMesajeFactura`, `listaMesajePaginatieFactura` | `200` + `{"eroare":"…","titlu":"Lista Mesaje"}` |
| `descarcare` | `200` + **a JSON error body in place of the ZIP** |

Consequences that must be honoured everywhere:

- **Never branch on `IsSuccessStatusCode`.** All response interpretation goes through a single
  shared envelope reader that content-sniffs the body.
- The `descarcare` case is the trap: JSON arriving where a ZIP is expected makes `ZipArchive`
  throw an opaque `InvalidDataException`, discarding the real reason
  (`Nu aveti dreptul sa descarcati acesta factura`).
- A genuine `400` also exists, and returns a *different*, JSON-only shape:
  `{"timestamp","status","error","message"}`. So one endpoint can answer in XML on `200` and JSON
  on `400`. Content-sniff; do not assume a content type per endpoint.

## Rule 2 — daily per-id call quotas

ANAF caps repeated calls for the same document:

| Endpoint | Observed cap | Error text |
|---|---|---|
| `stareMesaj` | ~20/day | `S-au facut deja 20 descarcari de mesaj in cursul zilei` |
| `descarcare` | ~10/day | `S-au facut deja 10 descarcari de mesaj in cursul zilei` |

The wording implies the cap is **per message**, but the specs do not state the scope.
**UNCONFIRMED: per-id vs per-CIF vs per-application.** Resolved in M8.

A fixed polling interval exhausts the `stareMesaj` budget within an hour and then goes blind for
the rest of the day. Polling must run on a persisted per-id daily budget with backoff.

---

## Endpoints

### `POST /upload`, `POST /uploadb2c`

`uploadb2c` is mandatory for B2C invoices. Parameters are identical.

| Parameter | Required | Values |
|---|---|---|
| `standard` | yes | `UBL`, `CN`, `CII`, `RASP` |
| `cif` | yes | numeric, **`RO` prefix stripped** |
| `extern` | no | `DA` — buyer outside Romania |
| `autofactura` | no | `DA` — issued by the beneficiary on the supplier's behalf |
| `executare` | no | `DA` — filed by an enforcement body |

Body is the raw XML. **Maximum 10 MB.**

Success:

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<header xmlns="mfp:anaf:dgti:spv:respUploadFisier:v1"
        dateResponse="202108051140" ExecutionStatus="0" index_incarcare="3828"/>
```

Failure (still `200`):

```xml
<header xmlns="mfp:anaf:dgti:spv:respUploadFisier:v1"
        dateResponse="202108051144" ExecutionStatus="1">
    <Errors errorMessage="Marime fisier transmis mai mare de 10 MB."/>
</header>
```

Other documented `errorMessage` values include a bad `standard`, a non-numeric CIF, no SPV rights
for any CIF, and no SPV rights for the requested CIF. `400` returns the JSON shape from Rule 1.

`index_incarcare` must be persisted — it is the only handle on the submission.

### `GET /stareMesaj`

Parameter: `id_incarcare` (the `index_incarcare` from upload).

```xml
<header xmlns="mfp:anaf:dgti:efactura:stareMesajFactura:v1" stare="ok" id_descarcare="1234"/>
```

`stare` values — this is the complete set:

| `stare` | Meaning |
|---|---|
| `ok` | Validated and processed. `id_descarcare` present. The invoice reached the buyer. |
| `nok` | Rejected. `id_descarcare` present, and the archive holds the errors. The invoice did **not** reach the buyer. |
| `in prelucrare` | Still processing. No `id_descarcare`. |
| `XML cu erori nepreluat de sistem` | Rejected at upload; the reason was returned by the upload call itself. |

v2 polled for `procesat`/`finalizat`/`respins`, none of which ANAF ever returns — so it never
detected completion.

### `GET /listaMesajeFactura`

| Parameter | Required | Notes |
|---|---|---|
| `zile` | yes | 1–60 |
| `cif` | yes | numeric |
| `filtru` | no | `E` errors, `T` sent, `P` received, `R` buyer message |

### `GET /listaMesajePaginatieFactura`

| Parameter | Required | Notes |
|---|---|---|
| `startTime` | yes | Unix ms |
| `endTime` | yes | Unix ms |
| `cif` | yes | numeric |
| `pagina` | yes | 1-based |
| `filtru` | no | as above |

**The 60-day limit applies here too**, despite the endpoint accepting arbitrary timestamps:
`startTime = … nu poate fi mai vechi de 60 de zile fata de momentul requestului`. Clamp before
calling.

Both endpoints return the same JSON:

```json
{
  "mesaje": [
    {
      "data_creare": "202210311452",
      "cif": "8000000000",
      "id_solicitare": "5001120362",
      "detalii": "Erori de validare identificate la factura primita cu id_incarcare=5001120362",
      "tip": "ERORI FACTURA",
      "id": "3001474425"
    }
  ],
  "numar_total_pagini": 1
}
```

- `data_creare` is **`yyyyMMddHHmm` as a string**, never an ISO timestamp. Same for `dateResponse`
  on upload. Parsing either as `DateTime` throws.
- `id` and `id_solicitare` are strings.
- `tip` values: `FACTURA TRIMISA`, `FACTURA PRIMITA`, `ERORI FACTURA`,
  `MESAJ CUMPARATOR PRIMIT` / `MESAJ CUMPARATOR TRANSMIS`.
- `{"eroare":"Nu exista mesaje"}` means **empty, not failure**.
- `numar_total_pagini` drives pagination on the paginated endpoint.

### `GET /descarcare`

Parameter: `id` (from the message list's `id`, or `stareMesaj`'s `id_descarcare`).

Returns a ZIP holding two XML files: the original invoice (or the validation errors) and the
Ministry of Finance electronic signature. **The signature must be archived** — it is the proof of
submission.

The document inside may be an `Invoice`, a `CreditNote`, a `DebitNote`, an error document, or a
buyer message. It is frequently not an invoice, so the download result must be discriminated
rather than assumed.

### `POST /validare/{standard}`

`standard` is `FACT1` or `FCN`. `Content-Type: text/plain`. Response is JSON in both directions:

```json
{"stare":"nok","Messages":[{"message":"Fisierul transmis nu este valid. …"}],"trace_id":"…"}
```

`stare` is `ok` or `nok`. v2 modelled this as `succes`/`erori`/`avertismente`, none of which
exist, so a valid invoice deserialized to `Success=false` with an empty error list.

### `POST /transformare/{standard}[/DA]`

`standard` is `FACT1`, `FCN`, or `FDN`. Appending `/DA` skips validation (ANAF does not guarantee
PDF correctness for unvalidated XML). `Content-Type: text/plain`.

Returns PDF bytes — **or base64-encoded PDF** (recognisable by the `JVBER` prefix). Handle both.

### `POST /api/validate/signature`

Multipart: `file` (the invoice XML) and `signature` (the signature XML). Both come from the
`descarcare` archive.

---

## OAuth 2.0

Authorization code flow. The authorize step requires a qualified digital certificate in the
user's browser, so **it cannot be performed headlessly** — a host application must redirect a real
person through it.

- `GET /authorize` — `response_type=code`, `client_id`, `redirect_uri`, `scope`, `state`,
  and `token_content_type=jwt`.
- `POST /token` — HTTP Basic auth with `base64(client_id:client_secret)`, form body with
  `grant_type=authorization_code|refresh_token`, plus `token_content_type=jwt`.

Access tokens last about 90 days, refresh tokens about 365. **The refresh token must be persisted
independently of access-token expiry** — v2 stored both in one cache entry with a 30-minute sliding
expiration, so thirty idle minutes destroyed the refresh token and forced a fresh certificate
login.

`state` must be signed. Prior implementations used a bare `cif|returnUrl`, which is CSRF-open.

---

## Undocumented behaviour

Learned from a working production implementation. ANAF documents none of it.

- **Messages with no `id`.** Some list entries carry only `id_solicitare`. Resolve the download id
  with a `stareMesaj` round-trip before calling `descarcare`.
- **Nested ZIPs.** The archive can contain another ZIP; recurse.
- **PDFs inside the archive.** Sometimes a `.pdf` entry is present, making `transformare`
  unnecessary.
- **`schemaLocation` must be stripped with a regex, not by round-tripping through `XDocument`.**
  Re-serialising rewrites namespace prefixes and ANAF then rejects the document.
- **Strip the `RO` prefix from CIF** before every API call.
- **429 is real.** Retry with exponential backoff, and serialize requests per CIF.

## UBL structure traps

Both of these produced schema-invalid XML in v2 and are worth stating explicitly:

- `AccountingSupplierParty` and `AccountingCustomerParty` are `SupplierParty`/`CustomerParty`
  types that **wrap a `<cac:Party>` child**. Mapping them straight to a party element is invalid.
- `IssueDate` and `DueDate` are `xs:date`. .NET's `XmlSerializer` emits
  `2026-08-31T00:00:00` for a `DateTime` unless the member is annotated
  `[XmlElement(DataType = "date")]`.

## The buyer message (RASP)

A buyer answers a received invoice by uploading a message with `standard=RASP`. It is not UBL and
carries none of the EN16931 rules — one element with two attributes:

```xml
<header xmlns="mfp:anaf:dgti:spv:reqMesaj:v1"
        index_incarcare="3828"
        message="Cantitatea livrata nu corespunde comenzii."/>
```

**This is the one wire format here that is not confirmed by an ANAF source.** ANAF publishes no
schema for it, and it appears in none of the four OpenAPI specifications the rest of this document
is built from — the API documentation names `RASP` as a valid `standard` value and says nothing
more. The shape above is corroborated by two independent third-party sources that agree on the
namespace and both attribute names, which is enough to implement against and not enough to call
confirmed. Sending one against the real test environment settles it.

## CIUS-RO rules that surprise

Romania narrows EN16931 in ways that are not visible from the European specification. These were
found by running generated documents through `ROeFacturaValidator.jar`, not by reading:

| Rule | What it demands |
|---|---|
| **BR-RO-100 / 101** | If the country is `RO` and the county is `RO-B` (Bucharest), the **city must be a sector code** — `SECTOR1` … `SECTOR6`. `Bucuresti` is rejected. Nothing in the address hints at the exception, and it applies to the seller, the buyer and the delivery address alike. |
| BR-RO-110 / 111 | A Romanian address states its county as an ISO 3166-2:RO code (`RO-B`, `RO-CJ`), never as a county name. |
| BR-RO-210 | A delivery address must state a country subdivision **whatever its country** — stricter than the seller and buyer rules, which only demand one for Romanian addresses. |
| BR-RO-010 | The document number must contain at least one digit. |
| BR-RO-030 | A document in a currency other than RON must also state its VAT in RON (BT-6 and BT-111). |
| BR-IC-11 / 12 | An intra-community supply must state the delivery date or the invoicing period, **and** the delivery country. |
| BR-RO-A020 / A051 / A052 / A500 | Caps on repeating groups: 20 notes, 50 supporting documents, 50 item attributes, 500 preceding references. |

The Schematron defines 97 `BR-RO-*` rules in total. What the library implements, and what it does
not, is tracked as its own issue.

### What the offline validator cannot check

`ROeFacturaValidator.jar` demands a Romanian buyer CUI unconditionally, and rejects any invoice
without one with `nu a fost identificat cui cumparator`. The live API handles that case through the
`extern=DA` upload parameter, which a local file cannot carry — so **every export and
intra-community invoice fails the offline validator while being perfectly legal**. The oracle suite
pins this behaviour rather than working around it, so that a future validator release which fixes
it is noticed.

## Open questions

| Question | Resolved by |
|---|---|
| Scope of the daily quotas — per-id, per-CIF, or per-application | M8 |
| Real-world timing of `in prelucrare` → `ok`, to calibrate the reconciler's backoff | M8 |
| Whether the RASP message shape above is what ANAF actually accepts | M8 |
| Whether `extern=DA` makes the live service accept a foreign-buyer invoice the offline validator refuses | M8 |
