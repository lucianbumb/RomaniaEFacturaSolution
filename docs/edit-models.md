# Filling in an invoice

`InvoiceEditModel` is the route most applications want. It exists so that sending a compliant
e-Factura invoice does not require reading EN16931, and so that a mistake is a red field on a form
rather than a rejection that arrives hours later.

```csharp
public class InvoicePage(IRomaniaEFacturaService efactura)
{
    public async Task<IActionResult> Send(InvoiceEditModel invoice)
    {
        var report = efactura.Verify(invoice);
        if (!report.IsValid) return View(invoice);          // findings carry property paths

        var result = await efactura.SendInvoiceAsync(invoice);
        return result.IsSuccess
            ? RedirectToAction("Sent", new { index = result.Value.UploadIndex })
            : View("SendFailed", result.Error);
    }
}
```

## What you do not fill in

Nothing in the model asks for a total, a VAT amount, or a line net amount. EN16931 requires all of
them to be stated and then spends a dozen rules checking they agree with the lines — BR-CO-10,
BR-CO-13, BR-CO-14, BR-CO-15, BR-CO-16 and the whole `BR-*-08` and `BR-*-09` families. Every one of
those figures follows from the lines, so the library computes them. Those rules are not caught;
they are unrepresentable.

| Business term | Where it comes from |
|---|---|
| BT-131 line net amount | quantity × unit price ÷ base quantity, less the line discount, plus the line charge |
| BT-106 sum of line net amounts | the lines |
| BT-107 / BT-108 allowances and charges | `AllowancesAndCharges` |
| BT-109 total without VAT | BT-106 − BT-107 + BT-108 |
| BG-23 VAT breakdown | lines grouped by VAT category **and rate** |
| BT-110 total VAT | the breakdown |
| BT-112 total with VAT | BT-109 + BT-110 |
| BT-115 amount due | BT-112 − BT-113 |

They are exposed as read-only properties (`invoice.TaxInclusiveTotal` and so on), so a form can
show a running total as the user types.

Amounts round to two decimal places away from zero — not .NET's banker's rounding, which would
turn 0.125 into 0.12 and put the library at odds with the accounting system feeding it. The unit
price is the exception: BT-146 permits more decimals, and ANAF's own example uses four.

## What the types decide for you

`VatCategory` is an enumeration, so a mistyped UNCL5305 code cannot be written down. It also fixes
the rate: only `StandardRate` uses the `VatRate` you supply. Setting 19% on an exempt line does not
produce a document that fails BR-E-05 — the rate is simply not written. `OutsideScope` carries no
rate at all, which is different from a rate of zero and is what BR-O-08 checks.

Line-level and document-level adjustments look identical in UBL and differ in one crucial respect:
a document-level allowance **must** carry a VAT category, and a line-level one **must not**
(UBL-CR-599). The mapper handles that; the model does not expose the choice.

An exemption reason is asked for on the line whose treatment prompts the question, but written to
the document-level VAT breakdown — the only place EN16931 permits it. `DocumentEditModel` also has
one, used for any category whose lines gave none, which covers the common wholly-exempt invoice.

## Two Romanian rules worth knowing

**A Bucharest address states its city as a sector code.** `SECTOR1` through `SECTOR6`, never
`Bucuresti`. Rule BR-RO-100 applies to the seller, the buyer and the delivery address, and nothing
about an address hints at the exception. `RomanianCounties.BucharestSectors` lists the values.

**A county is a code, not a name.** `RO-B`, `RO-CJ`. `RomanianCounties.All` pairs every code with
its county name, ready for a dropdown. Bucharest is `RO-B`; `RO-BU` is not a code at all, and
`RO-BZ` is Buzău.

## Validation

`Verify` runs two stages. The first checks the model — DataAnnotations plus the cross-field rules —
and reports against property paths such as `Lines[2].UnitCode` and `Buyer.Address.County`, so a form
can put each finding beside the input that caused it. Only if that passes does the second stage map
to UBL and run the full CIUS-RO engine. A finding from the second stage on a model that passed the
first is a defect in this library, not in your data.

The recursion in the first stage is the library's own. `Validator.TryValidateObject` checks one
object and stops, and Blazor's `DataAnnotationsValidator` does the same — neither looks inside
`Lines`. ASP.NET Core MVC's model binder does recurse, which makes the gap easy to miss until a
Blazor page hits it. Calling `Verify` covers it either way.

### In a Blazor form

Use `<EFacturaValidator />` in place of `<DataAnnotationsValidator />`:

```razor
<EditForm Model="invoice" OnValidSubmit="SendAsync">
    <EFacturaValidator />
    <ValidationSummary />

    <InputText @bind-Value="invoice.Lines[0].Name" />
    <ValidationMessage For="() => invoice.Lines[0].Name" />
</EditForm>
```

It runs the same two-stage check the service does and attaches each finding to the field that
caused it, walking a path such as `Lines[2].UnitCode` back to the line object the input is bound
to. The built-in validator would leave that line unchecked entirely, so a form using it enables its
send button on documents ANAF rejects.

Validation runs on every field change rather than only on the changed field, because almost every
rule here is a cross-field one — a total against its lines, an exemption reason against a VAT
category — and validating one field in isolation leaves stale messages on the fields it affects.

## Limits

- **RON only.** BR-RO-030 requires a foreign-currency document to state its VAT in RON as well
  (BT-6 and BT-111), which the mapper does not yet produce. Rather than build a document ANAF would
  refuse, the model refuses it first and points at `SendRawXmlAsync`.
- **Supporting documents and attachments (BG-24) are not modelled**, nor is card payment
  (BT-87/88) or a third address line. Tracked in
  [#23](https://github.com/lucianbumb/RomaniaEFacturaSolution/issues/23).

Anything the model cannot express goes through `SendRawXmlAsync`, which is unverified: the promise
about format does not extend to it.

## Describing what was sold

`Name` and `Description` are prose. `ItemAttributes` is structured — name and value pairs a buyer's
system can act on:

```csharp
line.ItemAttributes =
[
    new ItemAttributeEditModel { Name = "Culoare", Value = "Albastru" },
    new ItemAttributeEditModel { Name = "Serie",   Value = "SN-4417" },
];
```

Both halves are required (BR-54), the name is capped at 50 characters and the value at 100 — an
asymmetry that is easy to get backwards — and a line may carry at most 50.

## Selling into Romania from abroad

A seller not established in Romania appoints a fiscal representative, and the invoice must name
them. Set `TaxRepresentative`; the same address rules apply to it as to the seller, including the
Bucharest sector rule.

## Credit notes and buyer messages

`CreditNoteEditModel` is the same shape, with two differences it enforces. It must reference the
invoice it corrects, and its quantities are stated **positive** — the document is already a credit,
so entering negatives credits the wrong way round.

`BuyerMessageEditModel` is how a buyer disputes an invoice inside e-Factura rather than by email:
an upload index and a message, sent with `standard=RASP`. Note the provenance caveat in
[anaf-wire-formats.md](anaf-wire-formats.md) — this is the one wire format ANAF does not publish.
