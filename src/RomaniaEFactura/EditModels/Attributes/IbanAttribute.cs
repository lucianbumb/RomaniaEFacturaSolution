using System.ComponentModel.DataAnnotations;
using System.Numerics;

namespace RomaniaEFactura.EditModels.Attributes;

/// <summary>
/// Requires a structurally valid IBAN, checked by the ISO 13616 mod-97 rule.
/// </summary>
/// <remarks>
/// BT-84 is what the buyer's bank will pay into. A transposed pair of digits produces an IBAN that
/// looks right, passes ANAF's format rules, and sends the money nowhere — the check digits exist
/// precisely to catch that, so it is worth applying them rather than only checking the length.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class IbanAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null) return true;
        if (value is not string text) return false;
        if (string.IsNullOrWhiteSpace(text)) return true;

        return IsWellFormed(text);
    }

    /// <summary>Whether the value passes the ISO 13616 check.</summary>
    public static bool IsWellFormed(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban)) return false;

        var compact = iban.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (compact.Length is < 15 or > 34) return false;
        if (!char.IsAsciiLetterUpper(compact[0]) || !char.IsAsciiLetterUpper(compact[1])) return false;
        if (!char.IsAsciiDigit(compact[2]) || !char.IsAsciiDigit(compact[3])) return false;

        foreach (var c in compact)
        {
            if (!char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c)) return false;
        }

        // The first four characters move to the end, then each letter becomes its position in the
        // alphabet plus nine, and the resulting number must leave a remainder of one modulo 97.
        var rearranged = string.Concat(compact.AsSpan(4), compact.AsSpan(0, 4));
        var digits = new System.Text.StringBuilder(rearranged.Length * 2);
        foreach (var c in rearranged)
        {
            if (char.IsAsciiDigit(c)) digits.Append(c);
            else digits.Append(c - 'A' + 10);
        }

        return BigInteger.Parse(digits.ToString(), System.Globalization.CultureInfo.InvariantCulture)
            % 97 == 1;
    }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name) =>
        ErrorMessage ?? $"{name} is not a valid IBAN — the check digits do not match.";
}
