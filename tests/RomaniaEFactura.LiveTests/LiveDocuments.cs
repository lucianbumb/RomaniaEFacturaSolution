using RomaniaEFactura.EditModels;

namespace RomaniaEFactura.LiveTests;

/// <summary>
/// Documents for the live run, built the way a consuming application would build them.
/// </summary>
/// <remarks>
/// Deliberately built from <see cref="InvoiceEditModel"/> rather than from UBL: the point of the
/// run is to check the path an application actually takes, and the edit models carry the
/// guarantee. A number stamped with the current time keeps repeated runs from colliding on
/// document numbers, which ANAF rejects as duplicates.
/// </remarks>
public static class LiveDocuments
{
    /// <summary>A minimal invoice from the test company to itself.</summary>
    /// <param name="cif">
    /// The authorized company. It is both seller and buyer, because a test run must not send a
    /// fiscal document naming a real third party — the test register is shared and the buyer would
    /// see it.
    /// </param>
    public static InvoiceEditModel Invoice(string cif) => new()
    {
        Number = $"LIVE-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
        IssueDate = DateOnly.FromDateTime(DateTime.UtcNow),
        DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        Currency = "RON",
        Seller = Party(cif, "Test Seller"),
        Buyer = Party(cif, "Test Buyer"),
        PaymentTerms = "Test document — RO e-Factura library live run.",
        Lines =
        [
            new DocumentLineEditModel
            {
                Name = "Test line",
                Quantity = 1m,
                UnitPrice = 100.00m,
                VatCategory = VatCategory.StandardRate,
                VatRate = 21m,
            },
        ],
    };

    /// <summary>
    /// An invoice to a buyer outside Romania, which the offline validator cannot check.
    /// </summary>
    /// <remarks>
    /// ANAF's <c>ROeFacturaValidator.jar</c> demands a Romanian buyer CUI unconditionally and
    /// refuses every export invoice regardless of correctness. The live API is supposed to handle
    /// that through the <c>extern=DA</c> upload parameter, which a local file cannot carry — so
    /// whether this works is one of the questions only a real run can answer.
    /// </remarks>
    public static InvoiceEditModel ForeignBuyerInvoice(string cif)
    {
        var invoice = Invoice(cif);

        invoice.Number = $"LIVE-EXT-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        invoice.Buyer = new PartyEditModel
        {
            Name = "Kunde GmbH",
            TaxId = "DE123456789",
            VatNumber = "DE123456789",
            Address = new AddressEditModel
            {
                Street = "Hauptstrasse 5",
                City = "Berlin",
                Region = "Berlin",
                PostalCode = "10115",
                CountryCode = "DE",
            },
        };

        invoice.Lines[0].VatCategory = VatCategory.IntraCommunitySupply;
        invoice.Lines[0].VatRate = null;
        invoice.Lines[0].VatExemptionReason = "Livrare intracomunitara scutita";

        // BR-IC-11 and BR-IC-12: zero rating depends on showing the goods left Romania.
        invoice.DeliveryDate = DateOnly.FromDateTime(DateTime.UtcNow);
        invoice.DeliveryAddress = new AddressEditModel
        {
            Street = "Hauptstrasse 5",
            City = "Berlin",
            Region = "Berlin",
            CountryCode = "DE",
        };

        return invoice;
    }

    private static PartyEditModel Party(string cif, string name) => new()
    {
        Name = name,
        TaxId = cif,
        VatNumber = "RO" + cif,
        Address = new AddressEditModel
        {
            Street = "Strada Exemplu 1",
            // BR-RO-100: a Bucharest address states a sector code, never the city name. Left as a
            // Cluj address so the run does not depend on the company actually being in Bucharest.
            City = "Cluj-Napoca",
            County = "RO-CJ",
            PostalCode = "400001",
            CountryCode = "RO",
        },
    };
}
