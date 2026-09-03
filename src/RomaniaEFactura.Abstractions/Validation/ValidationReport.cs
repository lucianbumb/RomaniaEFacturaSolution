namespace RomaniaEFactura.Validation;

/// <summary>How serious a validation finding is.</summary>
public enum ValidationSeverity
{
    /// <summary>The document will be rejected by ANAF.</summary>
    Error,

    /// <summary>The document will be accepted, but something is questionable.</summary>
    Warning,
}

/// <summary>
/// One thing wrong with a document, identified by its business rule where one applies.
/// </summary>
/// <param name="Code">
/// The rule identifier, such as <c>BR-CO-10</c> or <c>BR-RO-001</c>. Checks ANAF performs outside
/// the rule set — the CIF control digit among them — use a <c>RO-CIF-*</c> code of our own so that
/// every finding can be identified programmatically.
/// </param>
/// <param name="Message">What is wrong, in terms the person filling the form can act on.</param>
/// <param name="Severity">Whether this blocks submission.</param>
/// <param name="Path">Where in the document the problem is, when it can be pinpointed.</param>
public sealed record ValidationFinding(
    string Code,
    string Message,
    ValidationSeverity Severity = ValidationSeverity.Error,
    string? Path = null)
{
    /// <inheritdoc />
    public override string ToString() =>
        Path is null ? $"[{Code}] {Message}" : $"[{Code}] {Message} ({Path})";
}

/// <summary>
/// The outcome of validating a document offline.
/// </summary>
/// <remarks>
/// A report with <see cref="IsValid"/> true is the library's promise that ANAF will not reject the
/// document on format grounds. It says nothing about whether the submission will succeed —
/// authorization, connectivity and SPV rights are separate, and surface as send results.
/// </remarks>
public sealed class ValidationReport
{
    /// <summary>Creates a report from the findings produced by the rules.</summary>
    public ValidationReport(IEnumerable<ValidationFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        Findings = [.. findings];
    }

    /// <summary>A report with nothing wrong.</summary>
    public static ValidationReport Valid { get; } = new([]);

    /// <summary>Everything the rules objected to, errors and warnings alike.</summary>
    public IReadOnlyList<ValidationFinding> Findings { get; }

    /// <summary>The findings that will cause ANAF to reject the document.</summary>
    public IEnumerable<ValidationFinding> Errors =>
        Findings.Where(f => f.Severity == ValidationSeverity.Error);

    /// <summary>The findings that are worth attention but will not block submission.</summary>
    public IEnumerable<ValidationFinding> Warnings =>
        Findings.Where(f => f.Severity == ValidationSeverity.Warning);

    /// <summary>Whether the document is free of errors and can be sent.</summary>
    public bool IsValid => !Findings.Any(f => f.Severity == ValidationSeverity.Error);

    /// <summary>The distinct rule codes that produced errors.</summary>
    public IReadOnlyList<string> ErrorCodes => [.. Errors.Select(f => f.Code).Distinct()];

    /// <inheritdoc />
    public override string ToString() =>
        IsValid ? "valid" : $"invalid: {string.Join("; ", Errors.Select(f => f.Code))}";
}
