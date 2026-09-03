using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.Transport;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Lookup;

/// <summary>Asks ANAF's public taxpayer register about companies.</summary>
public interface IAnafCompanyLookupClient
{
    /// <summary>
    /// Looks up companies by fiscal code.
    /// </summary>
    /// <param name="cuis">The fiscal codes, with or without the <c>RO</c> prefix.</param>
    /// <param name="on">
    /// The date to ask about, which is what the answer describes. Defaults to today; a past date
    /// gives the register as it stood then, which is what an invoice already issued should be
    /// judged against.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<AnafResult<CompanyLookupResult>> LookupAsync(
        IEnumerable<string> cuis,
        DateOnly? on = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The default <see cref="IAnafCompanyLookupClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// A different service from the rest of the library: no authorization, its own host, and its own
/// limits — <b>at most 100 fiscal codes per request, and at most one request per second per
/// client</b>. Both are ANAF's, published with the service, and both shape what this does.
/// </para>
/// <para>
/// More than a hundred codes are therefore split into batches and sent in turn rather than
/// refused, because the alternative is every caller writing the same chunking loop and none of
/// them writing the pacing. A large lookup accordingly takes about a second per hundred, which is
/// the service's speed rather than an inefficiency to route around.
/// </para>
/// </remarks>
public sealed class AnafCompanyLookupClient(
    IHttpClientFactory httpClientFactory,
    IOptions<EFacturaOptions> options,
    ILogger<AnafCompanyLookupClient> logger) : IAnafCompanyLookupClient
{
    /// <summary>The name of the typed <see cref="HttpClient"/> this client resolves.</summary>
    public const string HttpClientName = "RomaniaEFactura.Lookup";

    /// <summary>ANAF's published ceiling on one request.</summary>
    public const int MaxCuisPerRequest = 100;

    /// <summary>ANAF's published rate, which applies to the client as a whole.</summary>
    private static readonly TimeSpan MinimumGap = TimeSpan.FromSeconds(1);

    private readonly EFacturaOptions _options = options.Value;

    /// <summary>The clock, overridable so tests need not wait out the rate limit.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// The pacing state, shared across the process by default.
    /// </summary>
    /// <remarks>
    /// Settable so a test can hold its own. The default is deliberately shared and therefore
    /// outlives any one instance, which is correct — ANAF's limit is on the client, so two scoped
    /// instances pacing separately would exceed it — and is also why a test that used the default
    /// would inherit whatever a previous test left behind.
    /// </remarks>
    internal LookupPacer Pacer { get; set; } = LookupPacer.Shared;

    /// <summary>Waits out the rate limit. Overridable so tests do not sleep.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    /// <inheritdoc />
    public async Task<AnafResult<CompanyLookupResult>> LookupAsync(
        IEnumerable<string> cuis,
        DateOnly? on = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cuis);

        var asOf = on ?? DateOnly.FromDateTime(Clock().UtcDateTime);

        // A set alongside the list, rather than List.Contains inside the loop. The batching this
        // method exists for means a caller may pass thousands of codes, and a linear scan per code
        // is quadratic before a single request is sent.
        var normalised = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cui in cuis)
        {
            var value = RomanianCif.Normalize(cui);
            if (string.IsNullOrEmpty(value)) continue;

            // Checked here for the same reason it is checked before an e-Factura call: ANAF would
            // refuse it, and the register answers about numbers.
            if (!RomanianCif.IsValid(value))
            {
                throw new ArgumentException(
                    $"'{cui}' is not a valid Romanian fiscal code - the control digit does not match.",
                    nameof(cuis));
            }

            if (seen.Add(value)) normalised.Add(value);
        }

        if (normalised.Count == 0)
        {
            return AnafResult<CompanyLookupResult>.Success(new CompanyLookupResult([], []));
        }

        var found = new List<CompanyLookup>();
        var notFound = new List<string>();

        foreach (var batch in Chunk(normalised, MaxCuisPerRequest))
        {
            var result = await LookupBatchAsync(batch, asOf, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess) return result;

            found.AddRange(result.Value.Found);
            notFound.AddRange(result.Value.NotFound);
        }

        return AnafResult<CompanyLookupResult>.Success(new CompanyLookupResult(found, notFound));
    }

    private async Task<AnafResult<CompanyLookupResult>> LookupBatchAsync(
        IReadOnlyList<string> batch,
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(batch.Select(cui => new LookupRequest(
            long.Parse(cui, CultureInfo.InvariantCulture),
            asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));

        RawAnafResponse raw;

        await Pacer.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PaceAsync(cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri())
            {
                Content = new StringContent(payload, new UTF8Encoding(false), "application/json"),
            };

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.Timeout);

            try
            {
                var client = httpClientFactory.CreateClient(HttpClientName);
                using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
                raw = await AnafEnvelope.BufferAsync(response, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException
                                       && !cancellationToken.IsCancellationRequested)
            {
                return AnafResult<CompanyLookupResult>.Failure(new AnafError(
                    AnafErrorKind.ServiceUnavailable,
                    $"ANAF's taxpayer register is unreachable: {ex.Message}"));
            }
            finally
            {
                Pacer.LastCall = Clock();
            }
        }
        finally
        {
            Pacer.Gate.Release();
        }

        if (AnafEnvelope.DetectError(raw) is { } error)
        {
            logger.LogWarning("ANAF's taxpayer register refused a lookup of {Count} codes: {Error}",
                batch.Count, error);
            return AnafResult<CompanyLookupResult>.Failure(error);
        }

        return Parse(raw, asOf);
    }

    /// <summary>
    /// Waits out the one-per-second limit.
    /// </summary>
    /// <remarks>
    /// Held across the request rather than only before it, because the limit is on requests
    /// arriving, and two callers releasing together would both be inside their gap.
    /// </remarks>
    private async Task PaceAsync(CancellationToken cancellationToken)
    {
        var elapsed = Clock() - Pacer.LastCall;
        if (elapsed < MinimumGap)
        {
            await Delay(MinimumGap - elapsed, cancellationToken).ConfigureAwait(false);
        }
    }

    private Uri BuildUri() =>
        new($"{_options.ResolvedCompanyLookupBaseAddress.AbsoluteUri.TrimEnd('/')}/tva");

    private static AnafResult<CompanyLookupResult> Parse(RawAnafResponse raw, DateOnly asOf)
    {
        try
        {
            using var document = JsonDocument.Parse(raw.Text);
            var root = document.RootElement;

            var found = new List<CompanyLookup>();
            if (root.TryGetProperty("found", out var foundArray)
                && foundArray.ValueKind == JsonValueKind.Array)
            {
                found.AddRange(foundArray.EnumerateArray().Select(e => ReadCompany(e, asOf)));
            }

            var notFound = new List<string>();
            if (root.TryGetProperty("notFound", out var notFoundArray)
                && notFoundArray.ValueKind == JsonValueKind.Array)
            {
                notFound.AddRange(notFoundArray.EnumerateArray()
                    .Select(ReadLoose)
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Select(v => v!));
            }

            return AnafResult<CompanyLookupResult>.Success(new CompanyLookupResult(found, notFound));
        }
        catch (JsonException ex)
        {
            return AnafResult<CompanyLookupResult>.Failure(new AnafError(
                AnafErrorKind.Unreadable,
                $"ANAF's taxpayer register returned JSON that could not be parsed: {ex.Message}"));
        }
    }

    private static CompanyLookup ReadCompany(JsonElement entry, DateOnly asOf)
    {
        var general = Child(entry, "date_generale");

        return new CompanyLookup
        {
            Cui = RomanianCif.Normalize(Text(general, "cui")) ?? string.Empty,
            Name = Text(general, "denumire"),
            Address = Text(general, "adresa"),
            RegistrationNumber = Text(general, "nrRegCom"),
            Phone = Text(general, "telefon"),
            CaenCode = Text(general, "cod_CAEN"),
            Iban = Text(general, "iban"),
            IsRegisteredForEFactura = Flag(general, "statusRO_e_Factura"),
            IsVatRegistered = Flag(Child(entry, "inregistrare_scop_Tva"), "scpTVA"),
            IsInactive = Flag(Child(entry, "stare_inactiv"), "statusInactivi"),
            RegisteredOffice = ReadAddress(Child(entry, "adresa_sediu_social"), "s"),
            FiscalDomicile = ReadAddress(Child(entry, "adresa_domiciliu_fiscal"), "d"),
            AsOf = asOf,
        };
    }

    /// <summary>
    /// Reads one of the two address blocks.
    /// </summary>
    /// <remarks>
    /// The two carry the same fields under different prefixes — <c>sdenumire_Strada</c> against
    /// <c>ddenumire_Strada</c> — so the prefix is a parameter rather than the block being read
    /// twice.
    /// </remarks>
    private static CompanyAddress? ReadAddress(JsonElement? block, string prefix)
    {
        if (block is not { ValueKind: JsonValueKind.Object }) return null;

        var address = new CompanyAddress(
            Text(block, $"{prefix}denumire_Strada"),
            Text(block, $"{prefix}numar_Strada"),
            Text(block, $"{prefix}denumire_Localitate"),
            Text(block, $"{prefix}denumire_Judet"),
            Text(block, $"{prefix}cod_JudetAuto"),
            Text(block, prefix == "s" ? "stara" : "dtara"),
            Text(block, $"{prefix}cod_Postal"),
            Text(block, $"{prefix}detalii_Adresa"));

        var empty = address is { Street: null, Locality: null, County: null, PostalCode: null };
        return empty ? null : address;
    }

    private static JsonElement? Child(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var child)
            ? child
            : null;

    private static string? Text(JsonElement? element, string name)
    {
        if (element is not { ValueKind: JsonValueKind.Object }) return null;
        if (!element.Value.TryGetProperty(name, out var value)) return null;

        var text = ReadLoose(value);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>
    /// Reads a value whatever JSON type it arrives as.
    /// </summary>
    /// <remarks>
    /// The published contract calls <c>cui</c> a string, and ANAF has been observed sending
    /// identifiers as numbers elsewhere in its APIs. Being strict here would turn an inconsistency
    /// into an exception.
    /// </remarks>
    private static string? ReadLoose(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null,
    };

    private static bool Flag(JsonElement? element, string name)
    {
        if (element is not { ValueKind: JsonValueKind.Object }) return false;
        if (!element.Value.TryGetProperty(name, out var value)) return false;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
            _ => false,
        };
    }

    private static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return [.. source.Skip(i).Take(size)];
        }
    }

    private sealed record LookupRequest(long cui, string data);
}

/// <summary>
/// When the taxpayer register was last called, and the lock that keeps callers in turn.
/// </summary>
/// <remarks>
/// One object rather than two loose statics, so the shared state has a name and a test can hold
/// its own instead of inheriting the process's.
/// </remarks>
internal sealed class LookupPacer
{
    /// <summary>The instance the library uses, shared because ANAF's limit is on the client.</summary>
    public static LookupPacer Shared { get; } = new();

    /// <summary>Keeps callers in turn, so two do not both measure the same gap and proceed.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>When a request was last sent.</summary>
    public DateTimeOffset LastCall { get; set; } = DateTimeOffset.MinValue;
}
