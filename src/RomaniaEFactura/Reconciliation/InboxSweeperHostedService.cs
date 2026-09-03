using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Configuration;

namespace RomaniaEFactura.Reconciliation;

/// <summary>
/// Runs the inbox sweep on a loop for as long as the application is up.
/// </summary>
/// <remarks>
/// The loop decides how often the sweep <em>looks</em>. What it actually reads is decided per
/// company by that company's own schedule, so a short interval here does not mean more calls to
/// ANAF — only that a company which has become due is read sooner.
/// </remarks>
public sealed class InboxSweeperHostedService(
    InboxSweeper sweeper,
    IOptions<EFacturaOptions> options,
    ILogger<InboxSweeperHostedService> logger) : BackgroundService
{
    private readonly EFacturaOptions _options = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableInboxSync)
        {
            logger.LogDebug(
                "The e-Factura inbox sweep is off. Messages arrive only when something calls "
                + "SyncInboxAsync.");
            return;
        }

        logger.LogInformation(
            "The e-Factura inbox sweep is running; each company is read every {Interval}.",
            _options.InboxSyncInterval);

        // Looking more often than a company can become due wastes nothing but a query, and looking
        // less often would delay one that has. A minute is short enough to be responsive and long
        // enough not to matter.
        var tick = _options.InboxSyncInterval < TimeSpan.FromMinutes(1)
            ? _options.InboxSyncInterval
            : TimeSpan.FromMinutes(1);

        using var timer = new PeriodicTimer(tick);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await sweeper.RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The loop must outlive any single failure. A sweep that dies leaves every inbox
                // unread, and nothing would report that it had stopped.
                logger.LogError(ex, "An inbox sweep failed; the next one will run as scheduled.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
