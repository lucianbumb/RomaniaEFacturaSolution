using RomaniaEFactura.EditModels;

namespace RomaniaEFactura.Lookup;

/// <summary>
/// A company as ANAF's register describes it.
/// </summary>
/// <remarks>
/// Comes from ANAF's public taxpayer register, which is a different service from the e-Factura API
/// and needs no authorization. Three of these fields decide how a document to this company has to
/// be built, which is the reason the lookup exists at all rather than being a convenience.
/// </remarks>
public sealed record CompanyLookup
{
    /// <summary>The fiscal code, normalised without the <c>RO</c> prefix.</summary>
    public required string Cui { get; init; }

    /// <summary>The registered name.</summary>
    public string? Name { get; init; }

    /// <summary>The address as ANAF renders it in one line.</summary>
    public string? Address { get; init; }

    /// <summary>The commerce register number (<c>nrRegCom</c>), e.g. <c>J12/345/2001</c>.</summary>
    public string? RegistrationNumber { get; init; }

    /// <summary>The registered telephone number.</summary>
    public string? Phone { get; init; }

    /// <summary>The principal activity code.</summary>
    public string? CaenCode { get; init; }

    /// <summary>The bank account ANAF holds, when it holds one.</summary>
    public string? Iban { get; init; }

    /// <summary>
    /// Whether the company is in the RO e-Factura register.
    /// </summary>
    /// <remarks>
    /// The field the lookup exists for. A company in the register receives documents through
    /// e-Factura as an ordinary B2B submission; one that is not has to be sent through
    /// <c>uploadb2c</c> instead, and sending it the wrong way is refused by ANAF rather than
    /// delivered.
    /// </remarks>
    public bool IsRegisteredForEFactura { get; init; }

    /// <summary>
    /// Whether the company is registered for VAT under article 316.
    /// </summary>
    /// <remarks>
    /// Decides whether the buyer's VAT identifier (BT-48) belongs on the document, and with it
    /// whether a reverse charge or an intra-community exemption is available.
    /// </remarks>
    public bool IsVatRegistered { get; init; }

    /// <summary>
    /// Whether the company is on the register of inactive taxpayers.
    /// </summary>
    /// <remarks>
    /// Worth surfacing before an invoice is raised rather than after: transactions with an
    /// inactive taxpayer are treated differently for deduction, and the status is not something a
    /// person entering a buyer would think to check.
    /// </remarks>
    public bool IsInactive { get; init; }

    /// <summary>The registered office, broken into parts.</summary>
    public CompanyAddress? RegisteredOffice { get; init; }

    /// <summary>The fiscal domicile, broken into parts, when it differs.</summary>
    public CompanyAddress? FiscalDomicile { get; init; }

    /// <summary>The date the register was asked about, which is what the answer describes.</summary>
    public DateOnly AsOf { get; init; }

    /// <summary>
    /// Fills in a party from the register.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A starting point, not a finished party. The register does not carry an email address, and
    /// its address is only as precise as what was registered — so this saves the typing and the
    /// transcription errors, and a person still confirms it.
    /// </para>
    /// <para>
    /// The VAT identifier is set only when the company is actually registered for VAT. Writing
    /// <c>RO</c> in front of a fiscal code that carries no VAT registration produces a document
    /// that claims something untrue about the buyer.
    /// </para>
    /// </remarks>
    public PartyEditModel ToPartyEditModel() => new()
    {
        Name = Name ?? string.Empty,
        TaxId = Cui,
        Telephone = Phone,
        VatNumber = IsVatRegistered ? $"RO{Cui}" : null,
        TradeRegisterNumber = RegistrationNumber,
        Address = RegisteredOffice?.ToAddressEditModel() ?? new AddressEditModel { CountryCode = "RO" },
    };
}

/// <summary>An address as the register holds it, in parts.</summary>
/// <param name="Street">Street name.</param>
/// <param name="Number">Street number.</param>
/// <param name="Locality">Town or city.</param>
/// <param name="County">County name.</param>
/// <param name="CountyCode">
/// The two-letter county code — <c>CJ</c>, <c>B</c> — which becomes the CIUS-RO subdivision by
/// prefixing <c>RO-</c>.
/// </param>
/// <param name="Country">Country name.</param>
/// <param name="PostalCode">Postal code.</param>
/// <param name="Details">Anything further: block, staircase, floor, apartment.</param>
public sealed record CompanyAddress(
    string? Street,
    string? Number,
    string? Locality,
    string? County,
    string? CountyCode,
    string? Country,
    string? PostalCode,
    string? Details)
{
    /// <summary>Maps to the address an invoice carries.</summary>
    /// <remarks>
    /// Street name, number and any further detail are joined, because BT-35 is one line and the
    /// register keeps them apart.
    /// </remarks>
    public AddressEditModel ToAddressEditModel() => new()
    {
        Street = JoinNonEmpty(", ", Street, Number),
        StreetAdditional = string.IsNullOrWhiteSpace(Details) ? null : Details,
        City = Locality ?? string.Empty,
        County = string.IsNullOrWhiteSpace(CountyCode) ? null : $"RO-{CountyCode.Trim().ToUpperInvariant()}",
        PostalCode = PostalCode,
        CountryCode = "RO",
    };

    private static string JoinNonEmpty(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
}

/// <summary>The outcome of asking the register about a set of companies.</summary>
/// <param name="Found">The companies the register knows.</param>
/// <param name="NotFound">
/// The fiscal codes it does not. An ordinary answer rather than a failure — a code can be
/// mistyped, or belong to something never registered.
/// </param>
public sealed record CompanyLookupResult(
    IReadOnlyList<CompanyLookup> Found,
    IReadOnlyList<string> NotFound);
