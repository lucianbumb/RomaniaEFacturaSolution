using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.EditModels;

/// <summary>
/// Checks an edit model, then checks the document it produces.
/// </summary>
/// <remarks>
/// <para>
/// Two stages, in that order and not merged. The first reports problems in the caller's own terms
/// — a missing city, a mistyped IBAN — against property paths a form can highlight. The second
/// runs the full CIUS-RO engine over the mapped UBL, and exists as a backstop rather than as a
/// step callers should expect to fail: if the model's own rules are right, it finds nothing. A
/// finding from the second stage on a model that passed the first is a defect in this library, not
/// in the caller's data, which is precisely why it is worth running.
/// </para>
/// <para>
/// The first stage recurses through nested models and collections itself.
/// <see cref="Validator"/> validates one object and stops, and Blazor's
/// <c>DataAnnotationsValidator</c> does the same — so a library that only called
/// <see cref="Validator.TryValidateObject(object, ValidationContext, ICollection{ValidationResult}, bool)"/>
/// would silently ignore every rule on every line. ASP.NET Core MVC's model binder does recurse,
/// which makes the gap easy to miss until a Blazor page hits it.
/// </para>
/// </remarks>
public static partial class EditModelValidator
{
    /// <summary>Checks an invoice and the UBL it maps to.</summary>
    public static ValidationReport Validate(InvoiceEditModel invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var findings = ValidateModel(invoice);
        return findings.Count > 0
            ? new ValidationReport(findings)
            : CiusRoValidator.Validate(invoice.ToUbl());
    }

    /// <summary>Checks a credit note and the UBL it maps to.</summary>
    public static ValidationReport Validate(CreditNoteEditModel creditNote)
    {
        ArgumentNullException.ThrowIfNull(creditNote);

        var findings = ValidateModel(creditNote);
        return findings.Count > 0
            ? new ValidationReport(findings)
            : CiusRoValidator.Validate(creditNote.ToUbl());
    }

    /// <summary>Checks a buyer message.</summary>
    /// <remarks>
    /// There is no second stage: a buyer message is not UBL and carries none of the EN16931 rules.
    /// </remarks>
    public static ValidationReport Validate(BuyerMessageEditModel message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return new ValidationReport(ValidateModel(message));
    }

    /// <summary>
    /// Runs DataAnnotations over a model and everything nested inside it.
    /// </summary>
    /// <param name="model">The object to check.</param>
    /// <returns>One finding per rule the model breaks, with a path into the object graph.</returns>
    public static IReadOnlyList<ValidationFinding> ValidateModel(object model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var findings = new List<ValidationFinding>();
        // Guards against a model that refers back to itself. Nothing here does today, but a caller
        // subclassing one of these types could, and a stack overflow is a poor way to find out.
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Walk(model, path: null, findings, visited);
        return findings;
    }

    private static void Walk(
        object model,
        string? path,
        List<ValidationFinding> findings,
        HashSet<object> visited)
    {
        if (!visited.Add(model)) return;

        var results = new List<ValidationResult>();
        var context = new ValidationContext(model);

        // validateAllProperties: true is what makes anything beyond [Required] run at all.
        Validator.TryValidateObject(model, context, results, validateAllProperties: true);

        foreach (var result in results)
        {
            findings.Add(ToFinding(result, path));
        }

        foreach (var property in model.GetType().GetProperties())
        {
            if (property.GetIndexParameters().Length > 0) continue;
            if (!property.CanRead) continue;
            if (!IsWorthWalking(property.PropertyType)) continue;

            object? value;
            try
            {
                value = property.GetValue(model);
            }
            catch (TargetInvocationException)
            {
                // A derived property that throws on partly-filled data is not a validation
                // finding; the rules that matter will report the missing data themselves.
                continue;
            }

            if (value is null) continue;

            var childPath = Join(path, property.Name);

            if (value is IEnumerable items and not string)
            {
                var index = 0;
                foreach (var item in items)
                {
                    if (item is not null && IsWorthWalking(item.GetType()))
                    {
                        Walk(item, $"{childPath}[{index}]", findings, visited);
                    }

                    index++;
                }
            }
            else
            {
                Walk(value, childPath, findings, visited);
            }
        }
    }

    /// <summary>
    /// Whether a type can carry validation rules worth recursing into.
    /// </summary>
    /// <remarks>
    /// Restricted to this library's own models rather than "any class": walking arbitrary types
    /// would follow references into framework objects and cost far more than it finds.
    /// </remarks>
    private static bool IsWorthWalking(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
        {
            return false;
        }

        if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            var element = type.IsGenericType ? type.GetGenericArguments().FirstOrDefault() : null;
            return element is not null && IsOwnModel(element);
        }

        return IsOwnModel(type);
    }

    private static bool IsOwnModel(Type type) =>
        type.Namespace?.StartsWith("RomaniaEFactura.EditModels", StringComparison.Ordinal) == true
        && !type.IsEnum;

    private static ValidationFinding ToFinding(ValidationResult result, string? parentPath)
    {
        var member = result.MemberNames.FirstOrDefault();
        var path = member is null ? parentPath : Join(parentPath, member);
        var message = result.ErrorMessage ?? "This value is not valid.";

        // Where a message names the rule it enforces, the finding carries that code, so a caller
        // can branch on BR-CO-25 whether it was caught here or by the CIUS-RO engine.
        var match = RuleCode().Match(message);
        var code = match.Success ? match.Groups[1].Value : "RO-EDIT";

        return new ValidationFinding(code, message, ValidationSeverity.Error, path);
    }

    private static string Join(string? parent, string member) =>
        string.IsNullOrEmpty(parent) ? member : $"{parent}.{member}";

    [GeneratedRegex(@"\((BR-[A-Z0-9\-]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex RuleCode();
}
