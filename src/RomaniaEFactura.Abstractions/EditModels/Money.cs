namespace RomaniaEFactura.EditModels;

/// <summary>
/// Rounding for monetary amounts, applied consistently everywhere the library computes one.
/// </summary>
/// <remarks>
/// <para>
/// Two things make this worth naming rather than inlining. EN16931 requires document amounts to
/// carry at most two decimal places (the <c>BR-DEC-*</c> family), and its rules then check those
/// rounded figures against each other — so every derivation has to round at the same points, or
/// the totals disagree by a bani and ANAF rejects the document.
/// </para>
/// <para>
/// The mode is away-from-zero, not .NET's default. <see cref="MidpointRounding.ToEven"/> would
/// turn 0.125 into 0.12, which is not how invoices are added up anywhere in Romanian practice and
/// would put the library's arithmetic at odds with the accounting system feeding it.
/// </para>
/// </remarks>
public static class Money
{
    /// <summary>How many decimal places a monetary amount may carry.</summary>
    public const int Decimals = 2;

    /// <summary>Rounds an amount to two places, away from zero at the midpoint.</summary>
    public static decimal Round(decimal value) =>
        Math.Round(value, Decimals, MidpointRounding.AwayFromZero);

    /// <summary>Computes VAT on a taxable amount at a percentage rate.</summary>
    public static decimal Vat(decimal taxableAmount, decimal ratePercent) =>
        Round(taxableAmount * ratePercent / 100m);
}
