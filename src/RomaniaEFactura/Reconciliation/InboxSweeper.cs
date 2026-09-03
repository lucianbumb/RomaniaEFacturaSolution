using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Authentication;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.Persistence;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura.Reconciliation;

/// <summary>What one sweep of the inboxes did.</summary>
/// <param name="Companies">How many authorized companies were considered.</param>
/// <param name="Swept">How many were due and were read.</param>
/// <param name="Added">New messages recorded across all of them.</param>
/// <param name="Unauthorized">Companies whose stored authorization is no longer usable.</param>
/// <param name="Failed">Companies whose sync failed for some other reason.</param>
public readonly record struct InboxSweepOutcome(
    int Companies,
    int Swept,
    int Added,
    int Unauthorized,
    int Failed)
{
    /// <summary>Whether anything happened worth logging.</summary>
    public bool DidWork => Swept > 0 || Unauthorized > 0 || Failed > 0;
}

/// <summary>
/// Reads the SPV inbox of every authorized company.
/// </summary>
/// <remarks>
/// <para>
/// Outbound reconciliation was already company-agnostic — the reconciler settles whatever is due,
/// whoever submitted it. Inbound was not: <c>SyncInboxAsync</c> existed and nothing called it, so a
/// company's messages appeared only when somebody asked for them, and the first person to ask paid
/// for the whole sync.
/// </para>
/// <para>
/// Each company is scheduled separately rather than the whole set being read on one interval. With
/// a hundred companies a fixed interval means a hundred calls per tick, and ANAF throttles per
/// company but rate-limits the client.
/// </para>
/// <para>
/// This lists and records. It deliberately does <b>not</b> download archives: <c>descarcare</c> is
/// capped at roughly ten calls per identifier per day, and a sweep that eagerly fetched every new
/// message would spend a company's allowance before anybody had asked to read one.
/// </para>
/// </remarks>
public sealed class InboxSweeper(
    IServiceScopeFactory scopeFactory,
    IOptions<EFacturaOptions> options,
    TimeProvider time,
    ILogger<InboxSweeper> logger)
{
    private readonly EFacturaOptions _options = options.Value;

    /// <summary>Reads every company whose inbox is due, and returns what it did.</summary>
    public async Task<InboxSweepOutcome> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IEFacturaTokenStore>();
        var db = scope.ServiceProvider.GetRequiredService<EFacturaDbContext>();
        var efactura = scope.ServiceProvider.GetRequiredService<IRomaniaEFacturaService>();

        var companies = await store.ListAuthorizedCifsAsync(cancellationToken).ConfigureAwait(false);
        if (companies.Count == 0) return new InboxSweepOutcome(0, 0, 0, 0, 0);

        var now = time.GetUtcNow();
        var cursors = await db.InboxCursors
            .Where(c => companies.Contains(c.Cif))
            .ToDictionaryAsync(c => c.Cif, cancellationToken)
            .ConfigureAwait(false);

        var swept = 0;
        var added = 0;
        var unauthorized = 0;
        var failed = 0;

        foreach (var cif in companies)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A company with no cursor has never been read, so it is due. One with a cursor is due
            // when its own schedule says so.
            if (cursors.TryGetValue(cif, out var cursor) && cursor.NextSyncAt > now) continue;

            var result = await efactura.SyncInboxAsync(cif, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                swept++;
                added += result.Value.NewMessages;
                continue;
            }

            if (result.Error!.Kind == AnafErrorKind.NotAuthorized) unauthorized++;
            else failed++;

            await DeferAsync(db, cif, cursor, result.Error, now, cancellationToken).ConfigureAwait(false);
        }

        if (swept > 0 || unauthorized > 0 || failed > 0)
        {
            logger.LogInformation(
                "Swept {Swept} of {Companies} inbox(es): {Added} new message(s), "
                + "{Unauthorized} unauthorized, {Failed} failed.",
                swept, companies.Count, added, unauthorized, failed);
        }

        return new InboxSweepOutcome(companies.Count, swept, added, unauthorized, failed);
    }

    /// <summary>
    /// Pushes a company's next attempt out after a failure.
    /// </summary>
    /// <remarks>
    /// Without this a company whose authorization has lapsed is retried on every pass forever. The
    /// interval widens with consecutive failures and is capped, so a company that will never
    /// succeed again costs a call a day rather than one a minute — and one that fails once
    /// recovers quickly.
    /// </remarks>
    private async Task DeferAsync(
        EFacturaDbContext db,
        string cif,
        EFacturaInboxCursor? cursor,
        AnafError error,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (cursor is null)
        {
            // Never read, and the first attempt failed. The row exists from here on so the failure
            // is remembered across a restart rather than beginning again every time.
            cursor = new EFacturaInboxCursor
            {
                Cif = cif,
                SyncedUpTo = now.AddDays(-60),
                LastSyncedAt = now,
            };
            db.InboxCursors.Add(cursor);
        }

        cursor.ConsecutiveFailures++;
        cursor.LastError = error.ToString();
        cursor.NextSyncAt = now + Backoff(cursor.ConsecutiveFailures, _options.InboxSyncInterval);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The interval doubles with each consecutive failure, up to a day.</summary>
    internal static TimeSpan Backoff(int consecutiveFailures, TimeSpan interval)
    {
        var factor = Math.Min(consecutiveFailures, 10);
        var widened = interval * Math.Pow(2, factor);

        return widened > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : widened;
    }
}
