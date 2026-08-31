using Microsoft.Extensions.DependencyInjection;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura.LiveTests;

/// <summary>
/// Settles what ANAF's daily call cap is actually counted against.
/// </summary>
/// <remarks>
/// <para>
/// This is the single assumption in the whole library that could be wrong in a way that matters.
/// The reconciler's widening schedule is built on the cap being <em>per document</em> — roughly
/// twenty status calls per upload index per day. If it is really per company, then a business
/// sending fifty invoices a day exhausts its allowance on the first three and goes blind to the
/// rest, and the schedule needs rethinking rather than retuning.
/// </para>
/// <para>
/// ANAF's own wording — <c>S-au facut deja 20 descarcari de mesaj in cursul zilei</c>, "20
/// downloads of the message have already been made today" — reads as per-message, which is why the
/// library assumes it. Wording is not proof.
/// </para>
/// <para>
/// <b>This test deliberately spends a day's allowance</b> on one upload index, which is why it is
/// separated from the lifecycle tests and gated behind its own variable. Run it once, read the
/// answer, and record it in <c>docs/anaf-wire-formats.md</c>.
/// </para>
/// </remarks>
[Collection(nameof(LiveRunCollection))]
public class QuotaScopeTests(LiveRunReport report)
{
    [LiveAnafQuotaProbeFact]
    public async Task TheDailyStatusCapIsCountedPerDocumentRatherThanPerCompany()
    {
        await using var services = LiveAnaf.BuildServices();
        await using var scope = services.CreateAsyncScope();
        var efactura = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();
        var api = scope.ServiceProvider.GetRequiredService<IAnafApiClient>();

        // Two documents, so the cap can be attributed to one of them rather than to the company.
        var first = await efactura.SendInvoiceAsync(LiveDocuments.Invoice(LiveAnaf.Cif));
        var second = await efactura.SendInvoiceAsync(LiveDocuments.Invoice(LiveAnaf.Cif));

        Assert.True(first.IsSuccess, first.ToString());
        Assert.True(second.IsSuccess, second.ToString());

        var exhausted = 0;
        AnafError? refusal = null;

        // Spend the first document's allowance. The bound is generous because the real cap is what
        // is being measured; stopping at exactly twenty would assume the answer.
        for (var attempt = 1; attempt <= 40; attempt++)
        {
            var status = await api.GetStatusAsync(first.Value.UploadIndex);

            if (!status.IsSuccess && status.Error!.Kind == AnafErrorKind.QuotaExhausted)
            {
                exhausted = attempt;
                refusal = status.Error;
                break;
            }

            Assert.True(status.IsSuccess, $"Poll {attempt} failed for an unrelated reason: {status.Error}");
            await Task.Delay(TimeSpan.FromMilliseconds(600));
        }

        if (exhausted == 0)
        {
            report.Record("quota-scope", "INCONCLUSIVE — 40 status calls on one document were not refused.");
            Assert.Fail(
                "Forty status calls on a single document were all accepted. Either the cap is much "
                + "higher than documented, or it is not enforced in the test environment — in which "
                + "case this question can only be settled in production.");
        }

        // The decisive step: ask about the *other* document, whose own allowance is untouched.
        var other = await api.GetStatusAsync(second.Value.UploadIndex);

        var scopeIsPerDocument = other.IsSuccess
                                 || other.Error!.Kind != AnafErrorKind.QuotaExhausted;

        report.Record("quota-scope",
            $"refused after {exhausted} calls on one document ('{refusal!.Message}'); "
            + $"a second document was {(scopeIsPerDocument ? "still answerable" : "ALSO refused")} "
            + $"→ the cap is {(scopeIsPerDocument ? "per document" : "per company or wider")}.");

        Assert.True(
            scopeIsPerDocument,
            $"""
             The daily status cap is NOT per document — a second, untouched upload index was
             refused too after {exhausted} calls on the first.

             This invalidates the reconciler's schedule, which assumes each document has its own
             allowance. With a per-company cap, a business sending many invoices a day would go
             blind to most of them. The schedule needs a shared budget across documents, not a
             per-document backoff.

             ANAF said: {refusal.Message}
             """);
    }

}
