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
        CheckPeriods(doc, findings);

        // The BR-RO-L* and BR-RO-A* families, applied here so a document built directly as UBL is
        // held to the same limits the edit models enforce through their attributes.
        CiusRoLengthRules.Check(doc, findings);

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
        else if (!doc.Id.Any(char.IsAsciiDigit))
        {
            // BR-RO-010. Romania adds this to EN16931: a purely alphabetic number is refused.
            findings.Add(new("BR-RO-010",
                $"The document number '{doc.Id}' must contain at least one digit (BT-1).",
                Path: "Id"));
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

        // BR-RO-201/202/211/212 apply the same subdivision and sector rules to where the goods
        // went, which is a separate address and is missed easily because it is optional.
        var deliveryAddress = doc.Delivery?.DeliveryLocation?.Address;
        CheckRomanianAddress(deliveryAddress, "Delivery", findings);

        // BR-RO-210 goes further than the seller and buyer rules: a delivery address must name a
        // subdivision whatever its country, not only when that country is Romania.
        if (deliveryAddress is not null && string.IsNullOrWhiteSpace(deliveryAddress.CountrySubentity))
        {
            findings.Add(new("BR-RO-210",
                "A delivery address must state its country subdivision (BT-79).",
                Path: "Delivery.Address.CountrySubentity"));
        }
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

        CheckRomanianAddress(party.PostalAddress, role, findings);

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

        // BR-CO-09: a VAT identifier must carry its country prefix, so a Romanian VAT-registered
        // party is RO12345674 rather than a bare 12345674. This applies to BT-31/BT-48 only; the
        // legal registration identifier (BT-30/BT-47) is unprefixed.
        foreach (var scheme in party.PartyTaxSchemes)
        {
            var vatId = scheme.CompanyId?.Value;
            if (string.IsNullOrWhiteSpace(vatId)) continue;

            if (vatId.Length < 2 || !char.IsAsciiLetter(vatId[0]) || !char.IsAsciiLetter(vatId[1]))
            {
                findings.Add(new("BR-CO-09",
                    $"The {role.ToLowerInvariant()} VAT identifier '{vatId}' must start with a country code, for example RO{vatId}.",
                    Path: $"{role}.PartyTaxScheme"));
            }
        }
    }

    /// <summary>
    /// The subdivision and city rules CIUS-RO adds for Romanian addresses.
    /// </summary>
    /// <remarks>
    /// Shared between the seller, the buyer and the delivery address because the Schematron states
    /// the same three rules three times over, once per role — BR-RO-090/100/110 for the seller,
    /// BR-RO-092/101/111 for the buyer, BR-RO-201/202/211/212 for the delivery address.
    /// </remarks>
    private static void CheckRomanianAddress(
        PostalAddress? address,
        string role,
        List<ValidationFinding> findings)
    {
        if (address is null) return;
        if (!string.Equals(address.Country?.IdentificationCode, "RO", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var subdivision = address.CountrySubentity?.Trim();
        var noun = role.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(subdivision))
        {
            findings.Add(new("BR-RO-090",
                $"A Romanian {noun} address must have a county code such as RO-B or RO-CJ (BT-39).",
                Path: $"{role}.PostalAddress.CountrySubentity"));
            return;
        }

        if (!RomanianCounties.IsValid(subdivision))
        {
            findings.Add(new("BR-RO-110",
                $"'{subdivision}' is not an ISO 3166-2:RO county code (BT-39). "
                + "ANAF requires a code such as RO-B or RO-CJ, not a county name.",
                Path: $"{role}.PostalAddress.CountrySubentity"));
            return;
        }

        // BR-RO-100. Bucharest is the one county whose city is a code rather than a name: ANAF
        // rejects "Bucuresti" and expects the sector. Nothing signals this in the address itself,
        // so an otherwise perfect invoice from a Bucharest company is refused without it.
        if (string.Equals(subdivision, "RO-B", StringComparison.Ordinal)
            && !RomanianCounties.IsBucharestSector(address.CityName))
        {
            findings.Add(new("BR-RO-100",
                $"A {noun} in Bucharest (RO-B) must state the city as a sector code — "
                + $"{string.Join(", ", RomanianCounties.BucharestSectors)} — not '{address.CityName}' (BT-37).",
                Path: $"{role}.PostalAddress.CityName"));
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

    /// <summary>
    /// The VAT categories EN16931 defines, and the rule-code family each one uses.
    /// </summary>
    /// <remarks>
    /// The rules follow one shape per category: <c>-08</c> checks the taxable amount against the
    /// lines, <c>-09</c> checks the VAT amount against the rate, and <c>-10</c> requires an
    /// exemption reason where no VAT is charged. Encoding that as a table keeps the seven
    /// categories from becoming seven near-identical blocks. Reverse charge (<c>AE</c>) and exempt
    /// (<c>E</c>) matter especially in Romania — taxare inversă is routine.
    /// </remarks>
    private static readonly Dictionary<string, VatCategoryRules> VatCategories = new(StringComparer.Ordinal)
    {
        ["S"] = new("BR-S", RequiresPositiveRate: true, RequiresExemptionReason: false),
        ["Z"] = new("BR-Z", RequiresPositiveRate: false, RequiresExemptionReason: false),
        ["E"] = new("BR-E", RequiresPositiveRate: false, RequiresExemptionReason: true),
        ["AE"] = new("BR-AE", RequiresPositiveRate: false, RequiresExemptionReason: true),
        ["K"] = new("BR-IC", RequiresPositiveRate: false, RequiresExemptionReason: true),
        ["G"] = new("BR-G", RequiresPositiveRate: false, RequiresExemptionReason: true),
        ["O"] = new("BR-O", RequiresPositiveRate: false, RequiresExemptionReason: true, RateMustBeAbsent: true),
    };

    private sealed record VatCategoryRules(
        string Family,
        bool RequiresPositiveRate,
        bool RequiresExemptionReason,
        bool RateMustBeAbsent = false);

    private static void CheckVatBreakdown(DocumentView doc, List<ValidationFinding> findings)
    {
        // BR-CO-18: there must be a VAT breakdown.
        var subtotals = doc.TaxTotals.SelectMany(t => t.TaxSubtotals).ToList();
        if (subtotals.Count == 0)
        {
            findings.Add(new("BR-CO-18", "The document must have at least one VAT breakdown entry (BG-23).", Path: "TaxTotals"));
            return;
        }

        // BR-CO-14: the total VAT (BT-110) is the sum of the category VAT amounts (BT-117).
        foreach (var taxTotal in doc.TaxTotals.Where(t => MatchesCurrency(t.TaxAmount, doc.CurrencyCode)))
        {
            var sum = taxTotal.TaxSubtotals.Sum(s => s.TaxAmount.Value);
            if (Math.Abs(sum - taxTotal.TaxAmount.Value) > Tolerance)
            {
                findings.Add(new("BR-CO-14",
                    $"The total VAT (BT-110) is {taxTotal.TaxAmount.Value}, but the breakdown entries total {sum}.",
                    Path: "TaxTotals.TaxAmount"));
            }
        }

        foreach (var subtotal in subtotals)
        {
            var code = subtotal.TaxCategory?.Id?.Value;

            if (string.IsNullOrWhiteSpace(code))
            {
                findings.Add(new("BR-CO-17", "Each VAT breakdown entry must have a category code (BT-118).", Path: "TaxTotals"));
                continue;
            }

            if (!VatCategories.TryGetValue(code, out var rules))
            {
                findings.Add(new("BR-CO-17",
                    $"'{code}' is not a VAT category code EN16931 recognises (BT-118). Expected one of {string.Join(", ", VatCategories.Keys)}.",
                    Path: "TaxTotals"));
                continue;
            }

            CheckVatSubtotal(doc, subtotal, code, rules, findings);
        }
    }

    private static void CheckVatSubtotal(
        DocumentView doc,
        TaxSubtotal subtotal,
        string code,
        VatCategoryRules rules,
        List<ValidationFinding> findings)
    {
        var rate = subtotal.TaxCategory?.Percent;

        if (rules.RateMustBeAbsent)
        {
            if (rate is not null)
            {
                findings.Add(new($"{rules.Family}-08",
                    $"A '{code}' breakdown entry must not carry a VAT rate (BT-119).",
                    Path: "TaxTotals"));
            }
        }
        else if (rate is null)
        {
            findings.Add(new($"{rules.Family}-05",
                $"A '{code}' breakdown entry must have a VAT rate (BT-119).",
                Path: "TaxTotals"));
            return;
        }
        else if (rules.RequiresPositiveRate && rate <= 0)
        {
            findings.Add(new($"{rules.Family}-05",
                $"A '{code}' breakdown entry must have a VAT rate greater than zero (BT-119).",
                Path: "TaxTotals"));
        }
        else if (!rules.RequiresPositiveRate && rate != 0)
        {
            findings.Add(new($"{rules.Family}-05",
                $"A '{code}' breakdown entry must have a VAT rate of zero (BT-119), but it is {rate}.",
                Path: "TaxTotals"));
        }

        // -09: the VAT amount follows from the taxable amount and the rate.
        var effectiveRate = rate ?? 0m;
        var expectedVat = Math.Round(
            subtotal.TaxableAmount.Value * effectiveRate / 100m, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(expectedVat - subtotal.TaxAmount.Value) > Tolerance)
        {
            findings.Add(new($"{rules.Family}-09",
                $"VAT for the '{code}' entry is {subtotal.TaxAmount.Value}, but {subtotal.TaxableAmount.Value} at {effectiveRate}% is {expectedVat}.",
                Path: "TaxTotals"));
        }

        // -10: where no VAT is charged, the document must say why.
        if (rules.RequiresExemptionReason
            && string.IsNullOrWhiteSpace(subtotal.TaxCategory?.TaxExemptionReason)
            && string.IsNullOrWhiteSpace(subtotal.TaxCategory?.TaxExemptionReasonCode))
        {
            findings.Add(new($"{rules.Family}-10",
                $"A '{code}' breakdown entry must state an exemption reason (BT-120) or reason code (BT-121).",
                Path: "TaxTotals"));
        }

        // -08: the taxable amount is the sum of the line net amounts in that category, adjusted by
        // any document-level allowances and charges assigned to it.
        var linesInCategory = doc.Lines
            .Where(l => string.Equals(l.Item?.ClassifiedTaxCategory?.Id?.Value, code, StringComparison.Ordinal)
                        && l.Item?.ClassifiedTaxCategory?.Percent == rate)
            .Sum(l => l.LineExtensionAmount?.Value ?? 0m);

        var adjustments = doc.AllowanceCharges
            .Where(a => string.Equals(a.TaxCategory?.Id?.Value, code, StringComparison.Ordinal)
                        && a.TaxCategory?.Percent == rate)
            .Sum(a => a.ChargeIndicator ? a.Amount.Value : -a.Amount.Value);

        var expectedTaxable = linesInCategory + adjustments;
        if (Math.Abs(expectedTaxable - subtotal.TaxableAmount.Value) > Tolerance)
        {
            findings.Add(new($"{rules.Family}-08",
                $"The taxable amount for the '{code}' entry is {subtotal.TaxableAmount.Value}, but the lines in that category total {expectedTaxable}.",
                Path: "TaxTotals"));
        }

        // BR-IC-11 / BR-IC-12: an intra-community supply is zero-rated only if the goods can be
        // shown to have left the country, so the document must say when and where they went.
        if (string.Equals(code, "K", StringComparison.Ordinal))
        {
            var hasPeriod = doc.InvoicePeriod?.StartDate is not null
                            || doc.InvoicePeriod?.EndDate is not null;

            if (doc.Delivery?.ActualDeliveryDate is null && !hasPeriod)
            {
                findings.Add(new("BR-IC-11",
                    "An intra-community supply must state the delivery date (BT-72) or the "
                    + "invoicing period (BG-14).",
                    Path: "Delivery"));
            }

            if (string.IsNullOrWhiteSpace(
                    doc.Delivery?.DeliveryLocation?.Address?.Country?.IdentificationCode))
            {
                findings.Add(new("BR-IC-12",
                    "An intra-community supply must state the country the goods were delivered to (BT-80).",
                    Path: "Delivery"));
            }
        }

        // BR-AE-02 / BR-AE-03: reverse charge requires both parties to be VAT-identified, since
        // the liability moves to the buyer.
        if (string.Equals(code, "AE", StringComparison.Ordinal))
        {
            if (!HasVatIdentifier(doc.Seller))
            {
                findings.Add(new("BR-AE-02",
                    "Reverse charge requires the seller to have a VAT identifier (BT-31).", Path: "Seller"));
            }

            if (!HasVatIdentifier(doc.Buyer))
            {
                findings.Add(new("BR-AE-03",
                    "Reverse charge requires the buyer to have a VAT identifier (BT-48).", Path: "Buyer"));
            }
        }
    }

    private static bool HasVatIdentifier(Party party) =>
        party.PartyTaxSchemes.Any(s => !string.IsNullOrWhiteSpace(s.CompanyId?.Value));

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

    private static void CheckPeriods(DocumentView doc, List<ValidationFinding> findings)
    {
        CheckPeriod(doc.InvoicePeriod, "InvoicePeriod", findings);

        foreach (var line in doc.Lines)
        {
            CheckPeriod(line.InvoicePeriod, $"{line.Path}.InvoicePeriod", findings);
        }
    }

    private static void CheckPeriod(Period? period, string path, List<ValidationFinding> findings)
    {
        if (period is null) return;

        // BR-CO-19: a period that is present must say when it starts, ends, or both.
        if (period.StartDate is null && period.EndDate is null)
        {
            findings.Add(new("BR-CO-19",
                "An invoicing period must have a start date (BT-73) or an end date (BT-74).", Path: path));
            return;
        }

        // BR-29: the end cannot precede the start.
        if (period is { StartDate: { } start, EndDate: { } end } && end < start)
        {
            findings.Add(new("BR-29",
                $"The invoicing period ends on {end:yyyy-MM-dd}, before it starts on {start:yyyy-MM-dd}.",
                Path: path));
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
