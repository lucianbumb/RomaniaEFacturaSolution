using System.Xml.Linq;
using RomaniaEFactura.EditModels;
using RomaniaEFactura.EditModels.Attributes;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.Tests.EditModels;

/// <summary>The individual field checks the edit models rely on.</summary>
public class FieldRuleTests
{
    [Theory]
    // Real Romanian IBANs, in the format banks print them.
    [InlineData("RO49AAAA1B31007593840000")]
    [InlineData("ro49 aaaa 1b31 0075 9384 0000")]
    [InlineData("DE89370400440532013000")]
    [InlineData("GB29NWBK60161331926819")]
    public void AWellFormedIbanIsAccepted(string iban) => Assert.True(IbanAttribute.IsWellFormed(iban));

    [Theory]
    // The last digit changed: the check digits exist to catch exactly this.
    [InlineData("RO49AAAA1B31007593840001")]
    // Two characters transposed.
    [InlineData("RO94AAAA1B31007593840000")]
    [InlineData("")]
    [InlineData("not-an-iban")]
    // Check digits where letters belong.
    [InlineData("1249AAAA1B31007593840000")]
    // Too short to be any country's IBAN.
    [InlineData("RO49AAAA")]
    public void AMalformedIbanIsRejected(string iban) => Assert.False(IbanAttribute.IsWellFormed(iban));

    [Theory]
    [InlineData("RO-B")]
    [InlineData("RO-CJ")]
    [InlineData("RO-IF")]
    public void AKnownCountyCodeIsAccepted(string code) => Assert.True(RomanianCounties.IsValid(code));

    [Theory]
    // A county name rather than a code — what a person types unprompted.
    [InlineData("Bucuresti")]
    [InlineData("Cluj")]
    // RO-BU is Buzau's neighbour in the alphabet, not Bucharest, and is not a code at all.
    [InlineData("RO-BU")]
    [InlineData("B")]
    [InlineData("")]
    public void AnythingElseIsRejected(string code) => Assert.False(RomanianCounties.IsValid(code));

    [Fact]
    public void EveryCountyIsListedOnce()
    {
        // 41 counties plus Bucharest. A missing one silently blocks every seller registered there.
        Assert.Equal(42, RomanianCounties.Codes.Count);
        Assert.Equal(RomanianCounties.Codes.Count, RomanianCounties.Codes.Distinct().Count());
    }

    [Fact]
    public void BucharestIsRoBAndBuzauIsRoBz()
    {
        Assert.Equal("București", RomanianCounties.NameOf("RO-B"));
        Assert.Equal("Buzău", RomanianCounties.NameOf("RO-BZ"));
    }

    [Theory]
    [InlineData(VatCategory.StandardRate, "S")]
    [InlineData(VatCategory.ZeroRated, "Z")]
    [InlineData(VatCategory.Exempt, "E")]
    [InlineData(VatCategory.ReverseCharge, "AE")]
    [InlineData(VatCategory.IntraCommunitySupply, "K")]
    [InlineData(VatCategory.Export, "G")]
    [InlineData(VatCategory.OutsideScope, "O")]
    public void EachVatCategoryMapsToItsUnclCode(VatCategory category, string expected) =>
        Assert.Equal(expected, category.ToCode());

    [Fact]
    public void OnlyTheStandardCategoryKeepsTheRequestedRate()
    {
        Assert.Equal(19m, VatCategory.StandardRate.EffectiveRate(19m));
        Assert.Equal(0m, VatCategory.Exempt.EffectiveRate(19m));
        Assert.Equal(0m, VatCategory.ReverseCharge.EffectiveRate(19m));
        // Out of scope is distinct from zero: BR-O-08 rejects a rate of any value here.
        Assert.Null(VatCategory.OutsideScope.EffectiveRate(19m));
    }

    [Fact]
    public void ZeroRatedNeedsNoReasonBecauseVatDoesApply()
    {
        Assert.False(VatCategory.ZeroRated.RequiresExemptionReason());
        Assert.True(VatCategory.Exempt.RequiresExemptionReason());
    }
}

/// <summary>The buyer message format, which is the one wire shape ANAF does not publish.</summary>
public class BuyerMessageTests
{
    [Fact]
    public void AMessageRendersAsTheHeaderElementAnafExpects()
    {
        var message = new BuyerMessageEditModel
        {
            UploadIndex = "3828",
            Message = "Factura nu corespunde comenzii.",
        };

        var root = XDocument.Parse(message.ToXml()).Root!;

        Assert.Equal("header", root.Name.LocalName);
        Assert.Equal("mfp:anaf:dgti:spv:reqMesaj:v1", root.Name.NamespaceName);
        Assert.Equal("3828", root.Attribute("index_incarcare")!.Value);
        Assert.Equal("Factura nu corespunde comenzii.", root.Attribute("message")!.Value);
    }

    [Fact]
    public void DiacriticsSurviveTheRoundTrip()
    {
        // The text is Romanian and the encoding is UTF-8 without a BOM; mangling it here would
        // send the seller a message they cannot read.
        var message = new BuyerMessageEditModel
        {
            UploadIndex = "3828",
            Message = "Cantitatea livrată nu corespunde. Vă rugăm să emiteți o notă de credit.",
        };

        var root = XDocument.Parse(message.ToXml()).Root!;

        Assert.Equal(message.Message, root.Attribute("message")!.Value);
    }

    [Fact]
    public void AMessageWithoutAnUploadIndexIsRejected()
    {
        var report = EditModelValidator.Validate(new BuyerMessageEditModel { Message = "Ceva" });

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "UploadIndex");
    }

    [Fact]
    public void ANonNumericUploadIndexIsRejected()
    {
        var report = EditModelValidator.Validate(new BuyerMessageEditModel
        {
            UploadIndex = "FCT-2026-001",
            Message = "Ceva",
        });

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "UploadIndex");
    }

    [Fact]
    public void AnEmptyMessageIsRejected()
    {
        var report = EditModelValidator.Validate(new BuyerMessageEditModel { UploadIndex = "3828" });

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, f => f.Path == "Message");
    }
}
