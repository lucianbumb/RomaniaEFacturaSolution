using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RomaniaEFactura.Configuration;
using RomaniaEFactura.EditModels;
using RomaniaEFactura.Lookup;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura.IntegrationTests;

/// <summary>
/// Asking ANAF's public taxpayer register about a company.
/// </summary>
/// <remarks>
/// <para>
/// A different service from the e-Factura API: unauthenticated, its own host, and its own limits —
/// a hundred codes per request and one request per second.
/// </para>
/// <para>
/// The three flags are the reason it exists. Whether a company is in the RO e-Factura register
/// decides whether a document reaches it as B2B or through <c>uploadb2c</c>; whether it is
/// registered for VAT decides whether a VAT identifier belongs on the document at all; and whether
/// it is inactive is something nobody entering a buyer would think to check.
/// </para>
/// </remarks>
public class CompanyLookupTests(MockAnafFixture fixture) : IClassFixture<MockAnafFixture>
{
    [Fact]
    public async Task ACompanyInTheRegisterComesBackWithItsDetails()
    {
        var result = await CreateClient().LookupAsync(["12345674"]);

        Assert.True(result.IsSuccess, result.ToString());
        var company = Assert.Single(result.Value.Found);

        Assert.Equal("12345674", company.Cui);
        Assert.Equal("SC TEST SRL", company.Name);
        Assert.Equal("J12/345/2001", company.RegistrationNumber);
        Assert.Equal("RO49AAAA1B31007593840000", company.Iban);
    }

    [Fact]
    public async Task TheEFacturaRegistrationIsReported()
    {
        // The field the whole feature exists for.
        var client = CreateClient();

        var registered = await client.LookupAsync(["12345674"]);
        var notRegistered = await client.LookupAsync(["19867705"]);

        Assert.True(registered.Value.Found[0].IsRegisteredForEFactura);
        Assert.False(notRegistered.Value.Found[0].IsRegisteredForEFactura);
    }

    [Fact]
    public async Task VatRegistrationAndInactivityAreReported()
    {
        var client = CreateClient();

        var noVat = await client.LookupAsync(["80000009"]);
        var inactive = await client.LookupAsync(["98765438"]);

        Assert.False(noVat.Value.Found[0].IsVatRegistered);
        Assert.True(inactive.Value.Found[0].IsInactive);
    }

    [Fact]
    public async Task ACodeTheRegisterDoesNotKnowIsNotAFailure()
    {
        // 11111110 has a correct control digit and belongs to nothing.
        var result = await CreateClient().LookupAsync(["11111110"]);

        Assert.True(result.IsSuccess, result.ToString());
        Assert.Empty(result.Value.Found);
        Assert.Equal(["11111110"], result.Value.NotFound);
    }

    [Fact]
    public async Task ManyCompaniesCostOneRequest()
    {
        var result = await CreateClient().LookupAsync(["12345674", "19867705", "80000009"]);

        Assert.Equal(3, result.Value.Found.Count);
    }

    [Fact]
    public async Task MoreThanAHundredAreBatchedRatherThanRefused()
    {
        // ANAF caps a request at a hundred and answers an error above it. The mock reproduces that
        // cap, so this passing at all is the evidence the client batched.
        var cuis = new List<string> { "12345674", "19867705", "80000009", "98765438" };
        cuis.AddRange(Enumerable.Range(0, 120).Select(SyntheticCif));

        var result = await CreateClient().LookupAsync(cuis);

        Assert.True(result.IsSuccess, result.ToString());
        Assert.Equal(4, result.Value.Found.Count);
        Assert.Equal(120, result.Value.NotFound.Count);
    }

    [Fact]
    public async Task TheSameCodeTwiceIsAskedAboutOnce()
    {
        var result = await CreateClient().LookupAsync(["12345674", "RO12345674", " 12345674 "]);

        Assert.Single(result.Value.Found);
    }

    [Fact]
    public async Task AMalformedCodeIsRefusedLocally()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => CreateClient().LookupAsync(["12345678"]));
    }

    // ------------------------------------------------------- filling in a party

    [Fact]
    public async Task ACompanyBecomesAParty()
    {
        var result = await CreateClient().LookupAsync(["12345674"]);

        var party = result.Value.Found[0].ToPartyEditModel();

        Assert.Equal("SC TEST SRL", party.Name);
        Assert.Equal("12345674", party.TaxId);
        Assert.Equal("RO12345674", party.VatNumber);
        Assert.Equal("Strada Memorandumului, 28", party.Address.Street);
        Assert.Equal("Cluj-Napoca", party.Address.City);
        Assert.Equal("RO-CJ", party.Address.County);
        Assert.Equal("400114", party.Address.PostalCode);
    }

    [Fact]
    public async Task ACompanyWithoutVatGetsNoVatIdentifier()
    {
        // Writing RO in front of a fiscal code that carries no VAT registration would make the
        // document claim something untrue about the buyer.
        var result = await CreateClient().LookupAsync(["80000009"]);

        Assert.Null(result.Value.Found[0].ToPartyEditModel().VatNumber);
    }

    [Fact]
    public async Task APartyFromTheRegisterIsAcceptedByTheValidator()
    {
        // The point of the mapping. A party assembled from the register has to satisfy the
        // Romanian address rules — a coded county, and the Bucharest sector rule — or prefilling a
        // buyer produces a document that is then refused.
        var result = await CreateClient().LookupAsync(["12345674"]);
        var invoice = InvoiceWithBuyer(result.Value.Found[0].ToPartyEditModel());

        var report = EditModelValidator.Validate(invoice);

        Assert.True(report.IsValid, report.ToString());
    }

    // ------------------------------------------------------------- the harness

    private static InvoiceEditModel InvoiceWithBuyer(PartyEditModel buyer) => new()
    {
        Number = "FCT-2026-900",
        IssueDate = new DateOnly(2026, 9, 3),
        DueDate = new DateOnly(2026, 10, 3),
        Seller = new PartyEditModel
        {
            Name = "Furnizor Test SRL",
            TaxId = "12345674",
            VatNumber = "RO12345674",
            Address = new AddressEditModel
            {
                Street = "Strada Exemplu 1",
                City = "SECTOR1",
                County = "RO-B",
                CountryCode = "RO",
            },
        },
        Buyer = buyer,
        Payment = new PaymentEditModel
        {
            MeansCode = "31",
            Iban = "RO49AAAA1B31007593840000",
            AccountHolder = "Furnizor Test SRL",
        },
        Lines =
        [
            new DocumentLineEditModel
            {
                Name = "Servicii de consultanta",
                Quantity = 1m,
                UnitPrice = 100.00m,
                VatRate = 19m,
            },
        ],
    };

    /// <summary>A fiscal code with a correct control digit, belonging to nothing.</summary>
    private static string SyntheticCif(int seed)
    {
        ReadOnlySpan<byte> weights = [7, 5, 3, 2, 1, 7, 5, 3, 2];
        var body = (20_000_000 + seed).ToString(System.Globalization.CultureInfo.InvariantCulture);

        var sum = 0;
        var offset = weights.Length - body.Length;
        for (var i = 0; i < body.Length; i++) sum += (body[i] - '0') * weights[offset + i];

        var control = sum * 10 % 11;
        return body + (control == 10 ? 0 : control);
    }

    private AnafCompanyLookupClient CreateClient() =>
        new(new LookupHttpClientFactory(fixture),
            Options.Create(new EFacturaOptions
            {
                CompanyLookupBaseAddress = new Uri(fixture.Server.BaseAddress, "test/FCTEL/rest"),
            }),
            NullLogger<AnafCompanyLookupClient>.Instance)
        {
            // The register allows one request a second. Waiting it out would add a second per
            // batch to the suite and prove nothing the pacing test does not prove directly.
            Delay = (_, _) => Task.CompletedTask,
        };

    private sealed class LookupHttpClientFactory(MockAnafFixture fixture) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => fixture.CreateClient();
    }
}
