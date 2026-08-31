using RomaniaEFactura.Ubl;

namespace RomaniaEFactura.Tests;

/// <summary>
/// Minimal documents that ANAF's own validator accepts, built from the library's models.
/// </summary>
/// <remarks>
/// These are deliberately generated rather than copied from ANAF's published examples: those
/// examples are EUPL-licensed, and — more usefully — a fixture produced by our own models tests
/// the code path that actually matters. The CIFs below carry valid control digits, which ANAF's
/// validator checks independently of the CIUS-RO rules.
/// </remarks>
public static class SampleDocuments
{
    /// <summary>A seller CIF with a valid control digit.</summary>
    public const string SellerCif = "12345674";

    /// <summary>A buyer CIF with a valid control digit.</summary>
    public const string BuyerCif = "23456783";

    /// <summary>Builds the smallest invoice that satisfies CIUS-RO.</summary>
    public static UblInvoice MinimalInvoice()
    {
        // One line: 2 units at 100.00 = 200.00 net, 19% VAT = 38.00, payable 238.00.
        const decimal net = 200.00m;
        const decimal vat = 38.00m;
        const decimal gross = 238.00m;

        return new UblInvoice
        {
            Id = "FCT-2026-001",
            IssueDate = new DateTime(2026, 8, 31),
            // BR-CO-25: when an amount is payable, a due date or payment terms must be present.
            DueDate = new DateTime(2026, 9, 30),
            InvoiceTypeCode = "380",
            DocumentCurrencyCode = "RON",
            AccountingSupplierParty = new PartyWrapper(Seller()),
            AccountingCustomerParty = new PartyWrapper(Buyer()),
            TaxTotals =
            [
                new TaxTotal
                {
                    TaxAmount = new Amount(vat),
                    TaxSubtotals =
                    [
                        new TaxSubtotal
                        {
                            TaxableAmount = new Amount(net),
                            TaxAmount = new Amount(vat),
                            TaxCategory = StandardRate(),
                        },
                    ],
                },
            ],
            LegalMonetaryTotal = new MonetaryTotal
            {
                LineExtensionAmount = new Amount(net),
                TaxExclusiveAmount = new Amount(net),
                TaxInclusiveAmount = new Amount(gross),
                PayableAmount = new Amount(gross),
            },
            InvoiceLines =
            [
                new InvoiceLine
                {
                    Id = "1",
                    InvoicedQuantity = new Quantity(2m),
                    LineExtensionAmount = new Amount(net),
                    Item = new Item
                    {
                        Name = "Servicii de consultanta",
                        ClassifiedTaxCategory = StandardRateLine(),
                    },
                    Price = new Price { PriceAmount = new Amount(100.00m) },
                },
            ],
        };
    }

    /// <summary>Builds the smallest credit note that satisfies CIUS-RO.</summary>
    public static UblCreditNote MinimalCreditNote()
    {
        const decimal net = 200.00m;
        const decimal vat = 38.00m;
        const decimal gross = 238.00m;

        return new UblCreditNote
        {
            Id = "CN-2026-001",
            IssueDate = new DateTime(2026, 8, 31),
            CreditNoteTypeCode = "381",
            DocumentCurrencyCode = "RON",
            // A credit note that references nothing cannot be reconciled by the buyer.
            BillingReferences =
            [
                new BillingReference
                {
                    InvoiceDocumentReference = new DocumentReference
                    {
                        Id = new Identifier("FCT-2026-001"),
                        IssueDate = new DateTime(2026, 8, 31),
                    },
                },
            ],
            AccountingSupplierParty = new PartyWrapper(Seller()),
            AccountingCustomerParty = new PartyWrapper(Buyer()),
            TaxTotals =
            [
                new TaxTotal
                {
                    TaxAmount = new Amount(vat),
                    TaxSubtotals =
                    [
                        new TaxSubtotal
                        {
                            TaxableAmount = new Amount(net),
                            TaxAmount = new Amount(vat),
                            TaxCategory = StandardRate(),
                        },
                    ],
                },
            ],
            LegalMonetaryTotal = new MonetaryTotal
            {
                LineExtensionAmount = new Amount(net),
                TaxExclusiveAmount = new Amount(net),
                TaxInclusiveAmount = new Amount(gross),
                PayableAmount = new Amount(gross),
            },
            CreditNoteLines =
            [
                new CreditNoteLine
                {
                    Id = "1",
                    CreditedQuantity = new Quantity(2m),
                    LineExtensionAmount = new Amount(net),
                    Item = new Item
                    {
                        Name = "Storno servicii de consultanta",
                        ClassifiedTaxCategory = StandardRateLine(),
                    },
                    Price = new Price { PriceAmount = new Amount(100.00m) },
                },
            ],
        };
    }

    /// <summary>
    /// An invoice under reverse charge (taxare inversă), which is routine in Romania.
    /// </summary>
    /// <remarks>
    /// The whole document sits in VAT category AE at a zero rate, no VAT is charged, and the
    /// exemption reason is mandatory because the liability moves to the buyer.
    /// </remarks>
    public static UblInvoice ReverseChargeInvoice()
    {
        const decimal net = 200.00m;

        var invoice = MinimalInvoice();
        invoice.Id = "FCT-2026-AE-001";

        invoice.TaxTotals =
        [
            new TaxTotal
            {
                TaxAmount = new Amount(0.00m),
                TaxSubtotals =
                [
                    new TaxSubtotal
                    {
                        TaxableAmount = new Amount(net),
                        TaxAmount = new Amount(0.00m),
                        TaxCategory = ReverseCharge(),
                    },
                ],
            },
        ];

        invoice.LegalMonetaryTotal = new MonetaryTotal
        {
            LineExtensionAmount = new Amount(net),
            TaxExclusiveAmount = new Amount(net),
            TaxInclusiveAmount = new Amount(net),
            PayableAmount = new Amount(net),
        };

        // The line category deliberately carries no exemption reason: UBL-CR-601 forbids it
        // there, and LineTaxCategory has no such member.
        invoice.InvoiceLines[0].Item.ClassifiedTaxCategory =
            new LineTaxCategory { Id = "AE", Percent = 0m, TaxScheme = new TaxScheme { Id = "VAT" } };

        return invoice;
    }

    /// <summary>The reverse-charge VAT category, with the exemption reason EN16931 requires.</summary>
    public static TaxCategory ReverseCharge() => new()
    {
        Id = "AE",
        Percent = 0m,
        TaxExemptionReason = "Taxare inversa",
        TaxScheme = new TaxScheme { Id = "VAT" },
    };

    private static TaxCategory StandardRate() => new()
    {
        Id = "S",
        Percent = 19m,
        TaxScheme = new TaxScheme { Id = "VAT" },
    };

    private static LineTaxCategory StandardRateLine() => new()
    {
        Id = "S",
        Percent = 19m,
        TaxScheme = new TaxScheme { Id = "VAT" },
    };

    private static Party Seller() => new()
    {
        PartyName = new PartyName { Name = "Furnizor Test SRL" },
        PostalAddress = new PostalAddress
        {
            StreetName = "Strada Exemplu 1",
            CityName = "SECTOR1",
            PostalZone = "013329",
            // CIUS-RO requires an ISO 3166-2:RO subdivision for Romanian addresses.
            CountrySubentity = "RO-B",
            Country = new Country { IdentificationCode = "RO" },
        },
        PartyTaxSchemes =
        [
            new PartyTaxScheme { CompanyId = "RO" + SellerCif, TaxScheme = new TaxScheme() },
        ],
        PartyLegalEntity = new PartyLegalEntity
        {
            RegistrationName = "Furnizor Test SRL",
            CompanyId = new Identifier("RO" + SellerCif),
        },
    };

    private static Party Buyer() => new()
    {
        PartyName = new PartyName { Name = "Client Test SRL" },
        PostalAddress = new PostalAddress
        {
            StreetName = "Bulevardul Exemplu 2",
            CityName = "CLUJ-NAPOCA",
            PostalZone = "400001",
            CountrySubentity = "RO-CJ",
            Country = new Country { IdentificationCode = "RO" },
        },
        PartyTaxSchemes =
        [
            new PartyTaxScheme { CompanyId = "RO" + BuyerCif, TaxScheme = new TaxScheme() },
        ],
        PartyLegalEntity = new PartyLegalEntity
        {
            RegistrationName = "Client Test SRL",
            CompanyId = new Identifier("RO" + BuyerCif),
        },
    };
}
