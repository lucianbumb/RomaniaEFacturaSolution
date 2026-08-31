# CIUS-RO rule coverage

What the library checks, and what it deliberately does not. The Schematron ANAF ships inside its
validator defines 97 `BR-RO-*` rules; this records where each stands, so the guarantee in the
README can be read as a claim about something specific rather than a hope.

Two tests keep this document from drifting:

- `CiusRoLengthTableTests` parses ANAF's own `RO16931-rules.sch` and asserts every length constant
  matches, and that no length rule is silently unaccounted for.
- `OracleAgreementTests` runs a corpus of valid and deliberately-invalid documents through
  `ROeFacturaValidator.jar` and asserts our verdict matches ANAF's.

## Enforced

### Structure and identity

| Rule | What it demands |
|---|---|
| `BR-RO-001` | The CIUS-RO specification identifier (BT-24) is exactly the 1.0.1 value |
| `BR-RO-010` | The document number contains at least one digit |
| `BR-RO-081` / `082` | Seller and buyer street (BT-35, BT-50) are present |
| `BR-RO-091` / `092` | Seller and buyer city (BT-37, BT-52) are present |
| `BR-RO-090` / `092` | A Romanian address states a county |
| `BR-RO-100` / `101` | A Bucharest address states its city as `SECTOR1`…`SECTOR6` |
| `BR-RO-110` / `111` | A Romanian county is an ISO 3166-2:RO code, not a name |
| `BR-RO-120` | The buyer is identifiable by BT-47 or BT-48 |
| `BR-RO-180` / `201` / `202` / `211` / `212` | A delivery address is complete and correctly coded |
| `BR-RO-210` | A delivery address states a subdivision **whatever** its country |

### Amounts and VAT

Enforced through EN16931's own rules rather than the Romanian additions: `BR-CO-10` through
`BR-CO-18`, the `BR-S`/`BR-Z`/`BR-E`/`BR-AE`/`BR-IC`/`BR-G`/`BR-O` families, and `BR-DEC-*`. The
edit models make most of these unrepresentable rather than merely detectable, by deriving every
total from the lines.

`BR-RO-030` is enforced as a limit of the models: a non-RON document needs its VAT stated in RON as
well (BT-6, BT-111), which the mapper cannot yet produce, so the model refuses rather than building
something ANAF would reject.

### Lengths and occurrences

Every `BR-RO-L*` and `BR-RO-A*` rule whose field the library can express — roughly forty-five of
them. See `CiusRoLengths`, and the table in [anaf-wire-formats.md](anaf-wire-formats.md).

## Unrepresentable

These rules cap or constrain fields the library has no model for, so nothing can violate them
through its API. Sending such a document requires `SendRawXmlAsync`, which carries no guarantee.

Modelling them is tracked as
[#23](https://github.com/lucianbumb/RomaniaEFacturaSolution/issues/23). Until then, a field's
absence from this library is the reason its rules are not checked — not an oversight.

| Fields | Rules |
|---|---|
| Seller tax representative (BG-11) | `BR-RO-140`, `150`, `160`, `170`, `L0203`, `L0503`, `L153`, `L1012`, `L1013`, `L206` |
| Item attributes (BG-32) | `BR-RO-A052`, `L0505`, `L1025` |
| Document attachments (BT-124, BT-125) | `BR-RO-L210`, `L211` |
| Card payment (BT-88) | `BR-RO-L209` |
| Address line 3 (BT-162/163/165) | `BR-RO-L1003`, `L1008`, `L1015` |
| Delivery party name (BT-70) | `BR-RO-L207` |
| Sales order, receiving advice, despatch advice, tender references (BT-14…17) | `BR-RO-L0304`…`L0307` |

## Enforced by construction

| Rules | Why nothing can violate them |
|---|---|
| `BR-RO-DT001`…`DT006` | Dates are `DateOnly` and `DateTime`, serialized with `[XmlElement(DataType = "date")]`, so `YYYY-MM-DD` is the only form the library can emit. A malformed date in an incoming document fails deserialization rather than validation. |
| `BR-RO-040` | The tax point date code is not settable; the library never emits BT-8. |
| `BR-RO-065` | The seller always carries BT-30, and BT-31 whenever a VAT number is supplied. |

## Known divergence from ANAF's reporting

The code ANAF prints is not always the rule id. All three 300-character rules — `BR-RO-L301`,
`L302`, `L303` — report as `[BR-RO-L300]`, which is not the id of any rule; `BR-RO-092` reports as
`[BR-RO-090]`. Findings from this library use the **rule id**, so a code from here can be found in
the Schematron and a code from ANAF's validator sometimes cannot.
