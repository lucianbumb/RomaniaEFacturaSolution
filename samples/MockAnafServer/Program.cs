using System.Net.Mime;
using System.Text;
using MockAnafServer;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<MockAnafState>();

var app = builder.Build();

// ANAF mounts the API under /prod and /test with identical behaviour, so a client can be pointed
// at either by base address alone.
foreach (var env in new[] { "prod", "test" })
{
    MapEFactura(app.MapGroup($"/{env}/FCTEL/rest"));
}

MapOAuth(app.MapGroup("/anaf-oauth2/v1"));
MapControl(app.MapGroup("/__mock"));

app.MapGet("/", () => Results.Text(
    "Mock ANAF e-Factura server. See docs/anaf-wire-formats.md for the behaviour it reproduces."));

app.Run();

static void MapEFactura(RouteGroupBuilder group)
{
    // ---------------------------------------------------------------- upload
    foreach (var path in new[] { "/upload", "/uploadb2c" })
    {
        group.MapPost(path, async Task<IResult> (HttpRequest request, MockAnafState state) =>
        {
            if (Intercept(request, state) is { } intercepted) return intercepted;
            if (RequireToken(request) is { } unauthorized) return unauthorized;

            var now = state.Clock();
            var standard = request.Query["standard"].ToString();
            var cif = request.Query["cif"].ToString();

            // A genuine 400 - unlike the business failures below, which are 200s.
            if (string.IsNullOrEmpty(standard) || string.IsNullOrEmpty(cif))
            {
                return Results.Json(
                    AnafResponses.BadRequest("Parametrii standard si cif sunt obligatorii", now),
                    statusCode: StatusCodes.Status400BadRequest);
            }

            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            var xml = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(xml))
            {
                return Results.Json(
                    AnafResponses.BadRequest("Trebuie sa aveti atasat in request un fisier de tip xml", now),
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var scenario = request.Scenario();

            if (scenario == MockScenario.FileTooLarge || Encoding.UTF8.GetByteCount(xml) > 10 * 1024 * 1024)
            {
                return Xml(AnafResponses.UploadRejected(now, "Marime fisier transmis mai mare de 10 MB."));
            }

            if (scenario == MockScenario.NoSpvRights)
            {
                return Xml(AnafResponses.UploadRejected(now, $"Nu aveti drept in SPV pentru CIF={cif}"));
            }

            if (!string.Equals(standard, "UBL", StringComparison.Ordinal)
                && !string.Equals(standard, "CN", StringComparison.Ordinal)
                && !string.Equals(standard, "CII", StringComparison.Ordinal)
                && !string.Equals(standard, "RASP", StringComparison.Ordinal))
            {
                return Xml(AnafResponses.UploadRejected(
                    now, "Valorile acceptate pentru parametrul standard sunt UBL, CN, CII sau RASP"));
            }

            if (!cif.All(char.IsAsciiDigit))
            {
                return Xml(AnafResponses.UploadRejected(now, $"CIF introdus= {cif} nu este un numar"));
            }

            var upload = state.AddUpload(cif, standard, xml,
                willBeRejected: scenario == MockScenario.UploadWillFailValidation);

            return Xml(AnafResponses.UploadAccepted(upload.IndexIncarcare, now));
        });
    }

    // ------------------------------------------------------------ stareMesaj
    group.MapGet("/stareMesaj", IResult (HttpRequest request, MockAnafState state) =>
    {
        if (Intercept(request, state) is { } intercepted) return intercepted;
        if (RequireToken(request) is { } unauthorized) return unauthorized;

        var id = request.Query["id_incarcare"].ToString();
        if (string.IsNullOrEmpty(id))
        {
            return Results.Json(
                AnafResponses.BadRequest("Parametrul id_incarcare este obligatoriu", state.Clock()),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var scenario = request.Scenario();

        if (scenario == MockScenario.NotEntitled)
        {
            return Xml(AnafResponses.StatusError($"Nu aveti dreptul de inteorgare pentru id_incarcare= {id}"));
        }

        if (!id.All(char.IsAsciiDigit))
        {
            return Xml(AnafResponses.StatusError($"Id_incarcare introdus= {id} nu este un numar intreg"));
        }

        // The per-identifier daily cap. A client that polls on a fixed interval exhausts this and
        // then cannot see its own document for the rest of the day.
        if (scenario == MockScenario.QuotaExhausted
            || !state.TrySpendQuota("stareMesaj", id, state.StatusQuotaPerDay))
        {
            return Xml(AnafResponses.StatusError(
                $"S-au facut deja {state.StatusQuotaPerDay} descarcari de mesaj in cursul zilei"));
        }

        if (state.FindUpload(id) is not { } upload)
        {
            return Xml(AnafResponses.StatusError($"Nu exista factura cu id_incarcare= {id}"));
        }

        if (scenario == MockScenario.StuckInProcessing)
        {
            return Xml(AnafResponses.StatusPending("in prelucrare"));
        }

        state.Poll(upload);

        return upload.IdDescarcare is { } downloadId
            ? Xml(AnafResponses.StatusResolved(upload.Outcome, downloadId))
            : Xml(AnafResponses.StatusPending("in prelucrare"));
    });

    // ------------------------------------------------------------ lista mesaje
    group.MapGet("/listaMesajeFactura", IResult (HttpRequest request, MockAnafState state) =>
    {
        if (Intercept(request, state) is { } intercepted) return intercepted;
        if (RequireToken(request) is { } unauthorized) return unauthorized;

        var now = state.Clock();
        var cif = request.Query["cif"].ToString();
        var zileRaw = request.Query["zile"].ToString();

        if (string.IsNullOrEmpty(cif) || string.IsNullOrEmpty(zileRaw))
        {
            return Results.Json(
                AnafResponses.BadRequest("Parametrii zile si cif sunt obligatorii", now),
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!int.TryParse(zileRaw, out var zile))
        {
            return Results.Json(AnafResponses.MessageListError(
                $"Numarul de zile introdus= {zileRaw} nu este un numar intreg"));
        }

        if (zile is < 1 or > 60)
        {
            return Results.Json(AnafResponses.MessageListError(
                "Numarul de zile trebuie sa fie intre 1 si 60"));
        }

        if (FilterError(request) is { } filterError) return filterError;

        var messages = state.MessagesFor(cif, now.AddDays(-zile), now);
        return ListResult(request, state, messages, totalPages: null);
    });

    group.MapGet("/listaMesajePaginatieFactura", IResult (HttpRequest request, MockAnafState state) =>
    {
        if (Intercept(request, state) is { } intercepted) return intercepted;
        if (RequireToken(request) is { } unauthorized) return unauthorized;

        var now = state.Clock();
        var cif = request.Query["cif"].ToString();
        var startRaw = request.Query["startTime"].ToString();
        var endRaw = request.Query["endTime"].ToString();
        var pageRaw = request.Query["pagina"].ToString();

        if (string.IsNullOrEmpty(cif) || string.IsNullOrEmpty(startRaw)
            || string.IsNullOrEmpty(endRaw) || string.IsNullOrEmpty(pageRaw))
        {
            return Results.Json(
                AnafResponses.BadRequest(
                    "Parametrii startTime, endTime, cif si pagina sunt obligatorii", now),
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (!long.TryParse(startRaw, out var startMs))
        {
            return Results.Json(AnafResponses.MessageListError(
                $"startTime = {startRaw} nu este un numar sau nu are o valoare acceptata de sistem"));
        }

        if (!long.TryParse(endRaw, out var endMs))
        {
            return Results.Json(AnafResponses.MessageListError(
                $"endTime = {endRaw} nu este un numar sau nu are o valoare acceptata de sistem"));
        }

        if (!int.TryParse(pageRaw, out var page) || page < 1)
        {
            return Results.Json(AnafResponses.MessageListError(
                $"pagina = {pageRaw} nu este un numar sau nu are o valoare acceptata de sistem"));
        }

        var start = DateTimeOffset.FromUnixTimeMilliseconds(startMs);
        var end = DateTimeOffset.FromUnixTimeMilliseconds(endMs);

        // The 60-day limit applies here too, even though the endpoint takes arbitrary timestamps.
        if (start < now.AddDays(-60))
        {
            return Results.Json(AnafResponses.MessageListError(
                $"startTime = {start:dd-MM-yyyy HH:mm:ss} nu poate fi mai vechi de 60 de zile fata de momentul requestului"));
        }

        if (FilterError(request) is { } filterError) return filterError;

        const int pageSize = 5;
        var all = state.MessagesFor(cif, start, end);
        var totalPages = Math.Max(1, (int)Math.Ceiling(all.Count / (double)pageSize));
        var slice = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return ListResult(request, state, slice, totalPages);
    });

    // ------------------------------------------------------------- descarcare
    group.MapGet("/descarcare", IResult (HttpRequest request, MockAnafState state) =>
    {
        if (Intercept(request, state) is { } intercepted) return intercepted;
        if (RequireToken(request) is { } unauthorized) return unauthorized;

        var id = request.Query["id"].ToString();
        if (string.IsNullOrEmpty(id))
        {
            return Results.Json(
                AnafResponses.BadRequest("Parametrul id este obligatoriu", state.Clock()),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var scenario = request.Scenario();

        // Every failure below answers HTTP 200 with a JSON body, where the caller expected a ZIP.
        if (!id.All(char.IsAsciiDigit))
        {
            return Results.Json(AnafResponses.DownloadError(
                $"Id descarcare introdus= {id} nu este un numar intreg"));
        }

        if (scenario == MockScenario.NotEntitled)
        {
            return Results.Json(AnafResponses.DownloadError("Nu aveti dreptul sa descarcati acesta factura"));
        }

        if (scenario == MockScenario.QuotaExhausted
            || !state.TrySpendQuota("descarcare", id, state.DownloadQuotaPerDay))
        {
            return Results.Json(AnafResponses.DownloadError(
                $"S-au facut deja {state.DownloadQuotaPerDay} descarcari de mesaj in cursul zilei"));
        }

        if (state.FindMessage(id) is not { } message)
        {
            return Results.Json(AnafResponses.DownloadError(
                $"Pentru id={id} nu exista inregistrata nici o factura"));
        }

        return Results.File(
            ArchiveBuilder.Build(message, scenario),
            MediaTypeNames.Application.Zip,
            $"{id}.zip");
    });

    // --------------------------------------------------------------- validare
    group.MapPost("/validare/{standard}", async Task<IResult> (string standard, HttpRequest request) =>
    {
        if (standard is not ("FACT1" or "FCN"))
        {
            return Results.Json(AnafResponses.ValidationResult(
                false, $"Standardul {standard} nu este acceptat"));
        }

        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var xml = await reader.ReadToEndAsync();

        // Deliberately shallow: the mock is not a validator. Real validation is proven against
        // ANAF's own jar in the library's test suite, not here.
        var looksLikeDocument = xml.Contains("<Invoice", StringComparison.Ordinal)
                             || xml.Contains("<CreditNote", StringComparison.Ordinal);

        return Results.Json(looksLikeDocument
            ? AnafResponses.ValidationResult(true)
            : AnafResponses.ValidationResult(false, "Fisierul transmis nu este valid."));
    });

    // ------------------------------------------------------------ transformare
    group.MapPost("/transformare/{standard}/{novld?}", async Task<IResult> (HttpRequest request) =>
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var xml = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(xml))
        {
            return Results.Json(AnafResponses.ValidationResult(false, "Fisierul transmis nu este valid."));
        }

        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% mock rendering\n");

        // ANAF sometimes answers with base64 text instead of raw bytes; a client must handle both.
        return request.Scenario() == MockScenario.Base64Pdf
            ? Results.Text(Convert.ToBase64String(pdf), MediaTypeNames.Text.Plain)
            : Results.File(pdf, MediaTypeNames.Application.Pdf, "factura.pdf");
    });

    // ----------------------------------------------------- validate/signature
    group.MapPost("/validate/signature", () =>
        Results.Json(new Dictionary<string, string> { ["msg"] = "Semnatura este valida" }));
}

static void MapOAuth(RouteGroupBuilder group)
{
    // The real authorize step needs a qualified certificate in a browser and cannot be automated.
    // The mock redirects straight back with a code so the rest of the flow is testable.
    group.MapGet("/authorize", IResult (HttpRequest request) =>
    {
        var redirectUri = request.Query["redirect_uri"].ToString();
        var stateParam = request.Query["state"].ToString();

        if (string.IsNullOrEmpty(redirectUri)) return Results.BadRequest("redirect_uri is required");

        var separator = redirectUri.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return Results.Redirect(
            $"{redirectUri}{separator}code=mock-authorization-code&state={Uri.EscapeDataString(stateParam)}");
    });

    group.MapPost("/token", async Task<IResult> (HttpRequest request) =>
    {
        var form = await request.ReadFormAsync();
        var grantType = form["grant_type"].ToString();

        // ANAF requires client credentials as HTTP Basic, not in the body.
        if (!request.Headers.Authorization.ToString().StartsWith("Basic ", StringComparison.Ordinal))
        {
            return Results.Json(
                new Dictionary<string, string>
                {
                    ["error"] = "invalid_client",
                    ["error_description"] = "Client authentication failed",
                },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (grantType is not ("authorization_code" or "refresh_token"))
        {
            return Results.Json(
                new Dictionary<string, string> { ["error"] = "unsupported_grant_type" },
                statusCode: StatusCodes.Status400BadRequest);
        }

        var suffix = grantType == "refresh_token" ? "refreshed" : "initial";

        // Lifetimes mirror the real ones: the access token lasts about 90 days and the refresh
        // token about a year, which is why losing the refresh token is so costly.
        return Results.Json(new Dictionary<string, object>
        {
            ["access_token"] = $"mock-access-token-{suffix}",
            ["token_type"] = "Bearer",
            ["expires_in"] = 7_776_000,
            ["refresh_token"] = $"mock-refresh-token-{suffix}",
            ["scope"] = "efactura",
        });
    });
}

static void MapControl(RouteGroupBuilder group)
{
    // Test-only controls. Nothing here corresponds to a real ANAF endpoint.
    group.MapPost("/reset", (MockAnafState state) =>
    {
        state.Reset();
        return Results.Ok(new { reset = true });
    });

    group.MapPost("/polls-before-resolution/{count:int}", (int count, MockAnafState state) =>
    {
        state.PollsBeforeResolution = count;
        return Results.Ok(new { pollsBeforeResolution = count });
    });

    group.MapPost("/messages", (IncomingMessageRequest body, MockAnafState state) =>
    {
        var message = state.AddIncomingMessage(
            body.Cif,
            body.Xml,
            body.Tip ?? "FACTURA PRIMITA",
            body.CifEmitent,
            body.HideId,
            body.CreatedDaysAgo is { } days ? state.Clock().AddDays(-days) : null);

        return Results.Ok(new { message.Id, message.IdSolicitare, message.HideId });
    });
}

// ------------------------------------------------------------------- helpers

/// <summary>
/// Applies the transport-level scenarios that short-circuit any endpoint.
/// </summary>
static IResult? Intercept(HttpRequest request, MockAnafState state) => request.Scenario() switch
{
    MockScenario.RateLimited => Results.StatusCode(StatusCodes.Status429TooManyRequests),
    MockScenario.ServerError => Results.StatusCode(StatusCodes.Status500InternalServerError),
    MockScenario.TokenExpired => Unauthorized(),
    _ => null,
};

/// <summary>
/// Writes an XML body. ANAF returns <c>application/xml</c> for upload and stareMesaj, including
/// when reporting a failure, so this is used for both.
/// </summary>
static IResult Xml(string body) => Results.Text(body, "application/xml", Encoding.UTF8);

static IResult? RequireToken(HttpRequest request) =>
    request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal)
        ? null
        : Unauthorized();

/// <summary>
/// The one place ANAF genuinely uses an error status for an auth failure, and it is JSON.
/// </summary>
static IResult Unauthorized() => Results.Json(
    new Dictionary<string, string> { ["message"] = "Unauthorized", ["status"] = "401" },
    statusCode: StatusCodes.Status401Unauthorized);

static IResult? FilterError(HttpRequest request)
{
    var filtru = request.Query["filtru"].ToString();
    return string.IsNullOrEmpty(filtru) || filtru is "E" or "T" or "P" or "R"
        ? null
        : Results.Json(AnafResponses.MessageListError(
            "Valorile acceptate pentru parametrul filtru sunt E, T, P sau R"));
}

static IResult ListResult(
    HttpRequest request,
    MockAnafState state,
    IReadOnlyList<MessageRecord> messages,
    int? totalPages)
{
    var filtru = request.Query["filtru"].ToString();
    var filtered = filtru switch
    {
        "E" => messages.Where(m => m.Tip == "ERORI FACTURA"),
        "T" => messages.Where(m => m.Tip == "FACTURA TRIMISA"),
        "P" => messages.Where(m => m.Tip == "FACTURA PRIMITA"),
        "R" => messages.Where(m => m.Tip.StartsWith("MESAJ CUMPARATOR", StringComparison.Ordinal)),
        _ => messages,
    };

    var list = filtered.ToList();

    // An empty result arrives through the same "eroare" field as a genuine failure. A client that
    // treats any "eroare" as a fault will report an error every time there is simply no post.
    if (request.Scenario() == MockScenario.NoMessages || list.Count == 0)
    {
        return Results.Json(AnafResponses.MessageListError("Nu exista mesaje in intervalul selectat"));
    }

    var payload = list.Select(m =>
    {
        var entry = new Dictionary<string, object>
        {
            ["data_creare"] = m.Created.ToString(AnafResponses.TimestampFormat),
            ["cif"] = m.Cif,
            ["id_solicitare"] = m.IdSolicitare,
            ["detalii"] = m.Detalii,
            ["tip"] = m.Tip,
            ["cif_emitent"] = m.CifEmitent,
            ["cif_beneficiar"] = m.CifBeneficiar,
        };

        // Some messages carry no id at all, forcing a stareMesaj round-trip to resolve one.
        if (!m.HideId) entry["id"] = m.Id;

        return (object)entry;
    });

    return Results.Json(AnafResponses.MessageList(payload, totalPages));
}

/// <summary>Body of the test-only endpoint that seeds an inbound message.</summary>
internal sealed record IncomingMessageRequest(
    string Cif,
    string Xml,
    string? Tip = null,
    string? CifEmitent = null,
    bool HideId = false,
    int? CreatedDaysAgo = null);

/// <summary>Exposed so the integration tests can host this server in-process.</summary>
public partial class Program;
