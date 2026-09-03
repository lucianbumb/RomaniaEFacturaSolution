namespace RomaniaEFactura.Validation;

/// <summary>
/// The Romanian fiscal identification code (CIF/CUI), and its control-digit check.
/// </summary>
/// <remarks>
/// ANAF's validator rejects an invoice whose buyer or seller CIF fails this check, and does so
/// outside the CIUS-RO Schematron — so implementing the rules alone would let an invoice through
/// that ANAF then refuses. Verified against a known-valid CIF, 31108356.
/// </remarks>
public static class RomanianCif
{
    // Positional weights defined by the Ministry of Finance for the control digit.
    private static ReadOnlySpan<byte> Weights => [7, 5, 3, 2, 1, 7, 5, 3, 2];

    /// <summary>The longest a CIF can be, excluding any country prefix.</summary>
    private const int MaxDigits = 10;

    /// <summary>
    /// Removes an <c>RO</c> country prefix and surrounding whitespace, leaving the numeric CIF.
    /// </summary>
    /// <remarks>
    /// ANAF's API rejects the prefixed form, so every call that sends a CIF must normalise first.
    /// </remarks>
    public static string Normalize(string? cif)
    {
        if (string.IsNullOrWhiteSpace(cif)) return string.Empty;

        var trimmed = cif.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        return trimmed.StartsWith("RO", StringComparison.OrdinalIgnoreCase)
            ? trimmed[2..]
            : trimmed;
    }

    /// <summary>
    /// Whether the value is a well-formed Romanian CIF with a correct control digit.
    /// </summary>
    /// <param name="cif">The CIF, with or without an <c>RO</c> prefix.</param>
    public static bool IsValid(string? cif)
    {
        var digits = Normalize(cif);

        if (digits.Length is < 2 or > MaxDigits) return false;

        foreach (var c in digits)
        {
            if (!char.IsAsciiDigit(c)) return false;
        }

        var body = digits[..^1];
        var control = digits[^1] - '0';

        // The body is right-aligned against the weights, so shorter CIFs are padded on the left.
        var offset = Weights.Length - body.Length;
        var sum = 0;
        for (var i = 0; i < body.Length; i++)
        {
            sum += (body[i] - '0') * Weights[offset + i];
        }

        var expected = sum * 10 % 11;
        if (expected == 10) expected = 0;

        return expected == control;
    }
}
