using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Transport;

/// <summary>
/// The ANAF e-Factura HTTP client.
/// </summary>
/// <remarks>
/// Every response is interpreted by <see cref="AnafEnvelope"/> and nowhere else, because ANAF
/// reports failure inside HTTP 200 on every endpoint. There is deliberately no
/// <c>IsSuccessStatusCode</c> check in this file.
/// </remarks>
public sealed class AnafApiClient : IAnafApiClient
{
    /// <summary>The name of the typed <see cref="HttpClient"/> this client resolves.</summary>
    public const string HttpClientName = "RomaniaEFactura.Anaf";

    /// <summary>ANAF's own timestamp format, used by <c>dateResponse</c> and <c>data_creare</c>.</summary>
    private const string AnafTimestampFormat = "yyyyMMddHHmm";

    /// <summary>ANAF rejects a start time older than this, on both list endpoints.</summary>
    private static readonly TimeSpan MaximumLookback = TimeSpan.FromDays(60);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CompanyGates = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, DateTimeOffset> LastCallPerCompany = new(StringComparer.Ordinal);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAnafAccessTokenProvider _tokenProvider;
    private readonly EFacturaOptions _options;
    private readonly ILogger<AnafApiClient> _logger;

    /// <summary>Creates the client.</summary>
    public AnafApiClient(
        IHttpClientFactory httpClientFactory,
        IAnafAccessTokenProvider tokenProvider,
        IOptions<EFacturaOptions> options,
        ILogger<AnafApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>The clock, overridable so tests can exercise the sixty-day boundary.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    // ---------------------------------------------------------------- upload

    /// <inheritdoc />
    public async Task<AnafResult<UploadReceipt>> UploadAsync(
        string xml,
        string? cif = null,
        UploadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        options ??= new UploadOptions();
        var company = ResolveCif(cif);
        var endpoint = options.B2C ? "uploadb2c" : "upload";

        var query = new Dictionary<string, string?>
        {
            ["standard"] = options.StandardParameter,
            ["cif"] = company,
        };

        // Each optional flag is only ever sent as "DA"; ANAF accepts no other value.
        if (options.Foreign) query["extern"] = "DA";
        if (options.SelfBilled) query["autofactura"] = "DA";
        if (options.Enforcement) query["executare"] = "DA";

        var result = await SendAsync(
            company,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(endpoint, query))
                {
                    Content = new StringContent(xml, new UTF8Encoding(false), "text/plain"),
                };
                return request;
            },
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess) return result.CarryError<UploadReceipt>();

        return ParseXml(result.Value, root =>
        {
            var index = root.Attribute("index_incarcare")?.Value;
            if (string.IsNullOrEmpty(index))
            {
                return AnafResult<UploadReceipt>.Failure(new AnafError(
                    AnafErrorKind.Unreadable,
                    "ANAF accepted the upload but returned no index_incarcare.",
                    RawBody: result.Value.Text));
            }

            return AnafResult<UploadReceipt>.Success(
                new UploadReceipt(index, ParseAnafTimestamp(root.Attribute("dateResponse")?.Value)));
        });
    }

    // ------------------------------------------------------------ stareMesaj

    /// <inheritdoc />
    public async Task<AnafResult<MessageStatus>> GetStatusAsync(
        string uploadIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadIndex);

        var query = new Dictionary<string, string?> { ["id_incarcare"] = uploadIndex };
        var result = await SendAsync(
            ResolveCif(null),
            () => new HttpRequestMessage(HttpMethod.Get, BuildUri("stareMesaj", query)),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess) return result.CarryError<MessageStatus>();

        return ParseXml(result.Value, root =>
        {
            var raw = root.Attribute("stare")?.Value ?? string.Empty;
            var state = raw switch
            {
                "ok" => UploadState.Ok,
                "nok" => UploadState.Nok,
                "in prelucrare" => UploadState.InProgress,
                _ when raw.Contains("XML cu erori", StringComparison.OrdinalIgnoreCase)
                    => UploadState.RejectedAtUpload,
                _ => UploadState.InProgress,
            };

            return AnafResult<MessageStatus>.Success(
                new MessageStatus(state, root.Attribute("id_descarcare")?.Value, raw));
        });
    }

    // ----------------------------------------------------------- lista mesaje

    /// <inheritdoc />
    public async Task<AnafResult<IReadOnlyList<AnafMessage>>> ListMessagesAsync(
        int days,
        string? cif = null,
        MessageFilter filter = MessageFilter.All,
        CancellationToken cancellationToken = default)
    {
        if (days is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(nameof(days), days, "ANAF accepts between 1 and 60 days.");
        }

        var company = ResolveCif(cif);
        var query = new Dictionary<string, string?>
        {
            ["zile"] = days.ToString(CultureInfo.InvariantCulture),
            ["cif"] = company,
        };
        AddFilter(query, filter);

        var result = await SendAsync(
            company,
            () => new HttpRequestMessage(HttpMethod.Get, BuildUri("listaMesajeFactura", query)),
            cancellationToken).ConfigureAwait(false);

        // An empty inbox arrives as an "eroare" saying so. It is not a failure, and reporting it
        // as one would make a quiet day look like an outage.
        if (IsEmptyInbox(result)) return AnafResult<IReadOnlyList<AnafMessage>>.Success([]);
        if (!result.IsSuccess) return result.CarryError<IReadOnlyList<AnafMessage>>();

        return ParseJson(result.Value, root =>
            AnafResult<IReadOnlyList<AnafMessage>>.Success(ReadMessages(root)));
    }

    /// <inheritdoc />
    public async Task<AnafResult<MessagePage>> ListMessagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        int page = 1,
        string? cif = null,
        MessageFilter filter = MessageFilter.All,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);

        var company = ResolveCif(cif);

        // The sixty-day limit applies here too, even though the endpoint takes arbitrary
        // timestamps. Clamping is kinder than letting ANAF refuse the whole call.
        var earliest = Clock() - MaximumLookback + TimeSpan.FromMinutes(1);
        if (from < earliest)
        {
            _logger.LogDebug(
                "Clamping the message list start from {Requested:O} to {Clamped:O}; ANAF rejects anything older than 60 days.",
                from, earliest);
            from = earliest;
        }

        var query = new Dictionary<string, string?>
        {
            ["startTime"] = from.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            ["endTime"] = to.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            ["cif"] = company,
            ["pagina"] = page.ToString(CultureInfo.InvariantCulture),
        };
        AddFilter(query, filter);

        var result = await SendAsync(
            company,
            () => new HttpRequestMessage(HttpMethod.Get, BuildUri("listaMesajePaginatieFactura", query)),
            cancellationToken).ConfigureAwait(false);

        if (IsEmptyInbox(result))
        {
            return AnafResult<MessagePage>.Success(new MessagePage([], page, 0));
        }

        if (!result.IsSuccess) return result.CarryError<MessagePage>();

        return ParseJson(result.Value, root =>
        {
            var totalPages = root.TryGetProperty("numar_total_pagini", out var pages)
                             && pages.ValueKind == JsonValueKind.Number
                ? pages.GetInt32()
                : 1;

            return AnafResult<MessagePage>.Success(new MessagePage(ReadMessages(root), page, totalPages));
        });
    }

    // ------------------------------------------------------------- descarcare

    /// <inheritdoc />
    public async Task<AnafResult<byte[]>> DownloadArchiveAsync(
        string downloadId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadId);

        var query = new Dictionary<string, string?> { ["id"] = downloadId };
        var result = await SendAsync(
            ResolveCif(null),
            () => new HttpRequestMessage(HttpMethod.Get, BuildUri("descarcare", query)),
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess) return result.CarryError<byte[]>();

        // The envelope reader has already rejected a JSON error body arriving in place of the
        // archive, so anything reaching here that is not a ZIP is genuinely unexpected.
        if (!result.Value.IsZip)
        {
            return AnafResult<byte[]>.Failure(new AnafError(
                AnafErrorKind.Unreadable,
                "ANAF returned something that is not a ZIP archive.",
                (int)result.Value.StatusCode,
                Head(result.Value.Text)));
        }

        return AnafResult<byte[]>.Success(result.Value.Body);
    }

    // ------------------------------------------------- validare / transformare

    /// <inheritdoc />
    public async Task<AnafResult<AnafValidationOutcome>> ValidateAsync(
        string xml,
        AnafStandard standard = AnafStandard.Ubl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        var uri = new Uri(
            $"{_options.ResolvedPublicToolsBaseAddress.AbsoluteUri.TrimEnd('/')}/validare/{ValidationStandard(standard)}");

        var result = await SendAsync(
            ResolveCif(null),
            () => new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(xml, new UTF8Encoding(false), "text/plain"),
            },
            cancellationToken,
            requiresToken: false).ConfigureAwait(false);

        if (!result.IsSuccess) return result.CarryError<AnafValidationOutcome>();

        return ParseJson(result.Value, root =>
        {
            // The real shape is stare + Messages. The previous version modelled it as
            // succes/erori, so a valid invoice always deserialized as a failure with no errors.
            var isValid = root.TryGetProperty("stare", out var stare)
                          && string.Equals(stare.GetString(), "ok", StringComparison.OrdinalIgnoreCase);

            var messages = new List<string>();
            if (root.TryGetProperty("Messages", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                messages.AddRange(list.EnumerateArray()
                    .Select(m => m.TryGetProperty("message", out var text) ? text.GetString() : null)
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Select(m => m!));
            }

            var traceId = root.TryGetProperty("trace_id", out var trace) ? trace.GetString() : null;

            return AnafResult<AnafValidationOutcome>.Success(
                new AnafValidationOutcome(isValid, messages, traceId));
        });
    }

    /// <inheritdoc />
    public async Task<AnafResult<byte[]>> RenderPdfAsync(
        string xml,
        AnafStandard standard = AnafStandard.Ubl,
        bool skipValidation = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        var path = $"transformare/{ValidationStandard(standard)}" + (skipValidation ? "/DA" : string.Empty);
        var uri = new Uri($"{_options.ResolvedPublicToolsBaseAddress.AbsoluteUri.TrimEnd('/')}/{path}");

        var result = await SendAsync(
            ResolveCif(null),
            () => new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(xml, new UTF8Encoding(false), "text/plain"),
            },
            cancellationToken,
            requiresToken: false).ConfigureAwait(false);

        if (!result.IsSuccess) return result.CarryError<byte[]>();

        if (result.Value.IsPdf) return AnafResult<byte[]>.Success(result.Value.Body);

        // ANAF sometimes answers with base64 text rather than raw bytes.
        var text = result.Value.Text.Trim();
        if (text.StartsWith("JVBER", StringComparison.Ordinal))
        {
            try
            {
                return AnafResult<byte[]>.Success(Convert.FromBase64String(text));
            }
            catch (FormatException)
            {
                // Falls through to the failure below.
            }
        }

        return AnafResult<byte[]>.Failure(new AnafError(
            AnafErrorKind.Unreadable, "ANAF did not return a PDF.", RawBody: Head(text)));
    }

    // ---------------------------------------------------------------- sending

    /// <summary>
    /// Issues a request, applying the token, the per-company pacing, and the retry policy.
    /// </summary>
    private async Task<AnafResult<RawAnafResponse>> SendAsync(
        string company,
        Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken,
        bool requiresToken = true)
    {
        string? token = null;
        if (requiresToken)
        {
            token = await _tokenProvider.GetAccessTokenAsync(company, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(token))
            {
                // Not an exception: nobody having authorized this company yet is an ordinary state
                // that a page has to render, not a bug.
                return AnafResult<RawAnafResponse>.Failure(new AnafError(
                    AnafErrorKind.NotAuthorized,
                    $"No ANAF authorization is stored for CIF {company}."));
            }
        }

        // ANAF throttles per company, so calls for one company are paced and serialized.
        var gate = CompanyGates.GetOrAdd(company, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await PaceAsync(company, cancellationToken).ConfigureAwait(false);

            var delay = _options.RetryDelay;
            AnafError? lastError = null;

            for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
            {
                using var request = createRequest();
                if (token is not null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_options.Timeout);

                RawAnafResponse raw;
                try
                {
                    var client = _httpClientFactory.CreateClient(HttpClientName);
                    using var response = await client
                        .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
                        .ConfigureAwait(false);

                    raw = await AnafEnvelope.BufferAsync(response, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    lastError = new AnafError(AnafErrorKind.ServiceUnavailable,
                        $"ANAF did not respond within {_options.Timeout.TotalSeconds:0}s.");
                    if (attempt == _options.MaxRetries) break;
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    delay *= 2;
                    continue;
                }
                catch (HttpRequestException ex)
                {
                    lastError = new AnafError(AnafErrorKind.ServiceUnavailable, ex.Message);
                    if (attempt == _options.MaxRetries) break;
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    delay *= 2;
                    continue;
                }
                finally
                {
                    LastCallPerCompany[company] = Clock();
                }

                var error = AnafEnvelope.DetectError(raw);
                if (error is null) return AnafResult<RawAnafResponse>.Success(raw);

                lastError = error;

                // Only rate limits and outages are worth retrying; a rights problem or an
                // exhausted daily budget will answer the same way however often it is asked.
                if (!error.IsTransient || attempt == _options.MaxRetries) break;

                _logger.LogWarning(
                    "ANAF returned {Kind} for CIF {Cif}; retrying in {Delay}.", error.Kind, company, delay);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay *= 2;
            }

            return AnafResult<RawAnafResponse>.Failure(lastError!);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Waits out the minimum gap between two calls for the same company.</summary>
    private async Task PaceAsync(string company, CancellationToken cancellationToken)
    {
        if (_options.MinimumDelayBetweenCalls <= TimeSpan.Zero) return;
        if (!LastCallPerCompany.TryGetValue(company, out var last)) return;

        var elapsed = Clock() - last;
        if (elapsed < _options.MinimumDelayBetweenCalls)
        {
            await Task.Delay(_options.MinimumDelayBetweenCalls - elapsed, cancellationToken).ConfigureAwait(false);
        }
    }

    // ---------------------------------------------------------------- parsing

    private static AnafResult<T> ParseXml<T>(RawAnafResponse raw, Func<XElement, AnafResult<T>> parse)
    {
        try
        {
            return parse(XDocument.Parse(raw.Text).Root!);
        }
        catch (System.Xml.XmlException ex)
        {
            return AnafResult<T>.Failure(new AnafError(
                AnafErrorKind.Unreadable, $"ANAF returned unparseable XML: {ex.Message}",
                RawBody: Head(raw.Text)));
        }
    }

    private static AnafResult<T> ParseJson<T>(RawAnafResponse raw, Func<JsonElement, AnafResult<T>> parse)
    {
        try
        {
            using var document = JsonDocument.Parse(raw.Text);
            return parse(document.RootElement);
        }
        catch (JsonException ex)
        {
            return AnafResult<T>.Failure(new AnafError(
                AnafErrorKind.Unreadable, $"ANAF returned unparseable JSON: {ex.Message}",
                RawBody: Head(raw.Text)));
        }
    }

    private static IReadOnlyList<AnafMessage> ReadMessages(JsonElement root)
    {
        if (!root.TryGetProperty("mesaje", out var messages) || messages.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. messages.EnumerateArray().Select(m => new AnafMessage(
                // ANAF has been observed emitting this as both a string and a number.
                Id: ReadLoose(m, "id"),
                RequestId: ReadLoose(m, "id_solicitare") ?? string.Empty,
                Cif: ReadLoose(m, "cif") ?? string.Empty,
                Type: ReadLoose(m, "tip") ?? string.Empty,
                Details: ReadLoose(m, "detalii") ?? string.Empty,
                SupplierCif: ReadLoose(m, "cif_emitent"),
                CustomerCif: ReadLoose(m, "cif_beneficiar"),
                CreatedAt: ParseAnafTimestamp(ReadLoose(m, "data_creare")))),
        ];
    }

    /// <summary>
    /// Reads a property as a string whatever JSON type it arrives as. ANAF is inconsistent about
    /// quoting numeric identifiers, and <c>GetString</c> throws on a number.
    /// </summary>
    private static string? ReadLoose(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    /// <summary>
    /// Parses ANAF's <c>yyyyMMddHHmm</c> timestamps, which arrive as strings rather than ISO dates.
    /// </summary>
    private static DateTimeOffset? ParseAnafTimestamp(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && DateTimeOffset.TryParseExact(
            value, AnafTimestampFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Whether the result is ANAF's way of saying the inbox is empty, which it reports through the
    /// same field it uses for genuine failures.
    /// </summary>
    private static bool IsEmptyInbox(AnafResult<RawAnafResponse> result) =>
        !result.IsSuccess
        && result.Error!.Message.Contains("nu exista mesaje", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves, normalises and checks the company code. ANAF's API rejects the <c>RO</c> prefix,
    /// so it is stripped on every call.
    /// </summary>
    /// <remarks>
    /// Checked here rather than left to ANAF because a malformed CIF otherwise travels all the way
    /// to Bucharest and comes back as a sentence in Romanian, spending a call from the daily
    /// allowance to say what the control digit already said. It is also the value that keys the
    /// per-company pacing gate below, and a value that is not a company is not one worth pacing.
    /// </remarks>
    private string ResolveCif(string? cif)
    {
        var supplied = string.IsNullOrWhiteSpace(cif) ? _options.Cif : cif;
        var resolved = RomanianCif.Normalize(supplied);

        if (string.IsNullOrEmpty(resolved))
        {
            throw new InvalidOperationException(
                "No CIF was supplied and none is configured. Set EFacturaOptions.Cif or pass one per call.");
        }

        if (!RomanianCif.IsValid(resolved))
        {
            throw new ArgumentException(
                $"'{supplied}' is not a valid Romanian fiscal code - the control digit does not match. "
                + "ANAF would refuse it, at the cost of a call from the daily allowance.",
                nameof(cif));
        }

        return resolved;
    }

    private Uri BuildUri(string path, Dictionary<string, string?> query)
    {
        var baseAddress = _options.ResolvedApiBaseAddress.AbsoluteUri.TrimEnd('/');
        var queryString = string.Join("&", query
            .Where(kvp => !string.IsNullOrEmpty(kvp.Value))
            .Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value!)}"));

        return new Uri($"{baseAddress}/{path}?{queryString}");
    }

    private static void AddFilter(Dictionary<string, string?> query, MessageFilter filter)
    {
        var value = filter switch
        {
            MessageFilter.Errors => "E",
            MessageFilter.Sent => "T",
            MessageFilter.Received => "P",
            MessageFilter.BuyerMessages => "R",
            _ => null,
        };

        if (value is not null) query["filtru"] = value;
    }

    private static string ValidationStandard(AnafStandard standard) => standard switch
    {
        AnafStandard.CreditNote => "FCN",
        _ => "FACT1",
    };

    private static string Head(string text) => text.Length <= 500 ? text : text[..500] + "…";
}
