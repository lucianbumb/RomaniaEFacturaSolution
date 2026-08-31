using System.Collections.Frozen;

namespace RomaniaEFactura.Validation;

/// <summary>
/// The ISO 3166-2:RO county codes CIUS-RO requires in BT-39 for Romanian addresses.
/// </summary>
/// <remarks>
/// Romania narrows EN16931 here: where the European rule leaves the country subdivision as free
/// text, CIUS-RO demands a code from this list, and ANAF rejects anything else — a plain county
/// name such as "Cluj" is refused where <c>RO-CJ</c> is accepted. Bucharest is <c>RO-B</c>, not
/// <c>RO-BU</c>, which is Buzău; that single pair is a common and expensive mistake.
/// </remarks>
public static class RomanianCounties
{
    private static readonly FrozenDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["RO-AB"] = "Alba",
        ["RO-AG"] = "Argeș",
        ["RO-AR"] = "Arad",
        ["RO-B"] = "București",
        ["RO-BC"] = "Bacău",
        ["RO-BH"] = "Bihor",
        ["RO-BN"] = "Bistrița-Năsăud",
        ["RO-BR"] = "Brăila",
        ["RO-BT"] = "Botoșani",
        ["RO-BV"] = "Brașov",
        ["RO-BZ"] = "Buzău",
        ["RO-CJ"] = "Cluj",
        ["RO-CL"] = "Călărași",
        ["RO-CS"] = "Caraș-Severin",
        ["RO-CT"] = "Constanța",
        ["RO-CV"] = "Covasna",
        ["RO-DB"] = "Dâmbovița",
        ["RO-DJ"] = "Dolj",
        ["RO-GJ"] = "Gorj",
        ["RO-GL"] = "Galați",
        ["RO-GR"] = "Giurgiu",
        ["RO-HD"] = "Hunedoara",
        ["RO-HR"] = "Harghita",
        ["RO-IF"] = "Ilfov",
        ["RO-IL"] = "Ialomița",
        ["RO-IS"] = "Iași",
        ["RO-MH"] = "Mehedinți",
        ["RO-MM"] = "Maramureș",
        ["RO-MS"] = "Mureș",
        ["RO-NT"] = "Neamț",
        ["RO-OT"] = "Olt",
        ["RO-PH"] = "Prahova",
        ["RO-SB"] = "Sibiu",
        ["RO-SJ"] = "Sălaj",
        ["RO-SM"] = "Satu Mare",
        ["RO-SV"] = "Suceava",
        ["RO-TL"] = "Tulcea",
        ["RO-TM"] = "Timiș",
        ["RO-TR"] = "Teleorman",
        ["RO-VL"] = "Vâlcea",
        ["RO-VN"] = "Vrancea",
        ["RO-VS"] = "Vaslui",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Every valid code, in alphabetical order — suitable for a dropdown.</summary>
    public static IReadOnlyList<string> Codes { get; } = [.. Names.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Every code paired with its county name.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> All { get; } =
        [.. Names.OrderBy(pair => pair.Value, StringComparer.Ordinal)];

    /// <summary>Whether the value is a code CIUS-RO accepts.</summary>
    public static bool IsValid(string? code) =>
        !string.IsNullOrWhiteSpace(code) && Names.ContainsKey(code.Trim());

    /// <summary>The county name for a code, or null when the code is unknown.</summary>
    public static string? NameOf(string? code) =>
        code is not null && Names.TryGetValue(code.Trim(), out var name) ? name : null;

    /// <summary>
    /// The sector codes Bucharest uses in place of a city name (BT-37).
    /// </summary>
    /// <remarks>
    /// Rule BR-RO-100 is the trap here. Every other Romanian address states its city by name, but
    /// an address in Bucharest — county <c>RO-B</c> — must state a sector code instead, and ANAF
    /// refuses <c>Bucuresti</c> outright. Nothing in the address hints at the exception, so an
    /// otherwise faultless invoice from a Bucharest company is rejected until the city reads
    /// <c>SECTOR1</c>.
    /// </remarks>
    public static IReadOnlyList<string> BucharestSectors { get; } =
        ["SECTOR1", "SECTOR2", "SECTOR3", "SECTOR4", "SECTOR5", "SECTOR6"];

    /// <summary>Whether a city value is one of Bucharest's sector codes.</summary>
    public static bool IsBucharestSector(string? city) =>
        city is not null && BucharestSectors.Contains(city.Trim(), StringComparer.Ordinal);
}
