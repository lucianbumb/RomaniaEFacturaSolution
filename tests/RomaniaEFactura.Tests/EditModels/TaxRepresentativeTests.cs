using RomaniaEFactura.EditModels;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Tests.EditModels;

/// <summary>
/// The seller's fiscal representative (BG-11).
/// </summary>
/// <remarks>
/// Until this existed, a company selling into Romania without being established there could not
/// build an invoice through the library at all. These tests cover the four rules CIUS-RO adds for
/// the representative's address, which are the seller's rules under different ids.
/// </remarks>
public class TaxRepresentativeTests
{
    [Fact]
    public void AnInvoiceWithARepresentativeIsValid()
    {
        var report = EditModelValidator.Validate(WithRepresentative());

        Assert.True(report.IsValid, report.ToString());
    }

    [Fact]
    public void TheRepresentativeReachesTheDocumentAsAPartyWithAVatIdentifier()
    {
        var ubl = WithRepresentative().ToUbl();

        var representative = ubl.TaxRepresentativeParty;
        Assert.NotNull(representative);
        Assert.Equal("Reprezentant Fiscal SRL", representative.PartyName!.Name);
        Assert.Equal("RO12345674", Assert.Single(representative.PartyTaxSchemes).CompanyId.Value);
    }

    [Fact]
    public void TheRepresentativeGetsNoLegalEntity()
    {
        // A representative has no BT-30 of its own. Writing a PartyLegalEntity would be
        // schema-valid and meaningless, and would invite a reader to fill in a CIF that does not
        // belong to them.
        var ubl = WithRepresentative().ToUbl();

        Assert.Null(ubl.TaxRepresentativeParty!.PartyLegalEntity);
    }

    [Fact]
    public void NoRepresentativeMeansNoElement()
    {
        Assert.Null(SampleEditModels.MinimalInvoice().ToUbl().TaxRepresentativeParty);
    }

    [Fact]
    public void ARepresentativeWithoutANameIsRejected()
    {
        var invoice = WithRepresentative();
        invoice.TaxRepresentative!.Name = string.Empty;

        var report = EditModelValidator.Validate(invoice);

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "TaxRepresentative.Name");
    }

    [Fact]
    public void ARepresentativeWithoutAVatIdentifierIsRejected()
    {
        // The representative exists to be the VAT-liable party, so an unidentified one defeats the
        // purpose — and BR-RO-065 accepts it in place of the seller's own identifier.
        var invoice = WithRepresentative();
        invoice.TaxRepresentative!.VatNumber = string.Empty;

        Assert.Contains(
            EditModelValidator.Validate(invoice).Errors,
            f => f.Path == "TaxRepresentative.VatNumber");
    }

    [Fact]
    public void ARomanianRepresentativeAddressNeedsACountyCode()
    {
        var invoice = WithRepresentative();
        invoice.TaxRepresentative!.Address.County = null;

        Assert.Contains(
            EditModelValidator.Validate(invoice).Errors,
            f => f.Path == "TaxRepresentative.Address.County");
    }

    [Fact]
    public void ARepresentativeInBucharestStatesASector()
    {
        // BR-RO-160, which is BR-RO-100 wearing a different number.
        var invoice = WithRepresentative();
        invoice.TaxRepresentative!.Address.County = "RO-B";
        invoice.TaxRepresentative.Address.City = "Bucuresti";

        Assert.Contains(
            EditModelValidator.Validate(invoice).Errors,
            f => f.Path == "TaxRepresentative.Address.City");
    }

    // ------------------------------------------------------------- the UBL path

    [Fact]
    public void TheUblPathRequiresTheRepresentativesStreetAndCity()
    {
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.TaxRepresentativeParty = new Ubl.Party
        {
            PartyName = new Ubl.PartyName { Name = "Reprezentant" },
            PostalAddress = new Ubl.PostalAddress { Country = new Ubl.Country() },
        };
        invoice.TaxRepresentativeParty.PartyTaxSchemes.Add(
            new Ubl.PartyTaxScheme { CompanyId = "RO12345674" });

        var codes = CiusRoValidator.Validate(invoice).ErrorCodes;

        Assert.Contains("BR-RO-140", codes);
        Assert.Contains("BR-RO-150", codes);
    }

    [Fact]
    public void TheUblPathAppliesTheSectorRuleUnderItsOwnId()
    {
        // Reporting BR-RO-100 here would send a reader to the seller's rule for the
        // representative's field.
        var invoice = WithRepresentative().ToUbl();
        invoice.TaxRepresentativeParty!.PostalAddress!.CountrySubentity = "RO-B";
        invoice.TaxRepresentativeParty.PostalAddress.CityName = "Bucuresti";

        var codes = CiusRoValidator.Validate(invoice).ErrorCodes;

        Assert.Contains("BR-RO-160", codes);
        Assert.DoesNotContain("BR-RO-100", codes);
    }

    [Fact]
    public void TheUblPathCapsTheRepresentativesCity()
    {
        var invoice = WithRepresentative().ToUbl();
        invoice.TaxRepresentativeParty!.PostalAddress!.CountrySubentity = "RO-CJ";
        invoice.TaxRepresentativeParty.PostalAddress.CityName =
            new string('c', CiusRoLengths.City + 1);

        Assert.Contains("BR-RO-L0503", CiusRoValidator.Validate(invoice).ErrorCodes);
    }

    [Fact]
    public void TheRepresentativeSurvivesSerialization()
    {
        // TaxRepresentativeParty sits between AccountingCustomerParty and Delivery in the XSD
        // sequence. Emitting it anywhere else is schema-invalid, and only a round trip catches it.
        var xml = Ubl.UblSerializer.Serialize(WithRepresentative().ToUbl());
        var parsed = Ubl.UblSerializer.DeserializeInvoice(xml);

        Assert.Equal("Reprezentant Fiscal SRL", parsed.TaxRepresentativeParty!.PartyName!.Name);
        Assert.True(CiusRoValidator.Validate(parsed).IsValid);
    }

    /// <summary>An invoice from a foreign seller with a Romanian fiscal representative.</summary>
    private static InvoiceEditModel WithRepresentative()
    {
        var invoice = SampleEditModels.MinimalInvoice();

        invoice.TaxRepresentative = new TaxRepresentativeEditModel
        {
            Name = "Reprezentant Fiscal SRL",
            VatNumber = "RO12345674",
            Address = new AddressEditModel
            {
                Street = "Strada Reprezentantului 3",
                City = "Cluj-Napoca",
                County = "RO-CJ",
                PostalCode = "400002",
                CountryCode = "RO",
            },
        };

        return invoice;
    }
}
