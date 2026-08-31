# Running against ANAF's test environment

Everything else in this repository is verified against a mock seeded from ANAF's own OpenAPI
examples, and against ANAF's own validator for document correctness. That removes most of the
risk. It cannot remove the risk that the mock is unfaithful, which is what this run is for.

**This is a deliberate act by a person, not something a build does.** A live run spends real daily
allowance and files real documents in ANAF's test register, so the suite refuses to run wherever
`CI` is set, and skips entirely unless five environment variables are present.

## What you need first

1. **A registered application.** ANAF issues the client id and secret through
   [Înregistrare API](https://www.anaf.ro/anaf/internet/ANAF/servicii_online/inreg_api). The
   redirect URI you register has to match exactly what the application sends, and cannot be changed
   afterwards without re-registering.
2. **A qualified digital certificate**, and the person holding it. There is no headless path: ANAF
   authorizes by certificate in a browser. Nothing in this repository can automate that step, and
   nothing should try.
3. **A CIF with SPV rights** for that certificate.

## Step 1 — authorize, using the sample app

The sample app is the authorization tool. Point it at the real service by removing the two mock
addresses from `samples/SampleWebApp/appsettings.json`, and put your credentials in user secrets so
they are never written to a file in the repository:

```powershell
dotnet user-secrets --project samples/SampleWebApp set "EFactura:ClientId" "<your client id>"
```

```powershell
dotnet user-secrets --project samples/SampleWebApp set "EFactura:ClientSecret" "<your client secret>"
```

```powershell
dotnet user-secrets --project samples/SampleWebApp set "EFactura:Cif" "<your CIF>"
```

`EFactura:RedirectUri` must equal what you registered with ANAF, and `EFactura:Environment` must be
`Test`. Then run the app, open **Connection**, and click **Authorize with ANAF**. Complete the
certificate login. The Connection page should then show *Connected*, with an expiry roughly ninety
days out.

The token is now stored — encrypted — in `samples/SampleWebApp/efactura-sample.db`. That file is
what the live suite reads. **It contains a working credential: do not commit it, and delete it when
you are finished.**

## Step 2 — run the suite

```powershell
$env:EFACTURA_LIVE = "1"; $env:EFACTURA_LIVE_DB = "samples/SampleWebApp/efactura-sample.db"; $env:EFACTURA_LIVE_CIF = "<your CIF>"; $env:EFACTURA_LIVE_CLIENT_ID = "<your client id>"; $env:EFACTURA_LIVE_CLIENT_SECRET = "<your client secret>"; dotnet test tests/RomaniaEFactura.LiveTests
```

The suite writes `live-run-report.md` into its output directory and prints it to the console. That
report is the deliverable — more than the pass or fail.

### What it does, and what it costs

| Test | Sends | Costs |
|---|---|---|
| `TheCompanyIsAuthorized` | nothing | nothing |
| `TheWholeOutboundJourneyWorks` | one invoice | up to 10 status calls, 1 download |
| `TheInboxCanBeListed` | nothing | one message-list call |
| `TheBuyerMessageFormatIsWhatAnafAccepts` | one invoice, one RASP message | — |
| `AForeignBuyerInvoiceIsAcceptedWithTheExternFlag` | one invoice | — |
| `ErrorsReallyDoArriveInsideHttp200` | nothing | one status call on a nonsense index |

Every document is sent from the test CIF **to itself**, so no real third party receives anything.

## Step 3 — the quota experiment, separately

The one assumption in the library that could be wrong in a way that matters is that ANAF's daily
call cap is counted **per document**. The reconciler's whole widening schedule rests on it. If the
cap is really per company, a business sending fifty invoices a day would exhaust its allowance on
the first three and go blind to the rest — that is a redesign, not a retune.

The experiment is decisive: spend one document's allowance, then ask about a second, untouched
document. If the second still answers, the cap is per document.

It is gated separately because it deliberately burns a day's allowance:

```powershell
$env:EFACTURA_LIVE_QUOTA_PROBE = "1"; dotnet test tests/RomaniaEFactura.LiveTests --filter "QuotaScope"
```

Run it once. Whatever it reports, write the answer into
[anaf-wire-formats.md](anaf-wire-formats.md).

## Step 4 — what to do with the answers

The report answers the questions listed as open in
[anaf-wire-formats.md](anaf-wire-formats.md):

- **Quota scope.** Record it. If it is not per document, `PollSchedule` needs a shared budget
  across documents rather than a per-document backoff, and that is a new issue rather than a tweak.
- **Resolution timing.** If ANAF routinely takes longer than the early intervals assume, the
  schedule should start wider. Record the measurement before changing anything.
- **The RASP format.** If the buyer message is refused, the shape in `BuyerMessageDocument` is
  wrong — it was corroborated by third parties and by no ANAF source, which is exactly why this
  test exists.
- **`extern=DA`.** If a foreign-buyer invoice is refused, either the library has a format problem
  or `extern=DA` is not the mechanism, and the wire-format document is wrong about it.

**Any divergence between the mock and reality is fixed in the mock, never worked around in the
client.** The mock is the thing every other test trusts; a client patched to paper over a mock
defect leaves the whole suite proving something untrue.

## Afterwards

Delete `samples/SampleWebApp/efactura-sample.db` — it holds a live refresh token, valid for about a
year. Clear the environment variables, or open a new shell.
