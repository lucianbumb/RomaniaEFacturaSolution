namespace RomaniaEFactura.Tests.Oracle;

/// <summary>
/// A test that can only run when ANAF's offline validator is available.
/// </summary>
/// <remarks>
/// Skips rather than fails when the validator is absent, so a contributor without it can still run
/// the suite. CI installs it and sets <see cref="AnafValidator.HomeVariable"/>, so the oracle
/// comparison is enforced there — the skip is a local convenience, never a way to dodge the check.
/// </remarks>
public sealed class RequiresAnafValidatorFactAttribute : FactAttribute
{
    /// <summary>Marks the test skipped when the validator cannot be found.</summary>
    public RequiresAnafValidatorFactAttribute()
    {
        if (!AnafValidator.IsAvailable)
        {
            Skip = $"ANAF validator not available. Set {AnafValidator.HomeVariable} to an unpacked "
                 + "roefacturavalidator directory to run the oracle comparison.";
        }
    }
}
