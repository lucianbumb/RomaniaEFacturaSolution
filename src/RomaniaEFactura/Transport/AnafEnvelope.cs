using System.Net;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace RomaniaEFactura.Transport;

/// <summary>A buffered ANAF response, held as bytes so it can be inspected before being parsed.</summary>
/// <param name="StatusCode">The HTTP status.</param>
/// <param name="Body">The complete body.</param>
public sealed record RawAnafResponse(HttpStatusCode StatusCode, byte[] Body)
{
    private string? _text;

    /// <summary>The body decoded as UTF-8, with any byte order mark removed.</summary>
    public string Text => _text ??= new UTF8Encoding(false).GetString(Body).TrimStart('﻿');

    /// <summary>Whether the body is a ZIP archive, identified by its magic number.</summary>
    public bool IsZip => Body.Length > 4 && Body[0] == 'P' && Body[1] == 'K' && Body[2] == 3 && Body[3] == 4;

    /// <summary>Whether the body is a PDF, identified by its magic number.</summary>
    public bool IsPdf => Body.Length > 4 && Body[0] == '%' && Body[1] == 'P' && Body[2] == 'D' && Body[3] == 'F';
}

/// <summary>
/// The one place an ANAF response is judged successful or not.
/// </summary>
/// <remarks>
/// <para>
/// <b>ANAF signals failure inside HTTP 200 responses, on every endpoint.</b> Upload answers
/// <c>ExecutionStatus="1"</c> with <c>Errors</c> children; <c>stareMesaj</c> an <c>Errors</c>
/// element; the list endpoints an <c>eroare</c> field; and <c>descarcare</c> a JSON error body
/// where a ZIP was expected. Branching on <c>IsSuccessStatusCode</c> therefore reads every one of
/// them as success — which is precisely what the previous version of this library did, and why its
/// download path surfaced failures as an opaque <c>InvalidDataException</c> from
/// <c>ZipArchive</c> with ANAF's actual explanation discarded.
/// </para>
/// <para>
/// Detection is therefore content-based, not status-based. Nothing else in the library may decide
/// whether an ANAF response succeeded; every call goes through <see cref="DetectError"/> first.
/// </para>
/// </remarks>
internal static class AnafEnvelope
{
    /// <summary>Buffers a response so its body can be sniffed and then parsed.</summary>
    public static async Task<RawAnafResponse> BufferAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return new RawAnafResponse(response.StatusCode, body);
    }

    /// <summary>
    /// Classifies a response, returning <see langword="null"/> when it carries no error.
    /// </summary>
    public static AnafError? DetectError(RawAnafResponse raw)
    {
        // Transport-level failures do use real status codes.
        if (raw.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new AnafError(AnafErrorKind.NotAuthorized,
                ExtractJsonMessage(raw.Text) ?? "The access token is missing, expired or rejected.",
                (int)raw.StatusCode, Truncate(raw.Text));
        }

        if (raw.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new AnafError(AnafErrorKind.RateLimited, "ANAF is rate limiting this client.",
                (int)raw.StatusCode, Truncate(raw.Text));
        }

        if ((int)raw.StatusCode >= 500)
        {
            return new AnafError(AnafErrorKind.ServiceUnavailable,
                $"ANAF returned {(int)raw.StatusCode}.", (int)raw.StatusCode, Truncate(raw.Text));
        }

        if (raw.StatusCode == HttpStatusCode.BadRequest)
        {
            return new AnafError(AnafErrorKind.InvalidRequest,
                ExtractJsonMessage(raw.Text) ?? "ANAF rejected the request as malformed.",
                (int)raw.StatusCode, Truncate(raw.Text));
        }

        // Anything binary is a payload, not an error.
        if (raw.IsZip || raw.IsPdf) return null;

        var text = raw.Text.TrimStart();
        if (text.Length == 0)
        {
            return new AnafError(AnafErrorKind.Unreadable, "ANAF returned an empty body.",
                (int)raw.StatusCode);
        }

        return text[0] switch
        {
            '<' => DetectXmlError(raw, text),
            '{' or '[' => DetectJsonError(raw, text),
            _ => null,
        };
    }

    private static AnafError? DetectXmlError(RawAnafResponse raw, string text)
    {
        XElement root;
        try
        {
            root = XDocument.Parse(text).Root!;
        }
        catch (System.Xml.XmlException ex)
        {
            return new AnafError(AnafErrorKind.Unreadable,
                $"ANAF returned XML that could not be parsed: {ex.Message}",
                (int)raw.StatusCode, Truncate(text));
        }

        // Errors children appear on both the upload and stareMesaj envelopes.
        var messages = root.Elements()
            .Where(e => string.Equals(e.Name.LocalName, "Errors", StringComparison.Ordinal))
            .Select(e => e.Attribute("errorMessage")?.Value)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m!)
            .ToList();

        // Upload also flags failure numerically, and can do so with no Errors child at all.
        var executionStatus = root.Attribute("ExecutionStatus")?.Value;
        var failed = messages.Count > 0
            || (executionStatus is not null && !string.Equals(executionStatus, "0", StringComparison.Ordinal));

        if (!failed) return null;

        var message = messages.Count > 0
            ? string.Join("; ", messages)
            : "ANAF rejected the document without giving a reason.";

        return new AnafError(Classify(message, AnafErrorKind.Rejected), message,
            (int)raw.StatusCode, Truncate(text));
    }

    private static AnafError? DetectJsonError(RawAnafResponse raw, string text)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(text).RootElement;
        }
        catch (JsonException ex)
        {
            return new AnafError(AnafErrorKind.Unreadable,
                $"ANAF returned JSON that could not be parsed: {ex.Message}",
                (int)raw.StatusCode, Truncate(text));
        }

        if (root.ValueKind != JsonValueKind.Object) return null;

        // The list endpoints and descarcare both report failure through "eroare".
        if (root.TryGetProperty("eroare", out var eroare)
            && eroare.ValueKind == JsonValueKind.String
            && eroare.GetString() is { Length: > 0 } message)
        {
            return new AnafError(Classify(message, AnafErrorKind.Unknown), message,
                (int)raw.StatusCode, Truncate(text));
        }

        // The Spring-style error body, which normally accompanies a 400 but is checked here too.
        if (root.TryGetProperty("status", out var status)
            && root.TryGetProperty("message", out var springMessage)
            && springMessage.ValueKind == JsonValueKind.String)
        {
            var code = status.ValueKind == JsonValueKind.Number ? status.GetInt32()
                : int.TryParse(status.GetString(), out var parsed) ? parsed
                : (int)raw.StatusCode;

            var kind = code == 401 ? AnafErrorKind.NotAuthorized : AnafErrorKind.InvalidRequest;
            return new AnafError(kind, springMessage.GetString()!, code, Truncate(text));
        }

        return null;
    }

    /// <summary>
    /// Refines a classification from ANAF's own wording, which is the only signal distinguishing a
    /// spent daily quota from a rights problem — both arrive as plain sentences.
    /// </summary>
    private static AnafErrorKind Classify(string message, AnafErrorKind fallback)
    {
        if (message.Contains("in cursul zilei", StringComparison.OrdinalIgnoreCase))
        {
            return AnafErrorKind.QuotaExhausted;
        }

        if (message.Contains("drept", StringComparison.OrdinalIgnoreCase))
        {
            return AnafErrorKind.NoRights;
        }

        if (message.Contains("nu exista", StringComparison.OrdinalIgnoreCase))
        {
            return AnafErrorKind.NotFound;
        }

        if (message.Contains("nu este un numar", StringComparison.OrdinalIgnoreCase)
            || message.Contains("obligatoriu", StringComparison.OrdinalIgnoreCase)
            || message.Contains("obligatorii", StringComparison.OrdinalIgnoreCase)
            || message.Contains("valorile acceptate", StringComparison.OrdinalIgnoreCase))
        {
            return AnafErrorKind.InvalidRequest;
        }

        return fallback;
    }

    private static string? ExtractJsonMessage(string text)
    {
        try
        {
            var root = JsonDocument.Parse(text).RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            foreach (var name in (string[])["message", "eroare", "error_description", "error"])
            {
                if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON; the caller falls back to a generic message.
        }

        return null;
    }

    private static string Truncate(string text) =>
        text.Length <= 2000 ? text : text[..2000] + "…";
}
