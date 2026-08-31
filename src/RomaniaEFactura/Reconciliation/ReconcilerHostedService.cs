using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Configuration;

namespace RomaniaEFactura.Reconciliation;

/// <summary>
/// Runs the reconciler on a loop for as long as the application is up.
/// </summary>
/// <remarks>
/// The loop interval only decides how often the reconciler <em>looks</em>. What it actually does
/// is governed by each submission's own schedule, so a short interval here does not translate into
/// more calls to ANAF — it simply means a document that has become due is picked up sooner.
/// </remarks>
public sealed class ReconcilerHostedService(
    EFacturaReconciler reconciler,
    IOptions<EFacturaOptions> options,
    ILogger<ReconcilerHostedService> logger) : BackgroundService
{
    private readonly EFacturaOptions _options = options.Value;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableReconciler)
        {
            logger.LogInformation("The e-Factura reconciler is disabled; submissions will not be settled automatically.");
            return;
        }

        logger.LogInformation(
            "The e-Factura reconciler is running, checking every {Interval}.", _options.ReconcileInterval);

        using var timer = new PeriodicTimer(_options.ReconcileInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var outcome = await reconciler.RunOnceAsync(stoppingToken).ConfigureAwait(false);

                if (outcome.DidWork)
                {
                    logger.LogInformation(
                        "Reconciled: polled {Polled}, resolved {Resolved}, archived {Downloaded}, "
                        + "quota-deferred {Quota}, unauthorized {Unauthorized}, failed {Failed}.",
                        outcome.Polled, outcome.Resolved, outcome.Downloaded,
                        outcome.QuotaExhausted, outcome.Unauthorized, outcome.Failed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The loop must outlive any single failure. A reconciler that dies leaves every
                // submission permanently unresolved, and nothing would report that it had stopped.
                logger.LogError(ex, "A reconciliation pass failed; the next one will run as scheduled.");
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

        logger.LogInformation("The e-Factura reconciler has stopped.");
    }
}
