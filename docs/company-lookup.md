# Looking a company up at ANAF

ANAF publishes a register of taxpayers as a separate service from e-Factura. It needs no
authorization, so it works before anybody has connected a company — which is what makes it usable
on a form.

```csharp
var result = await efactura.LookupCompanyAsync("RO12345674");

if (result.IsSuccess && result.Value is { } company)
{
    invoice.Buyer = company.ToPartyEditModel();
}
```

## What it answers

Three of the fields decide how a document has to be built, and none of them is something a person
entering a buyer would think to check:

| Property | What it decides |
|---|---|
| `IsRegisteredForEFactura` | In the RO e-Factura register. A company that is not has to be sent through `uploadb2c`; sending it as ordinary B2B is refused rather than delivered. |
| `IsVatRegistered` | Whether a VAT identifier (BT-48) belongs on the document at all, and with it whether a reverse charge or an intra-community exemption is available. |
| `IsInactive` | On the register of inactive taxpayers. Transactions with one are treated differently for deduction. |

The rest is what saves the typing: `Name`, `RegistrationNumber` (the commerce register number),
`Phone`, `CaenCode`, `Iban`, and the registered office and fiscal domicile broken into parts.

## Filling in a party

`ToPartyEditModel()` maps the register onto the party an invoice carries — including the county
code, which becomes the CIUS-RO subdivision (`CJ` → `RO-CJ`) that Romanian addresses are required
to state.

It is a starting point rather than a finished party. The register holds no email address, and its
address is only as precise as what was registered, so a person still confirms it.

**The VAT identifier is set only when the company is actually registered for VAT.** Writing `RO` in
front of a fiscal code that carries no VAT registration produces a document claiming something
untrue about the buyer, and it is an easy mistake because the two look identical.

## The two limits, and what the library does about them

ANAF publishes both with the service:

- **at most 100 fiscal codes per request**
- **at most one request per second, per client**

So `LookupCompaniesAsync` is the primary method and the single-company one wraps it: asking about a
hundred companies together costs one call, and asking individually costs a hundred seconds.

More than a hundred codes are **batched and paced** rather than refused, because the alternative is
every caller writing the same chunking loop and none of them writing the pacing. A large lookup
accordingly takes about a second per hundred — the service's speed, not an inefficiency to route
around.

The pacing is **per client, not per company**, unlike the e-Factura endpoints. Two lookups about
different companies still have to be a second apart, so the state is shared across the process.

## If you expose this to people you do not trust

The lookup needs no ANAF authorization, so it is tempting to put it behind an open form — and it is
the one call in the library that works before a company has been connected, which makes that
tempting for good reasons.

Two things follow from the one-request-per-second limit being **per client**, not per caller:

- Every lookup in the process queues behind every other. Nothing breaks — the waits are
  asynchronous, so no thread is held — but a burst from one caller delays everybody else's.
- A lookup looks cheap because it needs no credential. It is not: it is a network call to ANAF with
  a hard rate limit attached.

**Rate-limit your own endpoint**, and prefer `LookupCompaniesAsync` over a loop. The library paces
what it sends; it cannot pace what you ask it for, and it should not decide your endpoint's policy.

The base address is held to the same rule as the API and OAuth ones — `https`, or `http` only
against a loopback host — checked at startup. It carries no credential, but its answer decides
whether a document is addressed as B2B or through `uploadb2c`, so anyone able to answer the request
can make an application send documents the wrong way.

## Duplicates and unknown codes

Repeated codes are asked about once — `12345674`, `RO12345674` and `" 12345674 "` are one company.

A code the register does not know comes back in `NotFound` rather than as a failure. It is an
ordinary answer: a code can be mistyped, or belong to something that was never registered. A
caller distinguishes it from an outage by the result being a success.

A code whose control digit does not match is refused locally, before any request, for the same
reason it is refused before an e-Factura call.
