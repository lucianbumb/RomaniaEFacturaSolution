namespace RomaniaEFactura.Transport;

/// <summary>What ANAF returns when it accepts an upload.</summary>
/// <param name="UploadIndex">
/// The <c>index_incarcare</c>. This must be persisted: it is the only handle on the submission,
/// and without it the outcome can never be reconciled.
/// </param>
/// <param name="ResponseDate">When ANAF recorded the upload.</param>
public sealed record UploadReceipt(string UploadIndex, DateTimeOffset? ResponseDate);

/// <summary>Where a submitted document has got to.</summary>
public enum UploadState
{
    /// <summary>Still being processed. No response is available yet.</summary>
    InProgress = 0,

    /// <summary>Validated and delivered. The buyer can see it.</summary>
    Ok,

    /// <summary>Rejected. The response holds the errors, and the buyer never receives it.</summary>
    Nok,

    /// <summary>Refused at upload; the reason came back from the upload call itself.</summary>
    RejectedAtUpload,
}

/// <summary>The status of a submitted document.</summary>
/// <param name="State">How far the document has got.</param>
/// <param name="DownloadId">
/// The identifier <c>descarcare</c> needs. Present once processing has finished, for a rejection
/// as well as an acceptance — a rejected document still has a downloadable error response.
/// </param>
/// <param name="RawState">ANAF's own wording, kept for diagnosis.</param>
public sealed record MessageStatus(UploadState State, string? DownloadId, string RawState)
{
    /// <summary>Whether processing has finished, either way.</summary>
    public bool IsComplete => State is not UploadState.InProgress;
}

/// <summary>A message in the SPV inbox.</summary>
/// <param name="Id">
/// The download identifier. Null when ANAF omitted it, in which case it has to be resolved through
/// <see cref="MessageStatus"/> using <paramref name="RequestId"/>.
/// </param>
/// <param name="RequestId">The <c>id_solicitare</c>, which is the upload index for sent documents.</param>
/// <param name="Cif">The company the message belongs to.</param>
/// <param name="Type">The message type, as ANAF words it.</param>
/// <param name="Details">ANAF's description of the message.</param>
/// <param name="SupplierCif">The seller's fiscal code.</param>
/// <param name="CustomerCif">The buyer's fiscal code.</param>
/// <param name="CreatedAt">When the message was created.</param>
public sealed record AnafMessage(
    string? Id,
    string RequestId,
    string Cif,
    string Type,
    string Details,
    string? SupplierCif,
    string? CustomerCif,
    DateTimeOffset? CreatedAt)
{
    /// <summary>Whether the download identifier must be resolved before this can be fetched.</summary>
    public bool NeedsIdResolution => string.IsNullOrEmpty(Id);

    /// <summary>Whether this message carries validation errors rather than a document.</summary>
    public bool IsError => Type.Contains("ERORI", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One page of the paginated message list.</summary>
/// <param name="Messages">The messages on this page.</param>
/// <param name="Page">The page number requested.</param>
/// <param name="TotalPages">How many pages exist in total.</param>
public sealed record MessagePage(IReadOnlyList<AnafMessage> Messages, int Page, int TotalPages)
{
    /// <summary>Whether another page follows this one.</summary>
    public bool HasMore => Page < TotalPages;
}

/// <summary>What ANAF's online validator concluded.</summary>
/// <param name="IsValid">Whether the document passed.</param>
/// <param name="Messages">The problems found, when it did not.</param>
/// <param name="TraceId">ANAF's correlation identifier, useful when raising a support query.</param>
public sealed record AnafValidationOutcome(
    bool IsValid,
    IReadOnlyList<string> Messages,
    string? TraceId);

/// <summary>Which document standard a payload is being sent or validated as.</summary>
public enum AnafStandard
{
    /// <summary>A UBL invoice.</summary>
    Ubl = 0,

    /// <summary>A UBL credit note.</summary>
    CreditNote,

    /// <summary>A CII invoice.</summary>
    Cii,

    /// <summary>A message from the buyer back to the seller.</summary>
    BuyerMessage,
}

/// <summary>Options that vary per submission.</summary>
/// <param name="Standard">Which document standard is being sent.</param>
/// <param name="B2C">
/// Whether to use the B2C endpoint, mandatory for consumer invoices since 2025.
/// </param>
/// <param name="Foreign">The buyer is outside Romania and has no Romanian fiscal code.</param>
/// <param name="SelfBilled">The buyer issued the invoice on the seller's behalf.</param>
/// <param name="Enforcement">An enforcement body filed the document for the debtor.</param>
public sealed record UploadOptions(
    AnafStandard Standard = AnafStandard.Ubl,
    bool B2C = false,
    bool Foreign = false,
    bool SelfBilled = false,
    bool Enforcement = false)
{
    /// <summary>The value ANAF's <c>standard</c> parameter expects.</summary>
    public string StandardParameter => Standard switch
    {
        AnafStandard.Ubl => "UBL",
        AnafStandard.CreditNote => "CN",
        AnafStandard.Cii => "CII",
        AnafStandard.BuyerMessage => "RASP",
        _ => throw new ArgumentOutOfRangeException(nameof(Standard)),
    };
}

/// <summary>Filters the message list by message type.</summary>
public enum MessageFilter
{
    /// <summary>Everything.</summary>
    All = 0,

    /// <summary>Validation errors only.</summary>
    Errors,

    /// <summary>Documents this company sent.</summary>
    Sent,

    /// <summary>Documents this company received.</summary>
    Received,

    /// <summary>Buyer messages.</summary>
    BuyerMessages,
}
