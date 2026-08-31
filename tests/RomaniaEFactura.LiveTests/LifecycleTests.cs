using Microsoft.Extensions.DependencyInjection;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura.LiveTests;

/// <summary>
/// The same journey the mock suite proves, run against ANAF's real test environment.
/// </summary>
/// <remarks>
/// <para>
/// The milestone's acceptance criterion is that this passes with only a base-address change from
/// the mock run, which is why it uses the ordinary <see cref="IRomaniaEFacturaService"/> and
/// nothing test-specific. Any divergence found here is a defect in the mock, to be fixed there
/// rather than worked around in the client.
/// </para>
/// <para>
/// The outbound journey is one test rather than four. xUnit does not guarantee the order of
/// methods in a class, so splitting send, poll and download into separate tests would leave each
/// depending on state another might not have produced yet — and making each self-sufficient would
/// send three fiscal documents where one will do.
/// </para>
/// </remarks>
[Collection(nameof(LiveRunCollection))]
public class LifecycleTests(LiveRunReport report)
{
    [LiveAnafFact]
    public async Task TheCompanyIsAuthorized()
    {
        await using var services = LiveAnaf.BuildServices();
        await using var scope = services.CreateAsyncScope();
        var efactura = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();

        var status = await efactura.GetAuthorizationStatusAsync();

        report.Record("authorization",
            $"connected={status.IsConnected}, access token expires {status.AccessTokenExpiresAt:u}");

        Assert.True(
            status.IsConnected,
            $"""
             CIF {LiveAnaf.Cif} is not authorized in the database at
             {Environment.GetEnvironmentVariable(LiveAnaf.DatabaseVariable)}.

             Authorization needs a person presenting a qualified certificate and cannot be
             automated — do it through the sample app first. See docs/live-run.md.
             """);
    }

    [LiveAnafFact]
    public async Task TheWholeOutboundJourneyWorks()
    {
        await using var services = LiveAnaf.BuildServices();
        await using var scope = services.CreateAsyncScope();
        var efactura = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();
        var api = scope.ServiceProvider.GetRequiredService<IAnafApiClient>();

        // --- verify -----------------------------------------------------------------

        var invoice = LiveDocuments.Invoice(LiveAnaf.Cif);

        // If offline validation and the real service ever disagree, that is the single most
        // valuable thing this whole suite can discover.
        var verdict = efactura.Verify(invoice);
        Assert.True(verdict.IsValid, $"Offline validation rejected the document: {verdict}");

        // --- send -------------------------------------------------------------------

        var send = await efactura.SendInvoiceAsync(invoice);

        report.Record("upload", send.IsSuccess
            ? $"accepted, index {send.Value.UploadIndex}"
            : $"REFUSED — kind {send.Error!.Kind}: {send.Error.Message}");

        Assert.True(
            send.IsSuccess,
            $"ANAF refused an invoice our own validator accepted. That is a broken promise, not a "
            + $"caller error. ANAF said: {send.Error}");

        report.UploadIndex = send.Value.UploadIndex;

        // --- poll -------------------------------------------------------------------

        var started = DateTimeOffset.UtcNow;
        MessageStatus? settled = null;
        var polls = 0;

        // Paced at thirty seconds rather than on the reconciler's widening schedule, because the
        // question here is how long ANAF takes and the backoff would blur that. Ten polls is half
        // the documented daily cap, leaving room to run this again the same day.
        while (polls < 10 && DateTimeOffset.UtcNow - started < TimeSpan.FromMinutes(6))
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            polls++;

            var status = await api.GetStatusAsync(send.Value.UploadIndex);

            if (!status.IsSuccess)
            {
                report.Record("status-poll", $"failed after {polls} polls: {status.Error}");
                Assert.Fail($"Polling failed: {status.Error}");
            }

            if (status.Value.IsComplete)
            {
                settled = status.Value;
                break;
            }
        }

        var elapsed = DateTimeOffset.UtcNow - started;

        report.Record("resolution-timing", settled is null
            ? $"still 'in prelucrare' after {elapsed.TotalMinutes:F1} minutes and {polls} polls"
            : $"{settled.State} after {elapsed.TotalMinutes:F1} minutes and {polls} polls "
              + $"(ANAF's wording: '{settled.RawState}')");

        Assert.True(
            settled is not null,
            $"""
             The document was still being processed after {elapsed.TotalMinutes:F0} minutes.

             That is not necessarily a failure — it is a measurement. If ANAF routinely takes
             longer than this, the reconciler's early intervals are too dense and the schedule in
             PollSchedule should start wider. Record the timing before changing anything.
             """);

        report.DownloadId = settled!.DownloadId;

        // --- download ---------------------------------------------------------------

        Assert.True(
            settled.DownloadId is not null,
            $"ANAF settled the document as {settled.State} but returned no download identifier. "
            + "A rejected document should still have a downloadable error response.");

        var archive = await api.DownloadArchiveAsync(settled.DownloadId!);

        report.Record("download", archive.IsSuccess
            ? $"{archive.Value.Length} bytes"
            : $"FAILED — kind {archive.Error!.Kind}: {archive.Error.Message}");

        Assert.True(archive.IsSuccess, $"Downloading the signed archive failed: {archive.Error}");

        var document = EFacturaArchiveReader.Read(archive.Value);

        report.Record("archive-contents",
            $"kind={document.Kind}, signature={(document.SignatureXml is null ? "ABSENT" : "present")}");

        Assert.NotEqual(EFacturaDocumentKind.Unknown, document.Kind);

        // The ministry's seal is the proof of submission. An archive without one would mean the
        // library has been storing something that does not prove what it is kept to prove.
        Assert.True(
            document.SignatureXml is not null,
            "The archive carried no signature. That is what makes it worth retaining.");
    }

    [LiveAnafFact]
    public async Task TheInboxCanBeListed()
    {
        await using var services = LiveAnaf.BuildServices();
        await using var scope = services.CreateAsyncScope();
        var efactura = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();

        var sync = await efactura.SyncInboxAsync();

        report.Record("inbox-sync", sync.IsSuccess
            ? $"{sync.Value.NewMessages} new, {sync.Value.AlreadyKnown} already held, "
              + $"synced up to {sync.Value.SyncedUpTo:u}"
            : $"FAILED — kind {sync.Error!.Kind}: {sync.Error.Message}");

        // An empty inbox is a success. ANAF's "Nu exista mesaje" means empty, not failure, and a
        // test company may genuinely have nothing.
        Assert.True(sync.IsSuccess, $"Inbox sync failed: {sync.Error}");
    }
}
