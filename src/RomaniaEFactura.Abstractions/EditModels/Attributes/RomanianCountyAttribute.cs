using System.ComponentModel.DataAnnotations;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.EditModels.Attributes;

/// <summary>
/// Requires an ISO 3166-2:RO county code, which CIUS-RO mandates for Romanian addresses (BT-39).
/// </summary>
/// <remarks>
/// Applied unconditionally rather than only when the country is Romania, because the field is
/// declared on <see cref="AddressEditModel.County"/>, which exists for exactly that purpose; a
/// foreign address leaves it empty and states its subdivision in
/// <see cref="AddressEditModel.Region"/> instead.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class RomanianCountyAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is null) return true;
        if (value is not string text) return false;
        if (string.IsNullOrWhiteSpace(text)) return true;

        return RomanianCounties.IsValid(text);
    }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name) =>
        ErrorMessage
        ?? $"{name} must be an ISO 3166-2:RO county code such as RO-B or RO-CJ, not a county name.";
}
