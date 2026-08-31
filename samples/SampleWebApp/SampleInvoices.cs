using RomaniaEFactura.EditModels;

namespace SampleWebApp;

/// <summary>
/// Starting points for the forms, so a demo does not begin with twenty empty boxes.
/// </summary>
/// <remarks>
/// The values are deliberately valid, including the CIF control digits and the Bucharest sector
/// code, so that the first thing a reader sees is a document that would be accepted — and the
/// validation messages appear when they break one on purpose.
/// </remarks>
public static class SampleInvoices
{
    /// <summary>An invoice pre-filled with a plausible Romanian supplier and customer.</summary>
    public static InvoiceEditModel Blank() => new()
    {
        Number = $"FCT-{DateTime.Today:yyyy}-001",
        IssueDate = DateOnly.FromDateTime(DateTime.Today),
        DueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
        Seller = Seller(),
        Buyer = Buyer(),
        Payment = new PaymentEditModel
        {
            MeansCode = "31",
            Iban = "RO49AAAA1B31007593840000",
            AccountHolder = "Furnizor Demo SRL",
        },
        PaymentTerms = "Plata in 30 de zile.",
        Lines =
        [
            new DocumentLineEditModel
            {
                Name = "Servicii de consultanta",
                Quantity = 2m,
                UnitPrice = 100.00m,
                VatCategory = VatCategory.StandardRate,
                VatRate = 21m,
            },
        ],
    };

    /// <summary>A credit note against the invoice above.</summary>
    public static CreditNoteEditModel BlankCreditNote() => new()
    {
        Number = $"CN-{DateTime.Today:yyyy}-001",
        IssueDate = DateOnly.FromDateTime(DateTime.Today),
        Seller = Seller(),
        Buyer = Buyer(),
        PrecedingDocuments =
        [
            new DocumentReferenceEditModel
            {
                Number = $"FCT-{DateTime.Today:yyyy}-001",
                IssueDate = DateOnly.FromDateTime(DateTime.Today),
            },
        ],
        Lines =
        [
            new DocumentLineEditModel
            {
                Name = "Storno servicii de consultanta",
                Quantity = 1m,
                UnitPrice = 100.00m,
                VatCategory = VatCategory.StandardRate,
                VatRate = 21m,
            },
        ],
    };

    private static PartyEditModel Seller() => new()
    {
        Name = "Furnizor Demo SRL",
        TaxId = "12345674",
        VatNumber = "RO12345674",
        TradeRegisterNumber = "J40/1234/2020",
        Address = new AddressEditModel
        {
            Street = "Strada Exemplu 1",
            // BR-RO-100: Bucharest states a sector, not the city name.
            City = "SECTOR1",
            County = "RO-B",
            PostalCode = "010101",
            CountryCode = "RO",
        },
        Email = "facturi@furnizor.example",
    };

    private static PartyEditModel Buyer() => new()
    {
        Name = "Client Demo SA",
        TaxId = "23456783",
        VatNumber = "RO23456783",
        Address = new AddressEditModel
        {
            Street = "Bulevardul Clientului 20",
            City = "Cluj-Napoca",
            County = "RO-CJ",
            PostalCode = "400001",
            CountryCode = "RO",
        },
    };
}
