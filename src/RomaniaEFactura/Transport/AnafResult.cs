namespace RomaniaEFactura.Transport;

/// <summary>What went wrong with an ANAF call.</summary>
/// <remarks>
/// Classified rather than left as free text, because the caller's response differs sharply by
/// kind: a rate limit is worth retrying, an expired authorization needs a human with a
/// certificate, and an exhausted daily quota means waiting until tomorrow.
/// </remarks>
public enum AnafErrorKind
{
    /// <summary>The cause could not be classified. The message carries ANAF's own wording.</summary>
    Unknown = 0,

    /// <summary>No valid access token. Someone must re-authorize with a qualified certificate.</summary>
    NotAuthorized,

    /// <summary>The account has no SPV rights for the requested CIF.</summary>
    NoRights,

    /// <summary>The document or identifier does not exist.</summary>
    NotFound,

    /// <summary>ANAF refused the submission outright — a bad standard, an oversized file.</summary>
    Rejected,

    /// <summary>The request was malformed. A programming error rather than a business outcome.</summary>
    InvalidRequest,

    /// <summary>Too many requests. Worth retrying after a pause.</summary>
    RateLimited,

    /// <summary>
    /// The daily call allowance for this identifier is spent. Unlike a rate limit this does not
    /// clear in seconds — the budget resets the next day.
    /// </summary>
    QuotaExhausted,

    /// <summary>ANAF is unavailable. Frequent enough in practice to deserve its own kind.</summary>
    ServiceUnavailable,

    /// <summary>The response could not be understood at all.</summary>
    Unreadable,
}

/// <summary>An error reported by ANAF, or by the transport on its behalf.</summary>
/// <param name="Kind">The classification a caller branches on.</param>
/// <param name="Message">ANAF's own wording, preserved verbatim where it supplied any.</param>
/// <param name="StatusCode">The HTTP status, which for most ANAF failures is 200.</param>
/// <param name="RawBody">The unparsed body, truncated, for diagnosing a surprise.</param>
public sealed record AnafError(
    AnafErrorKind Kind,
    string Message,
    int StatusCode = 200,
    string? RawBody = null)
{
    /// <summary>Whether retrying the same call unchanged could plausibly succeed.</summary>
    public bool IsTransient => Kind is AnafErrorKind.RateLimited or AnafErrorKind.ServiceUnavailable;

    /// <inheritdoc />
    public override string ToString() => $"{Kind}: {Message}";
}

/// <summary>
/// The outcome of an ANAF call: either a value, or a classified error.
/// </summary>
/// <remarks>
/// ANAF and business failures are returned rather than thrown. They are ordinary, expected
/// outcomes — nobody has authorized this CIF yet, the daily budget is spent, the service is down —
/// and a caller has to handle them on a page. Exceptions stay for programming errors.
/// </remarks>
public readonly struct AnafResult<T>
{
    private readonly T? _value;

    private AnafResult(T value)
    {
        _value = value;
        Error = null;
    }

    private AnafResult(AnafError error)
    {
        _value = default;
        Error = error;
    }

    /// <summary>The error, when the call did not succeed.</summary>
    public AnafError? Error { get; }

    /// <summary>Whether the call succeeded.</summary>
    public bool IsSuccess => Error is null;

    /// <summary>The value. Throws when the call failed, so check <see cref="IsSuccess"/> first.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"The call failed: {Error}");

    /// <summary>Creates a successful result.</summary>
    public static AnafResult<T> Success(T value) => new(value);

    /// <summary>Creates a failed result.</summary>
    public static AnafResult<T> Failure(AnafError error) => new(error);

    /// <summary>Carries an error across to a differently-typed result.</summary>
    public AnafResult<TOther> CarryError<TOther>() => Error is null
        ? throw new InvalidOperationException("There is no error to carry.")
        : AnafResult<TOther>.Failure(Error);

    /// <summary>Projects the value, leaving an error untouched.</summary>
    public AnafResult<TOther> Map<TOther>(Func<T, TOther> map) => IsSuccess
        ? AnafResult<TOther>.Success(map(_value!))
        : AnafResult<TOther>.Failure(Error!);

    /// <inheritdoc />
    public override string ToString() => IsSuccess ? $"ok: {_value}" : Error!.ToString();
}
