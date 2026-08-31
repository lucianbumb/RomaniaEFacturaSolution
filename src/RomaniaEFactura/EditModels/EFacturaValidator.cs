using System.Collections;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace RomaniaEFactura.EditModels;

/// <summary>
/// Makes an <c>EditForm</c> see every rule on an e-Factura edit model, lines included.
/// </summary>
/// <remarks>
/// <para>
/// Blazor's own <c>DataAnnotationsValidator</c> validates the model object and stops. Bound to an
/// <see cref="InvoiceEditModel"/> it therefore checks the invoice number and the currency, and
/// silently ignores every rule on every line, on both parties, and on the payment block — so a
/// form using it would enable its send button on an invoice with a nameless line. Drop this in
/// instead and the whole graph is checked.
/// </para>
/// <code>
/// &lt;EditForm Model="invoice" OnValidSubmit="SendAsync"&gt;
///     &lt;EFacturaValidator /&gt;
///     &lt;ValidationSummary /&gt;
///     ...
/// </code>
/// <para>
/// Findings arrive with paths such as <c>Lines[2].UnitCode</c>, which this walks back to the object
/// that owns the field so that <c>ValidationMessage</c> puts each message beside its own input
/// rather than all of them in a heap at the top.
/// </para>
/// </remarks>
public sealed class EFacturaValidator : ComponentBase, IDisposable
{
    private ValidationMessageStore? _messages;
    private EditContext? _subscribed;

    /// <summary>The form's edit context, supplied by <c>EditForm</c>.</summary>
    [CascadingParameter]
    public EditContext? CurrentEditContext { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (CurrentEditContext is null)
        {
            throw new InvalidOperationException(
                $"{nameof(EFacturaValidator)} must be placed inside an EditForm.");
        }

        if (ReferenceEquals(CurrentEditContext, _subscribed)) return;

        Detach();
        _subscribed = CurrentEditContext;
        _messages = new ValidationMessageStore(CurrentEditContext);

        CurrentEditContext.OnValidationRequested += OnValidationRequested;
        CurrentEditContext.OnFieldChanged += OnFieldChanged;
    }

    private void OnValidationRequested(object? sender, ValidationRequestedEventArgs args) =>
        Validate();

    private void OnFieldChanged(object? sender, FieldChangedEventArgs args)
    {
        // Revalidating the whole model on every keystroke, rather than only the field that
        // changed, is deliberate. Almost every rule here is a cross-field one — a total against
        // its lines, an exemption reason against a VAT category — so validating one field in
        // isolation would leave stale messages on the fields it affects.
        Validate();
    }

    private void Validate()
    {
        if (_subscribed is null || _messages is null) return;

        _messages.Clear();

        foreach (var finding in EditModelValidator.ValidateModel(_subscribed.Model))
        {
            var field = Resolve(_subscribed.Model, finding.Path)
                        ?? _subscribed.Field(finding.Path ?? string.Empty);

            _messages.Add(field, finding.Message);
        }

        _subscribed.NotifyValidationStateChanged();
    }

    /// <summary>
    /// Turns a finding path into the field it belongs to.
    /// </summary>
    /// <remarks>
    /// A <see cref="FieldIdentifier"/> is an object plus a property name, so <c>Lines[2].UnitCode</c>
    /// has to be walked: follow <c>Lines</c>, take element 2, and name <c>UnitCode</c> on it. Every
    /// segment except the last is followed; the last names the field on whatever the walk arrived
    /// at. Returning null when a step fails lets the caller attach the message to the model
    /// instead, where a <c>ValidationSummary</c> still shows it — a message in the wrong place is a
    /// nuisance, a message that vanishes is a defect.
    /// </remarks>
    private static FieldIdentifier? Resolve(object model, string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;

        var segments = path.Split('.');
        var owner = model;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            owner = Follow(owner, segments[i]);
            if (owner is null) return null;
        }

        var (name, index) = SplitIndex(segments[^1]);

        // A path ending in an index — "Lines[2]" — names the collection on its owner rather than a
        // property of the element, which is what a rule about the list as a whole produces.
        if (index is not null) return new FieldIdentifier(owner, name);

        return owner.GetType().GetProperty(name) is null
            ? null
            : new FieldIdentifier(owner, name);
    }

    private static object? Follow(object owner, string segment)
    {
        var (name, index) = SplitIndex(segment);

        var value = owner.GetType().GetProperty(name)?.GetValue(owner);
        if (value is null) return null;
        if (index is null) return value;

        return value is IEnumerable items
            ? items.Cast<object?>().ElementAtOrDefault(index.Value)
            : null;
    }

    private static (string Name, int? Index) SplitIndex(string segment)
    {
        var bracket = segment.IndexOf('[', StringComparison.Ordinal);
        if (bracket < 0) return (segment, null);

        var name = segment[..bracket];
        var digits = segment[(bracket + 1)..].TrimEnd(']');

        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            ? (name, index)
            : (name, null);
    }

    private void Detach()
    {
        if (_subscribed is null) return;

        _subscribed.OnValidationRequested -= OnValidationRequested;
        _subscribed.OnFieldChanged -= OnFieldChanged;
        _subscribed = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Detach();
        GC.SuppressFinalize(this);
    }
}
