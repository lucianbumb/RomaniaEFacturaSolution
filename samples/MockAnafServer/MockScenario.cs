namespace MockAnafServer;

/// <summary>
/// Forces a specific ANAF behaviour for one request.
/// </summary>
/// <remarks>
/// Sent as the <c>X-Mock-Scenario</c> header so a test can drive a failure path without having to
/// manoeuvre the mock's state into position. Everything here is a behaviour ANAF genuinely
/// exhibits — the header chooses which one, it does not invent any.
/// </remarks>
public enum MockScenario
{
    /// <summary>Behave normally.</summary>
    None = 0,

    /// <summary>Reject the upload for lack of SPV rights on the requested CIF.</summary>
    NoSpvRights,

    /// <summary>Reject the upload as larger than the 10 MB limit.</summary>
    FileTooLarge,

    /// <summary>Report the upload as accepted but destined to fail validation.</summary>
    UploadWillFailValidation,

    /// <summary>Report the document as still processing, however many times it is polled.</summary>
    StuckInProcessing,

    /// <summary>Report that the caller has no right to query or download this document.</summary>
    NotEntitled,

    /// <summary>Report the daily call quota for this identifier as already spent.</summary>
    QuotaExhausted,

    /// <summary>Answer with an empty message list, using ANAF's "Nu exista mesaje" wording.</summary>
    NoMessages,

    /// <summary>Return HTTP 429, as ANAF does under load.</summary>
    RateLimited,

    /// <summary>Return HTTP 500.</summary>
    ServerError,

    /// <summary>Wrap the document in a nested ZIP, which real archives sometimes are.</summary>
    NestedArchive,

    /// <summary>Include a ready-made PDF in the archive alongside the XML.</summary>
    ArchiveWithPdf,

    /// <summary>Return the PDF conversion base64-encoded rather than as raw bytes.</summary>
    Base64Pdf,

    /// <summary>Expire the access token, so the request answers 401.</summary>
    TokenExpired,
}

/// <summary>Reads the scenario header off a request.</summary>
public static class MockScenarioExtensions
{
    /// <summary>The header a test sets to force a behaviour.</summary>
    public const string HeaderName = "X-Mock-Scenario";

    /// <summary>
    /// The scenario requested, or <see cref="MockScenario.None"/> when the header is absent or
    /// unrecognised.
    /// </summary>
    public static MockScenario Scenario(this HttpRequest request) =>
        request.Headers.TryGetValue(HeaderName, out var values)
        && Enum.TryParse<MockScenario>(values.ToString(), ignoreCase: true, out var scenario)
            ? scenario
            : MockScenario.None;
}
