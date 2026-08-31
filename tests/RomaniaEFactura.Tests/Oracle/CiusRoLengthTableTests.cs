using System.Text.RegularExpressions;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Tests.Oracle;

/// <summary>
/// Every length in <see cref="CiusRoLengths"/> matches ANAF's own Schematron.
/// </summary>
/// <remarks>
/// <para>
/// A table of sixty numbers copied by hand from a specification is a table that drifts. This reads
/// the rules out of <c>RO16931-rules.sch</c> — the file ANAF ships inside its validator — and
/// asserts each constant against the rule it claims to come from. A limit that changes in a future
/// CIUS-RO release fails here rather than in production.
/// </para>
/// <para>
/// Runs only when the validator is available, like the other oracle tests, and CI enforces that it
/// was not skipped.
/// </para>
/// </remarks>
public partial class CiusRoLengthTableTests
{
    /// <summary>Each constant, paired with the rule that defines it.</summary>
    /// <remarks>
    /// Where a limit covers several business terms — a city is a city whether the seller's or the
    /// buyer's — one representative rule is named, and a separate test proves the others agree.
    /// </remarks>
    public static TheoryData<string, int> Expected =>
        new()
        {
            { "BR-RO-L155", CiusRoLengths.DocumentNumber },
            { "BR-RO-L156", CiusRoLengths.PrecedingDocumentNumber },
            { "BR-RO-L0302", CiusRoLengths.ContractReference },
            { "BR-RO-L0303", CiusRoLengths.OrderReference },
            { "BR-RO-L1001", CiusRoLengths.AccountingReference },
            { "BR-RO-L301", CiusRoLengths.PaymentTerms },
            { "BR-RO-L302", CiusRoLengths.Note },
            { "BR-RO-L0308", CiusRoLengths.ObjectIdentifier },
            { "BR-RO-L1020", CiusRoLengths.SupportingDocumentDescription },
            { "BR-RO-L201", CiusRoLengths.PartyName },
            { "BR-RO-L153", CiusRoLengths.AddressLine1 },
            { "BR-RO-L1012", CiusRoLengths.AddressLine2 },
            { "BR-RO-L0503", CiusRoLengths.City },
            { "BR-RO-L0203", CiusRoLengths.PostalCode },
            { "BR-RO-L206", CiusRoLengths.PartyName },
            { "BR-RO-L202", CiusRoLengths.TradingName },
            { "BR-RO-L1000", CiusRoLengths.CompanyLegalForm },
            { "BR-RO-L205", CiusRoLengths.PayeeName },
            { "BR-RO-L1004", CiusRoLengths.ContactName },
            { "BR-RO-L1005", CiusRoLengths.ContactTelephone },
            { "BR-RO-L1006", CiusRoLengths.ContactEmail },
            { "BR-RO-L151", CiusRoLengths.AddressLine1 },
            { "BR-RO-L1002", CiusRoLengths.AddressLine2 },
            { "BR-RO-L0501", CiusRoLengths.City },
            { "BR-RO-L0201", CiusRoLengths.PostalCode },
            { "BR-RO-L1016", CiusRoLengths.PaymentMeansText },
            { "BR-RO-L140", CiusRoLengths.RemittanceInformation },
            { "BR-RO-L208", CiusRoLengths.PaymentAccountName },
            { "BR-RO-L1017", CiusRoLengths.DocumentAdjustmentReason },
            { "BR-RO-L1022", CiusRoLengths.LineAdjustmentReason },
            { "BR-RO-L1019", CiusRoLengths.VatExemptionReason },
            { "BR-RO-L303", CiusRoLengths.LineNote },
            { "BR-RO-L1021", CiusRoLengths.LineAccountingReference },
            { "BR-RO-L1024", CiusRoLengths.ItemName },
            { "BR-RO-L212", CiusRoLengths.ItemDescription },
            { "BR-RO-L0505", CiusRoLengths.ItemAttributeName },
            { "BR-RO-L1025", CiusRoLengths.ItemAttributeValue },
            { "BR-RO-A020", CiusRoLengths.MaxNotes },
            { "BR-RO-A051", CiusRoLengths.MaxSupportingDocuments },
            { "BR-RO-A052", CiusRoLengths.MaxItemAttributes },
            { "BR-RO-A500", CiusRoLengths.MaxPrecedingDocuments },
        };

    [RequiresAnafValidatorTheory]
    [MemberData(nameof(Expected))]
    public void OurLimitMatchesTheRule(string ruleId, int ours)
    {
        var limits = ReadSchematronLimits();

        Assert.True(
            limits.TryGetValue(ruleId, out var theirs),
            $"Rule {ruleId} was not found in ANAF's Schematron. Either it was renamed or removed in "
            + "a newer CIUS-RO release, in which case the table needs revisiting.");

        Assert.True(
            ours == theirs,
            $"""
             {ruleId}: CiusRoLengths says {ours}, ANAF's Schematron says {theirs}.

             A limit that is too generous is the dangerous direction — the model would accept a
             value ANAF then refuses, which breaks the library's central promise.
             """);
    }

    /// <summary>
    /// Limits that one constant covers for several business terms, and every rule they span.
    /// </summary>
    /// <remarks>
    /// A city is capped the same whether it is the seller's, the buyer's or the delivery address's,
    /// so one constant serves three rules. Listing the siblings keeps a future release that changes
    /// only one of them from slipping past a table that names the other.
    /// </remarks>
    private static readonly (int Ours, string[] Rules)[] SharedLimits =
    [
        (CiusRoLengths.City, ["BR-RO-L0501", "BR-RO-L0502", "BR-RO-L0503", "BR-RO-L0504"]),
        (CiusRoLengths.PostalCode, ["BR-RO-L0201", "BR-RO-L0202", "BR-RO-L0203", "BR-RO-L0204"]),
        (CiusRoLengths.AddressLine1, ["BR-RO-L151", "BR-RO-L152", "BR-RO-L153", "BR-RO-L154"]),
        (CiusRoLengths.AddressLine2, ["BR-RO-L1002", "BR-RO-L1007", "BR-RO-L1012", "BR-RO-L1014"]),
        (CiusRoLengths.PartyName, ["BR-RO-L201", "BR-RO-L203", "BR-RO-L206"]),
        (CiusRoLengths.TradingName, ["BR-RO-L202", "BR-RO-L204"]),
        (CiusRoLengths.ContactName, ["BR-RO-L1004", "BR-RO-L1009"]),
        (CiusRoLengths.ContactTelephone, ["BR-RO-L1005", "BR-RO-L1010"]),
        (CiusRoLengths.ContactEmail, ["BR-RO-L1006", "BR-RO-L1011"]),
        (CiusRoLengths.DocumentAdjustmentReason, ["BR-RO-L1017", "BR-RO-L1018"]),
        (CiusRoLengths.LineAdjustmentReason, ["BR-RO-L1022", "BR-RO-L1023"]),
    ];

    [RequiresAnafValidatorFact]
    public void TheRulesSharingALimitReallyDoAgree()
    {
        var limits = ReadSchematronLimits();

        foreach (var (ours, rules) in SharedLimits)
        {
            AssertAllEqual(limits, ours, rules);
        }
    }

    [RequiresAnafValidatorFact]
    public void EveryLengthRuleInTheSchematronIsAccountedFor()
    {
        // Catches the opposite failure from the theory above: a rule ANAF defines that the table
        // has never heard of, and which the library therefore does not enforce.
        var limits = ReadSchematronLimits();

        var covered = Expected.Select(row => (string)row[0])
            .Concat(SharedLimits.SelectMany(shared => shared.Rules))
            .ToHashSet(StringComparer.Ordinal);

        // Deliberately not enforced, because the library cannot express the fields they cap.
        var unrepresentable = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BR-RO-L1013"] = "tax representative address line 3 (BT-164) — BG-11 is not modelled",
            ["BR-RO-L207"] = "deliver-to party name (BT-70) — the delivery party name is not modelled",
            ["BR-RO-L209"] = "payment card holder name (BT-88) — card payment is not modelled",
            ["BR-RO-L210"] = "external document location (BT-124) — attachments are not modelled",
            ["BR-RO-L211"] = "attached document filename (BT-125-2) — attachments are not modelled",
            ["BR-RO-L1003"] = "address line 3 (BT-162) — the third address line is not modelled",
            ["BR-RO-L1008"] = "address line 3 (BT-163) — the third address line is not modelled",
            ["BR-RO-L1015"] = "address line 3 (BT-165) — the third address line is not modelled",
            ["BR-RO-L0304"] = "sales order reference (BT-14) — not modelled",
            ["BR-RO-L0305"] = "receiving advice reference (BT-15) — not modelled",
            ["BR-RO-L0306"] = "despatch advice reference (BT-16) — not modelled",
            ["BR-RO-L0307"] = "tender or lot reference (BT-17) — not modelled",
        };

        var unexplained = limits.Keys
            .Where(rule => !covered.Contains(rule) && !unrepresentable.ContainsKey(rule))
            .OrderBy(rule => rule, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unexplained.Count == 0,
            $"""
             ANAF defines {unexplained.Count} length rule(s) the table neither enforces nor
             explains: {string.Join(", ", unexplained)}

             Either add the limit to CiusRoLengths, or record why the library cannot express the
             field it caps. Silence is the one option that leaves the guarantee overstated.
             """);
    }

    private static void AssertAllEqual(
        IReadOnlyDictionary<string, int> limits,
        int ours,
        params string[] ruleIds)
    {
        foreach (var ruleId in ruleIds)
        {
            Assert.True(
                limits.TryGetValue(ruleId, out var theirs) && theirs == ours,
                $"{ruleId} caps at {(limits.TryGetValue(ruleId, out var found) ? found : -1)}, "
                + $"but the library uses {ours} for every field of that kind. They have diverged, "
                + "so the shared constant is no longer correct for all of them.");
        }
    }

    private static IReadOnlyDictionary<string, int> ReadSchematronLimits()
    {
        var path = Path.Combine(
            AnafValidator.Home!, "ro16931-ubl-1.0.9", "cius-ro", "RO16931-rules.sch");

        Assert.True(File.Exists(path), $"The CIUS-RO Schematron was not found at {path}.");

        var text = File.ReadAllText(path);
        var limits = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (Match match in LengthRule().Matches(text))
        {
            limits[match.Groups["id"].Value] = int.Parse(
                match.Groups["limit"].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        Assert.True(limits.Count > 50, $"Only {limits.Count} length rules were parsed; the Schematron format has changed.");

        return limits;
    }

    /// <summary>
    /// Matches a rule id and the number in its English message.
    /// </summary>
    /// <remarks>
    /// The message is parsed rather than the XPath test, because the tests use several shapes —
    /// <c>string-length(...) &lt;= 200</c> among them — while every one of these rules states its
    /// limit in the sentence as "... is N.".
    /// </remarks>
    [GeneratedRegex(
        """id="(?<id>BR-RO-(?:L|A)[A-Z0-9]+)"\s*>[^<]*?(?:allowed maximum number of (?:characters|occurences)[^<]*?is\s*(?<limit>\d+))""",
        RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex LengthRule();
}
