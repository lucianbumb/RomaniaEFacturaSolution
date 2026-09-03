using System.ComponentModel.DataAnnotations;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.EditModels;

/// <summary>
/// The seller's fiscal representative in Romania (BG-11).
/// </summary>
/// <remarks>
/// <para>
/// A company selling into Romania without being established there appoints one, and the invoice
/// has to name them. Until this existed such an invoice could not be built through the library at
/// all — only through <c>SendRawXmlAsync</c>, which carries no guarantee.
/// </para>
/// <para>
/// Deliberately not a <see cref="PartyEditModel"/>. A representative has no Romanian fiscal code of
/// its own to state as BT-30: what identifies it is the VAT identifier it is registered under, and
/// requiring a CIF would make the model impossible to fill in correctly.
/// </para>
/// </remarks>
public sealed class TaxRepresentativeEditModel
{
    /// <summary>The representative's name (BT-62).</summary>
    [Required(ErrorMessage = "The tax representative's name is required.")]
    [StringLength(CiusRoLengths.PartyName, MinimumLength = 1)]
    [Display(Name = "Tax representative")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The representative's VAT identifier (BT-63), including its country prefix.
    /// </summary>
    /// <remarks>
    /// Required, because the representative exists to be the VAT-liable party. BR-RO-065 accepts it
    /// in place of the seller's own identifier, which is the whole point of appointing one.
    /// </remarks>
    [Required(ErrorMessage = "The tax representative's VAT identifier is required.")]
    [StringLength(30, MinimumLength = 3)]
    [Display(Name = "VAT number")]
    public string VatNumber { get; set; } = string.Empty;

    /// <summary>
    /// The representative's address (BG-12).
    /// </summary>
    /// <remarks>
    /// CIUS-RO makes the same four demands of it as of the seller's — street, city, a coded county
    /// and the Bucharest sector rule — under BR-RO-140, 150, 160 and 170.
    /// </remarks>
    [Required]
    public AddressEditModel Address { get; set; } = new();
}
