using System.ComponentModel.DataAnnotations;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.EditModels.Attributes;

/// <summary>
/// Requires a well-formed Romanian fiscal code, control digit included.
/// </summary>
/// <remarks>
/// <para>
/// ANAF checks the control digit and rejects the document when it fails. An <c>RO</c> prefix is
/// accepted here and stripped during mapping; ANAF's API refuses the prefixed form.
/// </para>
/// <para>
/// Offered for an application's own models — a customer record with a CIF field — rather than used
/// on <see cref="PartyEditModel.TaxId"/>, where the check has to depend on the party's country: a
/// foreign buyer's tax number is not a CIF, and judging one by this algorithm would reject every
/// export invoice. Apply it only where the value is known to be Romanian.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class RomanianCifAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        // Whether the value is required at all is a separate question, answered by [Required].
        if (value is null) return true;
        if (value is not string text) return false;
        if (string.IsNullOrWhiteSpace(text)) return true;

        return RomanianCif.IsValid(text);
    }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name) =>
        ErrorMessage ?? $"{name} is not a valid Romanian fiscal code — check the digits.";
}
