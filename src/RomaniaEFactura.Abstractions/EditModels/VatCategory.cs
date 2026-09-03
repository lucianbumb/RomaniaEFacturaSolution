namespace RomaniaEFactura.EditModels;

/// <summary>
/// How VAT applies to a line or an adjustment (BT-151 / BT-118).
/// </summary>
/// <remarks>
/// EN16931 draws these from UNCL5305, a code list with far more entries than e-Factura accepts.
/// Typing the field as an enumeration rather than a string removes a whole class of rejection:
/// a mistyped code cannot reach ANAF, because it cannot be written down.
/// </remarks>
public enum VatCategory
{
    /// <summary>Standard rate (<c>S</c>) — the ordinary case, currently 21%, 11% or 5% in Romania.</summary>
    StandardRate = 0,

    /// <summary>Zero rated (<c>Z</c>). VAT applies at 0%, and no reason is required.</summary>
    ZeroRated,

    /// <summary>Exempt from VAT (<c>E</c>). A reason must be stated.</summary>
    Exempt,

    /// <summary>Reverse charge (<c>AE</c>) — taxare inversă. The buyer accounts for the VAT.</summary>
    ReverseCharge,

    /// <summary>Intra-community supply (<c>K</c>). Goods or services to another member state.</summary>
    IntraCommunitySupply,

    /// <summary>Export outside the European Union (<c>G</c>).</summary>
    Export,

    /// <summary>Outside the scope of VAT (<c>O</c>). Carries no rate at all.</summary>
    OutsideScope,
}

/// <summary>What each VAT category demands of the document.</summary>
/// <remarks>
/// The same table the validator enforces, expressed once so the edit model can ask the questions
/// before the document is built rather than reporting them afterwards.
/// </remarks>
public static class VatCategoryInfo
{
    /// <summary>The UNCL5305 code EN16931 uses for a category.</summary>
    public static string ToCode(this VatCategory category) => category switch
    {
        VatCategory.StandardRate => "S",
        VatCategory.ZeroRated => "Z",
        VatCategory.Exempt => "E",
        VatCategory.ReverseCharge => "AE",
        VatCategory.IntraCommunitySupply => "K",
        VatCategory.Export => "G",
        VatCategory.OutsideScope => "O",
        _ => throw new ArgumentOutOfRangeException(nameof(category)),
    };

    /// <summary>Whether the category requires a rate above zero.</summary>
    public static bool RequiresPositiveRate(this VatCategory category) =>
        category == VatCategory.StandardRate;

    /// <summary>
    /// Whether the category must carry no rate at all, as distinct from a rate of zero.
    /// </summary>
    /// <remarks>
    /// Only <see cref="VatCategory.OutsideScope"/>. The distinction is real: BR-O-08 rejects a
    /// document that states 0% where the correct statement is that VAT does not apply.
    /// </remarks>
    public static bool RateMustBeAbsent(this VatCategory category) =>
        category == VatCategory.OutsideScope;

    /// <summary>
    /// Whether the document must say why no VAT is charged (BT-120 or BT-121).
    /// </summary>
    /// <remarks>
    /// Zero-rated is deliberately excluded: VAT does apply, at 0%, so there is nothing to excuse.
    /// </remarks>
    public static bool RequiresExemptionReason(this VatCategory category) =>
        category is VatCategory.Exempt
            or VatCategory.ReverseCharge
            or VatCategory.IntraCommunitySupply
            or VatCategory.Export
            or VatCategory.OutsideScope;

    /// <summary>
    /// The rate that will be written for this category, given what the caller asked for.
    /// </summary>
    /// <remarks>
    /// Every category except <see cref="VatCategory.StandardRate"/> is fixed by the code itself,
    /// so the value is derived rather than trusted — a stray rate on an exempt line becomes
    /// impossible instead of merely invalid.
    /// </remarks>
    public static decimal? EffectiveRate(this VatCategory category, decimal? requested) =>
        category switch
        {
            VatCategory.StandardRate => requested,
            VatCategory.OutsideScope => null,
            _ => 0m,
        };
}
