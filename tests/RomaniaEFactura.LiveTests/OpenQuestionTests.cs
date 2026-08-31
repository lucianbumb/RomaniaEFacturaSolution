using Microsoft.Extensions.DependencyInjection;
using RomaniaEFactura.EditModels;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura.LiveTests;

/// <summary>
/// The things the mock cannot settle, because they were built from something other than an ANAF
/// source or cannot be expressed in a local file.
/// </summary>
/// <remarks>
/// Each of these is listed as an open question in <c>docs/anaf-wire-formats.md</c>. A failure here
/// is not a regression — it is the answer, and it should be written into that document and into
/// the mock.
/// </remarks>
[Collection(nameof(LiveRunCollection))]
public class OpenQuestionTests(LiveRunReport report)
{
    [LiveAnafFact]
    public async Task TheBuyerMessageFormatIsWhatAnafAccepts()
    {
        // The one wire format in the library not confirmed by an ANAF source. Its shape — a
        // <header> in mfp:anaf:dgti:spv:reqMesaj:v1 carrying index_incarcare and message — is
        // corroborated by two independent third parties and by no official document.
        await using var services = LiveAnaf.BuildServices();
        await using var scope = services.CreateAsyncScope();
        var efactura = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();

        // Answering a real invoice, so the message has a subject ANAF can resolve. Sent here
        // rather than borrowed from another test, because xUnit does not order methods and a
        // borrowed index may not exist yet.
        var subject = await efactura.SendInvoiceAsync(LiveDocuments.Invoice(LiveAnaf.Cif));
        Assert.True(subject.IsSuccess, $"Could not send the invoice to answer: {subject.Error}");

        var result = await efactura.SendBuyerMessageAsync(new BuyerMessageEditModel
        {
            UploadIndex = subject.Value.UploadIndex,
            Message = "Test message from the RO e-Factura library live run.",
        });

        report.Record("rasp-format", result.IsSuccess
            ? $"accepted — the corroborated shape is correct (index {result.Value.UploadIndex})"
            : $"REFUSED kind={result.Error!.Kind} message={result.Error.Message}");

        Assert.True(
            result.IsSuccess,
            $"""
             ANAF refused the buyer message. The format was never confirmed against an ANAF source,
             so this is the expected way to find out it is wrong.

             Correct BuyerMessageDocument, then the mock, then the note in docs/anaf-wire-formats.md.
             ANAF said: {result.Error}
             """);
    }

    [LiveAnafFact]
    public async Task AForeignBuyerInvoiceIsAcceptedWithTheExternFlag()
    {
        // ANAF's offline validator refuses every export invoice because it demands a Romanian
        // buyer CUI. The live API is supposed to allow it through extern=DA, which a local file
        // cannot carry — so this is the only way to know whether export invoicing works at all.
        await using var services = LiveAnaf.BuildServices();
        await using var scope = services.CreateAsyncScope();
        var efactura = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();

        var invoice = LiveDocuments.ForeignBuyerInvoice(LiveAnaf.Cif);

        Assert.True(efactura.Verify(invoice).IsValid, "Our own validation rejected the export invoice.");

        var result = await efactura.SendInvoiceAsync(
            invoice,
            options: new UploadOptions(AnafStandard.Ubl, Foreign: true));

        report.Record("extern-da", result.IsSuccess
            ? $"accepted — export invoicing works with extern=DA (index {result.Value.UploadIndex})"
            : $"REFUSED kind={result.Error!.Kind} message={result.Error.Message}");

        Assert.True(
            result.IsSuccess,
            $"""
             ANAF refused an invoice to a foreign buyer sent with extern=DA. If this is a format
             problem the library must fix it; if extern=DA is not the mechanism, the wire-format
             document is wrong about it.

             ANAF said: {result.Error}
             """);
    }

    [LiveAnafFact]
    public async Task ErrorsReallyDoArriveInsideHttp200()
    {
        // Rule 1, the assumption every response path in the transport is built on. Provoked with
        // an upload index that cannot exist: a service that returned 404 here rather than a 200
        // carrying an <Errors> element would mean the envelope reader is solving a non-problem —
        // and, more importantly, that some genuine failures are being read as successes.
        await using var services = LiveAnaf.BuildServices();
        await using var scope = services.CreateAsyncScope();
        var api = scope.ServiceProvider.GetRequiredService<IAnafApiClient>();

        var result = await api.GetStatusAsync("1");

        report.Record("rule-1-http-200", result.IsSuccess
            ? "a nonsense upload index was ACCEPTED — unexpected, investigate"
            : $"refused as expected: kind={result.Error!.Kind} message={result.Error.Message}");

        Assert.False(result.IsSuccess, "ANAF accepted a status query for upload index 1.");
    }
}
