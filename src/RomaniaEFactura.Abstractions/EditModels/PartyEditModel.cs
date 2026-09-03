using System.ComponentModel.DataAnnotations;
using RomaniaEFactura.EditModels.Attributes;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.EditModels;

/// <summary>
/// A seller (BG-4) or buyer (BG-7), flattened into the fields a form actually asks for.
/// </summary>
/// <remarks>
/// UBL spreads a party across <c>PartyLegalEntity</c>, <c>PartyTaxScheme</c>,
/// <c>PartyIdentification</c>, <c>PostalAddress</c> and <c>Contact</c>, and which identifier
/// belongs in which of them is a frequent source of rejection. Here there is one field per real
/// question, and the mapping decides where each lands.
/// </remarks>
public sealed class PartyEditModel : IValidatableObject
{
    /// <summary>Registered legal name (BT-27 / BT-44).</summary>
    [Required(ErrorMessage = "The registered name is required.")]
    [StringLength(CiusRoLengths.PartyName, MinimumLength = 1)]
    [Display(Name = "Registered name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Fiscal identification code (BT-30 / BT-47), without the <c>RO</c> prefix.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept separate from <see cref="VatNumber"/> deliberately. Many Romanian companies are
    /// registered for tax but not for VAT, so the two are genuinely different facts: the CIF
    /// identifies the company, the VAT number says it charges VAT.
    /// </para>
    /// <para>
    /// The control digit is checked in <see cref="Validate"/> rather than by
    /// <see cref="RomanianCifAttribute"/> here, because whether the check applies depends on
    /// <see cref="AddressEditModel.CountryCode"/> — a German buyer's tax number is not a Romanian
    /// CIF and must not be judged as one. Applying the attribute directly would make every export
    /// and intra-community invoice unsendable.
    /// </para>
    /// </remarks>
    [Required(ErrorMessage = "The fiscal code (CIF) is required.")]
    [StringLength(30)]
    [Display(Name = "Fiscal code (CIF)")]
    public string TaxId { get; set; } = string.Empty;

    /// <summary>
    /// VAT identifier (BT-31 / BT-48), including the country prefix, for a VAT-registered party.
    /// </summary>
    /// <remarks>
    /// Leave empty for a party not registered for VAT. Reverse charge requires both parties to
    /// have one — BR-AE-02 and BR-AE-03 — which the model checks before the document is built.
    /// </remarks>
    [StringLength(30)]
    [Display(Name = "VAT number")]
    public string? VatNumber { get; set; }

    /// <summary>Trade register number, such as <c>J40/1234/2020</c> (BT-33).</summary>
    [StringLength(CiusRoLengths.CompanyLegalForm)]
    [Display(Name = "Trade register number")]
    public string? TradeRegisterNumber { get; set; }

    /// <summary>Trading name where it differs from the registered one (BT-28 / BT-45).</summary>
    [StringLength(CiusRoLengths.TradingName)]
    [Display(Name = "Trading name")]
    public string? TradingName { get; set; }

    /// <summary>The party's address.</summary>
    [Required]
    public AddressEditModel Address { get; set; } = new();

    /// <summary>Contact name (BT-41 / BT-56).</summary>
    [StringLength(CiusRoLengths.ContactName)]
    [Display(Name = "Contact name")]
    public string? ContactName { get; set; }

    /// <summary>Contact telephone (BT-42 / BT-57).</summary>
    [Phone]
    [StringLength(CiusRoLengths.ContactTelephone)]
    [Display(Name = "Telephone")]
    public string? Telephone { get; set; }

    /// <summary>Contact email (BT-43 / BT-58).</summary>
    [EmailAddress]
    [StringLength(CiusRoLengths.ContactEmail)]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // ANAF checks the control digit on a Romanian fiscal code and rejects the document when it
        // fails, so catching it here is the difference between a red field and a rejection hours
        // later. Applied only to Romanian parties: a foreign tax number has its own rules, and
        // judging one by this algorithm would reject every legitimate export invoice.
        if (Address.IsRomanian
            && !string.IsNullOrWhiteSpace(TaxId)
            && !Validation.RomanianCif.IsValid(TaxId))
        {
            yield return new ValidationResult(
                "The Romanian fiscal code is not valid — check the digits.",
                [nameof(TaxId)]);
        }

        // A VAT number that is present must be plausible. Romanian ones are the CIF with an RO
        // prefix, and getting the two out of step is a mistake ANAF catches rather than ignores.
        if (!string.IsNullOrWhiteSpace(VatNumber)
            && VatNumber.StartsWith("RO", StringComparison.OrdinalIgnoreCase)
            && !Validation.RomanianCif.IsValid(VatNumber))
        {
            yield return new ValidationResult(
                "The Romanian VAT number is not valid — check the digits.",
                [nameof(VatNumber)]);
        }
    }
}

/// <summary>A postal address (BG-5 / BG-8).</summary>
public sealed class AddressEditModel : IValidatableObject
{
    /// <summary>Street name and number (BT-35).</summary>
    [Required(ErrorMessage = "The street address is required.")]
    [StringLength(CiusRoLengths.AddressLine1, MinimumLength = 1)]
    [Display(Name = "Street")]
    public string Street { get; set; } = string.Empty;

    /// <summary>A second address line (BT-36).</summary>
    [StringLength(CiusRoLengths.AddressLine2)]
    [Display(Name = "Address line 2")]
    public string? StreetAdditional { get; set; }

    /// <summary>
    /// A third address line (BT-162 / BT-163 / BT-165).
    /// </summary>
    /// <remarks>
    /// Rarely needed. <see cref="Street"/> is line one and <see cref="StreetAdditional"/> line two;
    /// this is the third, which UBL nests in an element of its own rather than naming directly.
    /// </remarks>
    [StringLength(CiusRoLengths.AddressLine2)]
    [Display(Name = "Address line 3")]
    public string? AddressLine3 { get; set; }

    /// <summary>
    /// Town or city (BT-37).
    /// </summary>
    /// <remarks>
    /// For an address in Bucharest this is a sector code — <c>SECTOR1</c> through <c>SECTOR6</c>,
    /// listed in <see cref="RomanianCounties.BucharestSectors"/> — and not the word Bucuresti.
    /// Rule BR-RO-100, checked wherever both this and <see cref="County"/> are in view.
    /// </remarks>
    [Required(ErrorMessage = "The city is required.")]
    [StringLength(CiusRoLengths.City, MinimumLength = 1)]
    [Display(Name = "City")]
    public string City { get; set; } = string.Empty;

    /// <summary>Post code (BT-38).</summary>
    [StringLength(CiusRoLengths.PostalCode)]
    [Display(Name = "Post code")]
    public string? PostalCode { get; set; }

    /// <summary>
    /// County as an ISO 3166-2:RO code (BT-39), for Romanian addresses.
    /// </summary>
    /// <remarks>
    /// Required when <see cref="CountryCode"/> is <c>RO</c>; that dependency is checked on the
    /// document, where both fields are in view.
    /// </remarks>
    [RomanianCounty]
    [Display(Name = "County")]
    public string? County { get; set; }

    /// <summary>Subdivision for a non-Romanian address (BT-39), as free text.</summary>
    [StringLength(100)]
    [Display(Name = "Region or state")]
    public string? Region { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country code (BT-40 / BT-55).</summary>
    [Required(ErrorMessage = "The country is required.")]
    [RegularExpression("^[A-Z]{2}$", ErrorMessage = "The country must be a two-letter ISO code, such as RO or DE.")]
    [Display(Name = "Country")]
    public string CountryCode { get; set; } = "RO";

    /// <summary>Whether this address is in Romania.</summary>
    public bool IsRomanian => string.Equals(CountryCode, "RO", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this address is in Bucharest, where the city is stated as a sector.</summary>
    public bool IsBucharest => IsRomanian && string.Equals(County?.Trim(), "RO-B", StringComparison.Ordinal);

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // BR-RO-100. The single most surprising rule in CIUS-RO: every other Romanian address
        // names its city, but a Bucharest one states a sector code, and ANAF rejects "Bucuresti"
        // outright. Nothing about the address hints at the exception.
        if (IsBucharest && !RomanianCounties.IsBucharestSector(City))
        {
            yield return new ValidationResult(
                "An address in Bucharest states the city as a sector code — "
                + $"{string.Join(", ", RomanianCounties.BucharestSectors)} — not '{City}' (BR-RO-100).",
                [nameof(City)]);
        }
    }
}
