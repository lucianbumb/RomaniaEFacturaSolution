using RomaniaEFactura.EditModels;

namespace RomaniaEFactura.Tests.EditModels;

/// <summary>
/// Filled-in edit models covering the shapes a real application produces.
/// </summary>
/// <remarks>
/// Each of these is a scenario the oracle then puts to ANAF's own validator, so they are written
/// the way a caller would write them — with no totals, no VAT breakdown and no line amounts, since
/// the whole point of the edit model is that those are derived.
/// </remarks>
public static class SampleEditModels
{
    /// <summary>A seller CIF with a valid control digit.</summary>
    public const string SellerCif = "12345674";

    /// <summary>A buyer CIF with a valid control digit.</summary>
    public const string BuyerCif = "23456783";

    /// <summary>The smallest invoice a caller can fill in and send.</summary>
    public static InvoiceEditModel MinimalInvoice() => new()
    {
        Number = "FCT-2026-001",
        IssueDate = new DateOnly(2026, 8, 31),
        DueDate = new DateOnly(2026, 9, 30),
        Currency = "RON",
        Seller = Seller(),
        Buyer = Buyer(),
        Lines =
        [
            new DocumentLineEditModel
            {
                Name = "Servicii de consultanta",
                Quantity = 2m,
                UnitPrice = 100.00m,
                VatCategory = VatCategory.StandardRate,
                VatRate = 19m,
            },
        ],
    };

    /// <summary>An invoice using every optional field the model offers.</summary>
    public static InvoiceEditModel FullInvoice()
    {
        var invoice = MinimalInvoice();

        invoice.BuyerReference = "CC-4417";
        invoice.OrderReference = "PO-2026-88";
        invoice.AccountingReference = "DEPT-12";
        invoice.Notes = ["Livrare conform contract 442/2026."];
        invoice.PeriodStart = new DateOnly(2026, 8, 1);
        invoice.PeriodEnd = new DateOnly(2026, 8, 31);
        invoice.DeliveryDate = new DateOnly(2026, 8, 30);
        invoice.PaymentTerms = "Plata in 30 de zile.";
        invoice.Payment = new PaymentEditModel
        {
            MeansCode = "31",
            Iban = "RO49AAAA1B31007593840000",
            AccountHolder = "Furnizor Test SRL",
            Reference = "FCT-2026-001",
        };

        invoice.Lines[0].Description = "Consultanta tehnica, august 2026";
        invoice.Lines[0].SellerItemCode = "SRV-001";
        invoice.Lines[0].Note = "Tarif contractual.";
        invoice.Lines[0].AccountingReference = "PROJ-9";

        invoice.Lines.Add(new DocumentLineEditModel
        {
            Name = "Licenta software",
            Quantity = 3m,
            UnitPrice = 49.9900m,
            UnitCode = "C62",
            VatCategory = VatCategory.StandardRate,
            VatRate = 19m,
            DiscountAmount = 10.00m,
            DiscountReason = "Reducere volum",
        });

        // A second standard rate, which has to produce its own VAT breakdown entry rather than
        // merging with the 19% one.
        invoice.Lines.Add(new DocumentLineEditModel
        {
            Name = "Manual tiparit",
            Quantity = 5m,
            UnitPrice = 20.00m,
            VatCategory = VatCategory.StandardRate,
            VatRate = 5m,
        });

        invoice.AllowancesAndCharges.Add(new DocumentAllowanceChargeEditModel
        {
            IsCharge = true,
            Amount = 25.00m,
            Reason = "Transport",
            VatCategory = VatCategory.StandardRate,
            VatRate = 19m,
        });

        invoice.AllowancesAndCharges.Add(new DocumentAllowanceChargeEditModel
        {
            IsCharge = false,
            Amount = 15.00m,
            Reason = "Discount fidelitate",
            VatCategory = VatCategory.StandardRate,
            VatRate = 19m,
        });

        return invoice;
    }

    /// <summary>An invoice under reverse charge — taxare inversă, routine in Romania.</summary>
    public static InvoiceEditModel ReverseChargeInvoice()
    {
        var invoice = MinimalInvoice();
        invoice.Lines[0].VatCategory = VatCategory.ReverseCharge;
        invoice.Lines[0].VatRate = null;
        invoice.Lines[0].VatExemptionReason = "Taxare inversa conform art. 331 Cod Fiscal";
        return invoice;
    }

    /// <summary>An invoice mixing standard-rate and exempt lines.</summary>
    public static InvoiceEditModel MixedVatInvoice()
    {
        var invoice = MinimalInvoice();
        invoice.Lines.Add(new DocumentLineEditModel
        {
            Name = "Serviciu medical",
            Quantity = 1m,
            UnitPrice = 300.00m,
            VatCategory = VatCategory.Exempt,
            VatExemptionReason = "Scutit conform art. 292 Cod Fiscal",
        });
        return invoice;
    }

    /// <summary>An invoice already settled, so nothing is left to pay.</summary>
    public static InvoiceEditModel PrepaidInvoice()
    {
        var invoice = MinimalInvoice();
        invoice.DueDate = null;
        invoice.PaymentTerms = null;
        invoice.AmountAlreadyPaid = 238.00m;
        return invoice;
    }

    /// <summary>The smallest credit note a caller can fill in and send.</summary>
    public static CreditNoteEditModel MinimalCreditNote() => new()
    {
        Number = "CN-2026-001",
        IssueDate = new DateOnly(2026, 8, 31),
        Currency = "RON",
        Seller = Seller(),
        Buyer = Buyer(),
        PrecedingDocuments =
        [
            new DocumentReferenceEditModel
            {
                Number = "FCT-2026-001",
                IssueDate = new DateOnly(2026, 8, 31),
            },
        ],
        Lines =
        [
            new DocumentLineEditModel
            {
                Name = "Servicii de consultanta",
                Quantity = 2m,
                UnitPrice = 100.00m,
                VatCategory = VatCategory.StandardRate,
                VatRate = 19m,
            },
        ],
    };

    /// <summary>A seller with everything CIUS-RO requires.</summary>
    public static PartyEditModel Seller() => new()
    {
        Name = "Furnizor Test SRL",
        TaxId = SellerCif,
        VatNumber = "RO" + SellerCif,
        TradeRegisterNumber = "J40/1234/2020",
        Address = new AddressEditModel
        {
            Street = "Strada Exemplu 1",
            City = "SECTOR1",
            County = "RO-B",
            PostalCode = "010101",
            CountryCode = "RO",
        },
        Email = "facturi@furnizor.example",
    };

    /// <summary>A buyer with everything CIUS-RO requires.</summary>
    public static PartyEditModel Buyer() => new()
    {
        Name = "Client Test SA",
        TaxId = BuyerCif,
        VatNumber = "RO" + BuyerCif,
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
