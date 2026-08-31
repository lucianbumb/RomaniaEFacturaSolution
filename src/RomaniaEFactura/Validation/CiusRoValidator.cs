using RomaniaEFactura.Ubl;

namespace RomaniaEFactura.Validation;

/// <summary>
/// Validates a document against EN16931 and the Romanian CIUS-RO customisation, entirely offline.
/// </summary>
/// <remarks>
/// <para>
/// This is a C# port of the rules ANAF distributes as a Schematron. The Schematron declares
/// <c>queryBinding="xslt2"</c> and .NET's <c>XslCompiledTransform</c> implements XSLT 1.0 only, so
/// executing it would mean shipping a second XSLT engine to every consumer. Porting instead keeps
/// the package dependency-free and lets findings carry a rule code the UI can act on.
/// </para>
/// <para>
/// Correctness is not asserted from a reading of the specification: the test suite runs a corpus
/// through this engine and through ANAF's own <c>ROeFacturaValidator.jar</c> and requires the two
/// to agree. Rules ANAF enforces outside the Schematron — the CIF control digit, and the
/// requirement that both parties be identifiable — are implemented here too, because an invoice
/// that passes the rules alone can still be refused.
/// </para>
/// </remarks>
public static class CiusRoValidator
{
    /// <summary>Rounding tolerance for the arithmetic rules, per EN16931.</summary>
    private const decimal Tolerance = 0.01m;

    /// <summary>Validates an invoice.</summary>
    public static ValidationReport Validate(UblInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return Validate(DocumentView.From(invoice));
    }

    /// <summary>Validates a credit note.</summary>
    public static ValidationReport Validate(UblCreditNote creditNote)
    {
        ArgumentNullException.ThrowIfNull(creditNote);
        return Validate(DocumentView.From(creditNote));
    }

    private static ValidationReport Validate(DocumentView doc)
    {
        var findings = new List<ValidationFinding>();

        CheckDocumentIdentity(doc, findings);
        CheckParties(doc, findings);
        CheckLines(doc, findings);
        CheckTotals(doc, findings);
        CheckVatBreakdown(doc, findings);
        CheckPaymentTerms(doc, findings);

        return new ValidationReport(findings);
    }

    private static void CheckDocumentIdentity(DocumentView doc, List<ValidationFinding> findings)
    {
        // BR-01 / BR-RO-001: the specification identifier must be exactly the CIUS-RO value.
        if (string.IsNullOrWhiteSpace(doc.CustomizationId))
        {
            findings.Add(new("BR-01", "The document must carry a specification identifier (BT-24).", Path: "CustomizationId"));
        }
        else if (!string.Equals(doc.CustomizationId, UblNamespaces.CustomizationId, StringComparison.Ordinal))
        {
            findings.Add(new("BR-RO-001",
                $"The specification identifier (BT-24) must be exactly '{UblNamespaces.CustomizationId}'.",
                Path: "CustomizationId"));
        }

        if (string.IsNullOrWhiteSpace(doc.Id))
        {
            findings.Add(new("BR-02", "The document must have a number (BT-1).", Path: "Id"));
        }

        if (doc.IssueDate == default)
        {
            findings.Add(new("BR-03", "The document must have an issue date (BT-2).", Path: "IssueDate"));
        }

        if (string.IsNullOrWhiteSpace(doc.TypeCode))
        {
            findings.Add(new("BR-04", "The document must have a type code (BT-3).", Path: "TypeCode"));
        }

        if (string.IsNullOrWhiteSpace(doc.CurrencyCode))
        {
            findings.Add(new("BR-05", "The document must have a currency code (BT-5).", Path: "CurrencyCode"));
        }
    }

    private static void CheckParties(DocumentView doc, List<ValidationFinding> findings)
    {
        CheckParty(doc.Seller, "Seller", "BR-06", "BR-08", "BR-09", findings);
        CheckParty(doc.Buyer, "Buyer", "BR-07", "BR-10", "BR-11", findings);
    }

    private static void CheckParty(
        Party party,
        string role,
        string nameRule,
        string addressRule,
        string countryRule,
        List<ValidationFinding> findings)
    {
        var name = party.PartyLegalEntity?.RegistrationName ?? party.PartyName?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            findings.Add(new(nameRule, $"The {role.ToLowerInvariant()} must have a name.", Path: role));
        }

        if (party.PostalAddress is null)
        {
            findings.Add(new(addressRule, $"The {role.ToLowerInvariant()} must have a postal address.", Path: role));
        }
        else if (string.IsNullOrWhiteSpace(party.PostalAddress.Country?.IdentificationCode))
        {
            findings.Add(new(countryRule,
                $"The {role.ToLowerInvariant()} address must have a country code.",
                Path: $"{role}.PostalAddress.Country"));
        }

        // ANAF requires the county subdivision for Romanian addresses (ISO 3166-2:RO).
        var countryCode = party.PostalAddress?.Country?.IdentificationCode;
        if (string.Equals(countryCode, "RO", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(party.PostalAddress?.CountrySubentity))
        {
            findings.Add(new("BR-RO-090",
                $"A Romanian {role.ToLowerInvariant()} address must have a county code such as RO-B or RO-CJ (BT-39).",
                Path: $"{role}.PostalAddress.CountrySubentity"));
        }

        // Enforced by ANAF outside the Schematron: the party must be identifiable, and a Romanian
        // CIF must carry a correct control digit.
        var identifiers = new List<string?>
        {
            party.PartyLegalEntity?.CompanyId?.Value,
        };
        identifiers.AddRange(party.PartyTaxSchemes.Select(s => s.CompanyId?.Value));

        var present = identifiers.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList();
        if (present.Count == 0)
        {
            findings.Add(new("RO-CIF-MISSING",
                $"The {role.ToLowerInvariant()} must be identifiable by a fiscal code; ANAF rejects the document otherwise.",
                Path: role));
            return;
        }

        foreach (var identifier in present.Where(i => LooksRomanian(i)))
        {
            if (!RomanianCif.IsValid(identifier))
            {
                findings.Add(new("RO-CIF-INVALID",
                    $"The {role.ToLowerInvariant()} fiscal code '{identifier}' has an incorrect control digit.",
                    Path: role));
            }
        }
    }

    /// <summary>
    /// Whether an identifier should be checked as a Romanian CIF. A foreign VAT number such as
    /// <c>SE123451234501</c> must not be run through the Romanian control-digit algorithm.
    /// </summary>
    private static bool LooksRomanian(string identifier)
    {
        var trimmed = identifier.Trim();
        if (trimmed.StartsWith("RO", StringComparison.OrdinalIgnoreCase)) return true;

        // Unprefixed and entirely numeric is treated as Romanian; any other country prefix is not.
        return trimmed.All(char.IsAsciiDigit);
    }

    private static void CheckLines(DocumentView doc, List<ValidationFinding> findings)
    {
        if (doc.Lines.Count == 0)
        {
            findings.Add(new("BR-16", "The document must have at least one line (BG-25).", Path: "Lines"));
            return;
        }

        foreach (var line in doc.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.Id))
            {
                findings.Add(new("BR-21", "Each line must have an identifier (BT-126).", Path: line.Path));
            }

            if (line.Quantity is null)
            {
                findings.Add(new("BR-22", "Each line must have a quantity (BT-129).", Path: line.Path));
            }
            else if (string.IsNullOrWhiteSpace(line.Quantity.UnitCode))
            {
                findings.Add(new("BR-23", "Each line quantity must have a unit of measure (BT-130).", Path: line.Path));
            }

            if (line.LineExtensionAmount is null)
            {
                findings.Add(new("BR-24", "Each line must have a net amount (BT-131).", Path: line.Path));
            }

            if (string.IsNullOrWhiteSpace(line.Item?.Name))
            {
                findings.Add(new("BR-25", "Each line must name the item (BT-153).", Path: line.Path));
            }

            if (line.Price?.PriceAmount is null)
            {
                findings.Add(new("BR-26", "Each line must have a net unit price (BT-146).", Path: line.Path));
            }
            else if (line.Price.PriceAmount.Value < 0)
            {
                findings.Add(new("BR-27", "The net unit price (BT-146) must not be negative.", Path: line.Path));
            }

            if (string.IsNullOrWhiteSpace(line.Item?.ClassifiedTaxCategory?.Id?.Value))
            {
                findings.Add(new("BR-CO-04", "Each line must have a VAT category code (BT-151).", Path: line.Path));
            }
        }
    }

    private static void CheckTotals(DocumentView doc, List<ValidationFinding> findings)
    {
        var totals = doc.Totals;

        if (totals.LineExtensionAmount is null)
        {
            findings.Add(new("BR-12", "The document must have a sum of line net amounts (BT-106).", Path: "Totals"));
        }

        if (totals.TaxExclusiveAmount is null)
        {
            findings.Add(new("BR-13", "The document must have a total without VAT (BT-109).", Path: "Totals"));
        }

        if (totals.TaxInclusiveAmount is null)
        {
            findings.Add(new("BR-14", "The document must have a total with VAT (BT-112).", Path: "Totals"));
        }

        if (totals.PayableAmount is null)
        {
            findings.Add(new("BR-15", "The document must have an amount due for payment (BT-115).", Path: "Totals"));
        }

        // BR-CO-10: BT-106 is the sum of the line net amounts.
        if (totals.LineExtensionAmount is { } lineTotal)
        {
            var sum = doc.Lines.Sum(l => l.LineExtensionAmount?.Value ?? 0m);
            if (Math.Abs(sum - lineTotal.Value) > Tolerance)
            {
                findings.Add(new("BR-CO-10",
                    $"The sum of line net amounts (BT-106) is {lineTotal.Value}, but the lines total {sum}.",
                    Path: "Totals.LineExtensionAmount"));
            }
        }

        // BR-CO-13: BT-109 = BT-106 - allowances + charges.
        if (totals is { LineExtensionAmount: { } lines, TaxExclusiveAmount: { } exclusive })
        {
            var allowances = doc.AllowanceCharges.Where(a => !a.ChargeIndicator).Sum(a => a.Amount.Value);
            var charges = doc.AllowanceCharges.Where(a => a.ChargeIndicator).Sum(a => a.Amount.Value);
            var expected = lines.Value - allowances + charges;

            if (Math.Abs(expected - exclusive.Value) > Tolerance)
            {
                findings.Add(new("BR-CO-13",
                    $"The total without VAT (BT-109) is {exclusive.Value}, but line total minus allowances plus charges is {expected}.",
                    Path: "Totals.TaxExclusiveAmount"));
            }
        }

        // BR-CO-15: BT-112 = BT-109 + BT-110.
        if (totals is { TaxExclusiveAmount: { } net, TaxInclusiveAmount: { } gross })
        {
            var vat = doc.TaxTotals
                .Where(t => MatchesCurrency(t.TaxAmount, doc.CurrencyCode))
                .Sum(t => t.TaxAmount.Value);
            var expected = net.Value + vat;

            if (Math.Abs(expected - gross.Value) > Tolerance)
            {
                findings.Add(new("BR-CO-15",
                    $"The total with VAT (BT-112) is {gross.Value}, but total without VAT plus VAT is {expected}.",
                    Path: "Totals.TaxInclusiveAmount"));
            }
        }

        // BR-CO-16: BT-115 = BT-112 - BT-113.
        if (totals is { TaxInclusiveAmount: { } inclusive, PayableAmount: { } payable })
        {
            var prepaid = totals.PrepaidAmount?.Value ?? 0m;
            var expected = inclusive.Value - prepaid;

            if (Math.Abs(expected - payable.Value) > Tolerance)
            {
                findings.Add(new("BR-CO-16",
                    $"The amount due (BT-115) is {payable.Value}, but total with VAT minus prepaid is {expected}.",
                    Path: "Totals.PayableAmount"));
            }
        }

        CheckTwoDecimals(totals.LineExtensionAmount, "BR-DEC-09", "BT-106", findings);
        CheckTwoDecimals(totals.TaxExclusiveAmount, "BR-DEC-11", "BT-109", findings);
        CheckTwoDecimals(totals.TaxInclusiveAmount, "BR-DEC-14", "BT-112", findings);
        CheckTwoDecimals(totals.PayableAmount, "BR-DEC-18", "BT-115", findings);
    }

    private static void CheckVatBreakdown(DocumentView doc, List<ValidationFinding> findings)
    {
        // BR-CO-18: there must be a VAT breakdown.
        var subtotals = doc.TaxTotals.SelectMany(t => t.TaxSubtotals).ToList();
        if (subtotals.Count == 0)
        {
            findings.Add(new("BR-CO-18", "The document must have at least one VAT breakdown entry (BG-23).", Path: "TaxTotals"));
            return;
        }

        foreach (var subtotal in subtotals)
        {
            var category = subtotal.TaxCategory;

            if (string.IsNullOrWhiteSpace(category?.Id?.Value))
            {
                findings.Add(new("BR-CO-17", "Each VAT breakdown entry must have a category code (BT-118).", Path: "TaxTotals"));
                continue;
            }

            // BR-S-09: for standard-rated entries, VAT = taxable amount x rate.
            if (string.Equals(category.Id.Value, "S", StringComparison.Ordinal))
            {
                if (category.Percent is not { } rate)
                {
                    findings.Add(new("BR-S-05", "A standard-rated VAT breakdown entry must have a rate (BT-119).", Path: "TaxTotals"));
                    continue;
                }

                var expected = Math.Round(subtotal.TaxableAmount.Value * rate / 100m, 2, MidpointRounding.AwayFromZero);
                if (Math.Abs(expected - subtotal.TaxAmount.Value) > Tolerance)
                {
                    findings.Add(new("BR-S-09",
                        $"VAT for the {rate}% breakdown entry is {subtotal.TaxAmount.Value}, but {subtotal.TaxableAmount.Value} at {rate}% is {expected}.",
                        Path: "TaxTotals"));
                }

                // BR-S-08: the taxable amount is the sum of line net amounts at that rate.
                var linesAtRate = doc.Lines
                    .Where(l => string.Equals(l.Item?.ClassifiedTaxCategory?.Id?.Value, "S", StringComparison.Ordinal)
                                && l.Item?.ClassifiedTaxCategory?.Percent == rate)
                    .Sum(l => l.LineExtensionAmount?.Value ?? 0m);

                var documentAdjustments = doc.AllowanceCharges
                    .Where(a => string.Equals(a.TaxCategory?.Id?.Value, "S", StringComparison.Ordinal)
                                && a.TaxCategory?.Percent == rate)
                    .Sum(a => a.ChargeIndicator ? a.Amount.Value : -a.Amount.Value);

                var expectedTaxable = linesAtRate + documentAdjustments;
                if (Math.Abs(expectedTaxable - subtotal.TaxableAmount.Value) > Tolerance)
                {
                    findings.Add(new("BR-S-08",
                        $"The taxable amount for the {rate}% entry is {subtotal.TaxableAmount.Value}, but the lines at that rate total {expectedTaxable}.",
                        Path: "TaxTotals"));
                }
            }
        }
    }

    private static void CheckPaymentTerms(DocumentView doc, List<ValidationFinding> findings)
    {
        // BR-CO-25 applies to invoices only. ANAF's validator accepts a credit note with neither a
        // due date nor payment terms, and applying the rule there produced a false reject — caught
        // by the oracle comparison, not by reading the specification.
        if (!string.Equals(doc.DocumentType, "Invoice", StringComparison.Ordinal)) return;

        // When something is payable, say when or on what terms.
        if (doc.Totals.PayableAmount is { } payable
            && payable.Value > 0
            && doc.DueDate is null
            && string.IsNullOrWhiteSpace(doc.PaymentTerms?.Note))
        {
            findings.Add(new("BR-CO-25",
                "When an amount is payable, the document must have a due date (BT-9) or payment terms (BT-20).",
                Path: "DueDate"));
        }
    }

    private static void CheckTwoDecimals(Amount? amount, string code, string term, List<ValidationFinding> findings)
    {
        if (amount is null) return;

        // Scale is the decimal's own precision, so 100.00m reports 2 and 100.000m reports 3.
        var scale = (decimal.GetBits(amount.Value)[3] >> 16) & 0xFF;
        if (scale > 2 && decimal.Round(amount.Value, 2) != amount.Value)
        {
            findings.Add(new(code, $"{term} must have at most two decimal places.", Path: term));
        }
    }

    private static bool MatchesCurrency(Amount? amount, string currency) =>
        amount is not null && string.Equals(amount.CurrencyId, currency, StringComparison.OrdinalIgnoreCase);
}
