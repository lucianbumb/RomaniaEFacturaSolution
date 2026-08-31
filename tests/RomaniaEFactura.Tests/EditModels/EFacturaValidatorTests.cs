using Microsoft.AspNetCore.Components.Forms;
using RomaniaEFactura.EditModels;

namespace RomaniaEFactura.Tests.EditModels;

/// <summary>
/// The Blazor validator, and the path-to-field mapping it depends on.
/// </summary>
/// <remarks>
/// Driven through an <see cref="EditContext"/> rather than by rendering a component, so the suite
/// needs no renderer. What is under test is the mapping from a finding path such as
/// <c>Lines[2].UnitCode</c> to the object and property a form binds to: get that wrong and every
/// message piles up at the top of the page instead of appearing beside the input that caused it.
/// </remarks>
public class EFacturaValidatorTests
{
    [Fact]
    public void AFindingOnALineLandsOnThatLinesField()
    {
        var invoice = SampleEditModels.FullInvoice();
        invoice.Lines[2].UnitCode = string.Empty;

        var context = Validate(invoice);

        // The message belongs to the third line's own object, which is what the input is bound to.
        var field = new FieldIdentifier(invoice.Lines[2], nameof(DocumentLineEditModel.UnitCode));
        Assert.Contains("required", string.Join(" ", context.GetValidationMessages(field)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AFindingTwoLevelsDownLandsOnTheNestedObject()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Buyer.Address.City = string.Empty;

        var context = Validate(invoice);

        var field = new FieldIdentifier(invoice.Buyer.Address, nameof(AddressEditModel.City));
        Assert.NotEmpty(context.GetValidationMessages(field));
    }

    [Fact]
    public void AFindingAboutTheDocumentLandsOnTheDocument()
    {
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.DueDate = null;   // BR-CO-25

        var context = Validate(invoice);

        var field = new FieldIdentifier(invoice, nameof(InvoiceEditModel.DueDate));
        Assert.NotEmpty(context.GetValidationMessages(field));
    }

    [Fact]
    public void AFindingAboutTheLinesAsAWholeLandsOnTheCollection()
    {
        // "Lines" rather than "Lines[0].something": the rule is about the list, not an element.
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Lines.Clear();

        var context = Validate(invoice);

        var field = new FieldIdentifier(invoice, nameof(InvoiceEditModel.Lines));
        Assert.Contains("BR-16", string.Join(" ", context.GetValidationMessages(field)), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryMessageReachesTheFormEvenWhenItsPathCannotBeResolved()
    {
        // The property that matters most: a message must never be dropped. GetValidationMessages()
        // with no field returns the whole form's messages, which is what ValidationSummary shows.
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Number = string.Empty;
        invoice.Buyer.Name = string.Empty;
        invoice.Lines[0].Name = string.Empty;

        var context = Validate(invoice);

        Assert.Equal(3, context.GetValidationMessages().Count());
    }

    [Fact]
    public void AValidInvoicePassesTheForm()
    {
        var context = Validate(SampleEditModels.FullInvoice());

        Assert.Empty(context.GetValidationMessages());
    }

    [Fact]
    public void TheFormAgreesWithVerify()
    {
        // The form and the send path must never disagree about whether an invoice is sendable:
        // a form that enables its button on a document Verify rejects is worse than no validation.
        var invoice = SampleEditModels.MinimalInvoice();
        invoice.Seller.Address.County = "Bucuresti";

        var context = Validate(invoice);

        Assert.NotEmpty(context.GetValidationMessages());
        Assert.False(EditModelValidator.Validate(invoice).IsValid);
    }

    /// <summary>
    /// Runs the validator's own logic against an edit context, as the component does on submit.
    /// </summary>
    /// <remarks>
    /// The component's <c>Validate</c> is private and needs a renderer to reach, so this mirrors
    /// it: the same findings, the same resolver, through the public component surface would add a
    /// test-only renderer dependency for no extra coverage of the part that can be wrong.
    /// </remarks>
    private static EditContext Validate(object model)
    {
        var context = new EditContext(model);
        var store = new ValidationMessageStore(context);

        foreach (var finding in EditModelValidator.ValidateModel(model))
        {
            store.Add(ResolveViaComponent(model, finding.Path) ?? context.Field(finding.Path ?? string.Empty),
                finding.Message);
        }

        return context;
    }

    /// <summary>Calls the component's resolver, which is where the interesting logic lives.</summary>
    private static FieldIdentifier? ResolveViaComponent(object model, string? path) =>
        (FieldIdentifier?)typeof(EFacturaValidator)
            .GetMethod("Resolve", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [model, path]);
}
