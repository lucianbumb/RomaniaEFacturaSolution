using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Tests;

/// <summary>
/// The Romanian CIF control-digit check, which ANAF enforces outside the CIUS-RO rule set.
/// </summary>
public class RomanianCifTests
{
    [Theory]
    [InlineData("31108356")]   // a real, in-use CIF
    [InlineData("RO31108356")] // same, with the country prefix
    [InlineData("8000000000")] // the example CIF from ANAF's own OpenAPI specs
    [InlineData("12345674")]
    [InlineData("23456783")]
    [InlineData("18016")]      // short CIFs are left-padded against the weights
    public void IsValid_AcceptsCorrectControlDigit(string cif) =>
        Assert.True(RomanianCif.IsValid(cif), $"{cif} should be valid");

    [Theory]
    [InlineData("31108357")]   // last digit altered
    [InlineData("RO23456784")]
    [InlineData("12345675")]
    public void IsValid_RejectsIncorrectControlDigit(string cif) =>
        Assert.False(RomanianCif.IsValid(cif), $"{cif} should be rejected");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]                // too short
    [InlineData("123456789012")]     // too long
    [InlineData("SE123451234501")]   // a foreign VAT number is not a Romanian CIF
    [InlineData("RO12345A7")]
    public void IsValid_RejectsMalformedInput(string? cif) =>
        Assert.False(RomanianCif.IsValid(cif));

    [Theory]
    [InlineData("RO31108356", "31108356")]
    [InlineData("ro31108356", "31108356")]
    [InlineData("  RO 3110 8356 ", "31108356")]
    [InlineData("31108356", "31108356")]
    [InlineData(null, "")]
    public void Normalize_StripsPrefixAndWhitespace(string? input, string expected) =>
        Assert.Equal(expected, RomanianCif.Normalize(input));
}
