using RomaniaEFactura.Ubl;
using RomaniaEFactura.Validation;

namespace RomaniaEFactura.EditModels;

/// <summary>
/// Turns an edit model into the UBL document ANAF receives.
/// </summary>
/// <remarks>
/// This is where the library earns its keep. The edit model asks fourteen questions about a line;
/// UBL needs those spread across <c>Item</c>, <c>Price</c>, <c>ClassifiedTaxCategory</c> and
/// <c>AllowanceCharge</c>, with the net amount computed and the totals and VAT breakdown assembled
/// to match. Every figure written here is derived, never copied from a field a caller filled in,
/// which is what makes the arithmetic rules impossible to fail.
/// </remarks>
public static class EditModelMapper
{
    /// <summary>Builds the UBL invoice an edit model describes.</summary>
    public static UblInvoice ToUbl(this InvoiceEditModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var invoice = new UblInvoice
        {
            Id = model.Number,
            IssueDate = model.IssueDate.ToDateTime(TimeOnly.MinValue),
            DueDate = model.DueDate?.ToDateTime(TimeOnly.MinValue),
            InvoiceTypeCode = model.TypeCode,
            DocumentCurrencyCode = model.Currency,
            Notes = [.. model.Notes.Where(note => !string.IsNullOrWhiteSpace(note))],
            BuyerReference = NullIfBlank(model.BuyerReference),
            AccountingCost = NullIfBlank(model.AccountingReference),
            InvoicePeriod = MapPeriod(model),
            OrderReference = MapOrderReference(model),
            BillingReferences = MapPrecedingDocuments(model),
            AccountingSupplierParty = new PartyWrapper(MapParty(model.Seller)),
            AccountingCustomerParty = new PartyWrapper(MapParty(model.Buyer)),
            TaxRepresentativeParty = MapTaxRepresentative(model),
            Delivery = MapDelivery(model),
            PaymentMeans = MapPaymentMeans(model),
            PaymentTerms = MapPaymentTerms(model),
            AllowanceCharges = MapAllowanceCharges(model),
            TaxTotals = [MapTaxTotal(model)],
            LegalMonetaryTotal = MapTotals(model),
        };

        invoice.InvoiceLines =
        [
            .. model.Lines.Select((line, index) =>
            {
                var mapped = MapLine(line, index, model.Currency);
                return new InvoiceLine
                {
                    Id = mapped.Id,
                    Note = mapped.Note,
                    InvoicedQuantity = mapped.Quantity,
                    LineExtensionAmount = mapped.NetAmount,
                    AccountingCost = mapped.AccountingCost,
                    AllowanceCharges = mapped.AllowanceCharges,
                    Item = mapped.Item,
                    Price = mapped.Price,
                };
            })
        ];

        return invoice;
    }

    /// <summary>Builds the UBL credit note an edit model describes.</summary>
    public static UblCreditNote ToUbl(this CreditNoteEditModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var creditNote = new UblCreditNote
        {
            Id = model.Number,
            IssueDate = model.IssueDate.ToDateTime(TimeOnly.MinValue),
            CreditNoteTypeCode = model.TypeCode,
            DocumentCurrencyCode = model.Currency,
            Notes = [.. model.Notes.Where(note => !string.IsNullOrWhiteSpace(note))],
            BuyerReference = NullIfBlank(model.BuyerReference),
            AccountingCost = NullIfBlank(model.AccountingReference),
            InvoicePeriod = MapPeriod(model),
            OrderReference = MapOrderReference(model),
            BillingReferences = MapPrecedingDocuments(model),
            AccountingSupplierParty = new PartyWrapper(MapParty(model.Seller)),
            AccountingCustomerParty = new PartyWrapper(MapParty(model.Buyer)),
            TaxRepresentativeParty = MapTaxRepresentative(model),
            Delivery = MapDelivery(model),
            PaymentMeans = MapPaymentMeans(model),
            PaymentTerms = MapPaymentTerms(model),
            AllowanceCharges = MapAllowanceCharges(model),
            TaxTotals = [MapTaxTotal(model)],
            LegalMonetaryTotal = MapTotals(model),
        };

        creditNote.CreditNoteLines =
        [
            .. model.Lines.Select((line, index) =>
            {
                var mapped = MapLine(line, index, model.Currency);
                return new CreditNoteLine
                {
                    Id = mapped.Id,
                    Note = mapped.Note,
                    CreditedQuantity = mapped.Quantity,
                    LineExtensionAmount = mapped.NetAmount,
                    AccountingCost = mapped.AccountingCost,
                    AllowanceCharges = mapped.AllowanceCharges,
                    Item = mapped.Item,
                    Price = mapped.Price,
                };
            })
        ];

        return creditNote;
    }

    // --------------------------------------------------------------------- lines

    private static MappedLine MapLine(DocumentLineEditModel line, int index, string currency)
    {
        var adjustments = new List<AllowanceCharge>();

        if (line.DiscountAmount is > 0)
        {
            adjustments.Add(new AllowanceCharge
            {
                ChargeIndicator = false,
                Reason = line.DiscountReason,
                Amount = new Amount(Money.Round(line.DiscountAmount.Value), currency),
                // No TaxCategory: UBL-CR-599 forbids one on a line-level allowance, because the
                // line's own category already settles how it is taxed.
            });
        }

        if (line.ChargeAmount is > 0)
        {
            adjustments.Add(new AllowanceCharge
            {
                ChargeIndicator = true,
                Reason = line.ChargeReason,
                Amount = new Amount(Money.Round(line.ChargeAmount.Value), currency),
            });
        }

        return new MappedLine
        {
            // A caller who numbered the lines keeps their numbering; otherwise the position is
            // used, because BR-21 requires an identifier and an empty one fails it.
            Id = NullIfBlank(line.Id) ?? (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Note = NullIfBlank(line.Note),
            Quantity = new Quantity(line.Quantity, line.UnitCode),
            NetAmount = new Amount(line.NetAmount, currency),
            AccountingCost = NullIfBlank(line.AccountingReference),
            AllowanceCharges = adjustments,
            Item = new Item
            {
                Name = line.Name,
                Description = NullIfBlank(line.Description),
                SellersItemIdentification = NullIfBlank(line.SellerItemCode) is { } code
                    ? new ItemIdentification { Id = code }
                    : null,
                ClassifiedTaxCategory = new LineTaxCategory
                {
                    Id = line.VatCategory.ToCode(),
                    Percent = line.EffectiveVatRate,
                },
            },
            Price = new Price
            {
                PriceAmount = new Amount(line.UnitPrice, currency),
                BaseQuantity = line.PriceBaseQuantity is { } baseQuantity
                    ? new Quantity(baseQuantity, line.UnitCode)
                    : null,
            },
        };
    }

    private sealed class MappedLine
    {
        public required Identifier Id { get; init; }

        public string? Note { get; init; }

        public required Quantity Quantity { get; init; }

        public required Amount NetAmount { get; init; }

        public string? AccountingCost { get; init; }

        public required List<AllowanceCharge> AllowanceCharges { get; init; }

        public required Item Item { get; init; }

        public required Price Price { get; init; }
    }

    // -------------------------------------------------------------------- totals

    private static TaxTotal MapTaxTotal(DocumentEditModel model) => new()
    {
        TaxAmount = new Amount(model.VatTotal, model.Currency),
        TaxSubtotals =
        [
            .. model.VatBreakdown.Select(entry => new TaxSubtotal
            {
                TaxableAmount = new Amount(entry.TaxableAmount, model.Currency),
                TaxAmount = new Amount(entry.VatAmount, model.Currency),
                TaxCategory = new TaxCategory
                {
                    Id = entry.Category.ToCode(),
                    Percent = entry.Rate,
                    TaxExemptionReason = entry.ExemptionReason,
                    TaxExemptionReasonCode = entry.ExemptionReasonCode,
                },
            })
        ],
    };

    private static MonetaryTotal MapTotals(DocumentEditModel model) => new()
    {
        LineExtensionAmount = new Amount(model.LineTotal, model.Currency),
        TaxExclusiveAmount = new Amount(model.TaxExclusiveTotal, model.Currency),
        TaxInclusiveAmount = new Amount(model.TaxInclusiveTotal, model.Currency),
        // Written only when non-zero: BR-CO-11 and BR-CO-12 tie these to the adjustments, and an
        // explicit zero where there are none is noise the buyer's system has to interpret.
        AllowanceTotalAmount = model.AllowanceTotal > 0
            ? new Amount(model.AllowanceTotal, model.Currency)
            : null,
        ChargeTotalAmount = model.ChargeTotal > 0
            ? new Amount(model.ChargeTotal, model.Currency)
            : null,
        PrepaidAmount = model.AmountAlreadyPaid is > 0
            ? new Amount(Money.Round(model.AmountAlreadyPaid.Value), model.Currency)
            : null,
        PayableAmount = new Amount(model.PayableAmount, model.Currency),
    };

    private static List<AllowanceCharge> MapAllowanceCharges(DocumentEditModel model) =>
    [
        .. model.AllowancesAndCharges.Select(adjustment => new AllowanceCharge
        {
            ChargeIndicator = adjustment.IsCharge,
            Reason = adjustment.Reason,
            ReasonCode = NullIfBlank(adjustment.ReasonCode),
            Amount = new Amount(Money.Round(adjustment.Amount), model.Currency),
            // Required at document level, and forbidden at line level — the opposite of the line
            // case above, which is exactly the kind of asymmetry this mapper exists to absorb.
            TaxCategory = new TaxCategory
            {
                Id = adjustment.VatCategory.ToCode(),
                Percent = adjustment.EffectiveVatRate,
            },
        })
    ];

    // -------------------------------------------------------------------- parties

    private static Party MapParty(PartyEditModel party)
    {
        var mapped = new Party
        {
            PartyName = NullIfBlank(party.TradingName) is { } tradingName
                ? new PartyName { Name = tradingName }
                : null,
            PostalAddress = MapAddress(party.Address),
            PartyLegalEntity = new PartyLegalEntity
            {
                RegistrationName = party.Name,
                // ANAF's API refuses a prefixed fiscal code, so it is normalised here rather than
                // relied upon to have been entered without one.
                CompanyId = RomanianCif.Normalize(party.TaxId),
                CompanyLegalForm = NullIfBlank(party.TradeRegisterNumber),
            },
            Contact = MapContact(party),
        };

        if (NullIfBlank(party.VatNumber) is { } vatNumber)
        {
            // The VAT identifier keeps its country prefix — unlike the fiscal code, which must
            // lose it. BT-31 is an international identifier; BT-30 is a national one.
            mapped.PartyTaxSchemes.Add(new PartyTaxScheme { CompanyId = vatNumber });
        }

        return mapped;
    }

    private static Contact? MapContact(PartyEditModel party)
    {
        if (string.IsNullOrWhiteSpace(party.ContactName)
            && string.IsNullOrWhiteSpace(party.Telephone)
            && string.IsNullOrWhiteSpace(party.Email))
        {
            return null;
        }

        return new Contact
        {
            Name = NullIfBlank(party.ContactName),
            Telephone = NullIfBlank(party.Telephone),
            ElectronicMail = NullIfBlank(party.Email),
        };
    }

    private static PostalAddress MapAddress(AddressEditModel address) => new()
    {
        StreetName = NullIfBlank(address.Street),
        AdditionalStreetName = NullIfBlank(address.StreetAdditional),
        CityName = NullIfBlank(address.City),
        PostalZone = NullIfBlank(address.PostalCode),
        // The county code for a Romanian address, the free-text region for any other.
        CountrySubentity = address.IsRomanian
            ? NullIfBlank(address.County)
            : NullIfBlank(address.Region),
        Country = new Country { IdentificationCode = address.CountryCode.ToUpperInvariant() },
    };

    // ------------------------------------------------------------------ the rest

    private static Period? MapPeriod(DocumentEditModel model) =>
        model.PeriodStart is null && model.PeriodEnd is null
            ? null
            : new Period
            {
                StartDate = model.PeriodStart?.ToDateTime(TimeOnly.MinValue),
                EndDate = model.PeriodEnd?.ToDateTime(TimeOnly.MinValue),
            };

    private static OrderReference? MapOrderReference(DocumentEditModel model) =>
        NullIfBlank(model.OrderReference) is { } order
            ? new OrderReference { Id = order }
            : null;

    private static List<BillingReference> MapPrecedingDocuments(DocumentEditModel model) =>
    [
        .. model.PrecedingDocuments.Select(reference => new BillingReference
        {
            InvoiceDocumentReference = new DocumentReference
            {
                Id = reference.Number,
                IssueDate = reference.IssueDate?.ToDateTime(TimeOnly.MinValue),
            },
        })
    ];

    private static Party? MapTaxRepresentative(DocumentEditModel model)
    {
        if (model.TaxRepresentative is not { } representative) return null;

        // Name and VAT identifier only. A representative has no BT-30 of its own, so unlike a
        // seller or buyer it gets no PartyLegalEntity — writing one would be schema-valid and
        // meaningless.
        var party = new Party
        {
            PartyName = new PartyName { Name = representative.Name },
            PostalAddress = MapAddress(representative.Address),
        };

        party.PartyTaxSchemes.Add(new PartyTaxScheme { CompanyId = representative.VatNumber });

        return party;
    }

    private static Delivery? MapDelivery(DocumentEditModel model)
    {
        if (model.DeliveryDate is null && model.DeliveryAddress is null) return null;

        return new Delivery
        {
            ActualDeliveryDate = model.DeliveryDate?.ToDateTime(TimeOnly.MinValue),
            DeliveryLocation = model.DeliveryAddress is { } address
                ? new DeliveryLocation { Address = MapAddress(address) }
                : null,
        };
    }

    private static List<PaymentMeans> MapPaymentMeans(DocumentEditModel model)
    {
        if (model.Payment is not { } payment) return [];

        return
        [
            new PaymentMeans
            {
                PaymentMeansCode = payment.MeansCode,
                PaymentId = NullIfBlank(payment.Reference),
                PayeeFinancialAccount = NullIfBlank(payment.Iban) is { } iban
                    ? new FinancialAccount
                    {
                        Id = iban.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant(),
                        Name = NullIfBlank(payment.AccountHolder),
                    }
                    : null,
            },
        ];
    }

    private static PaymentTerms? MapPaymentTerms(DocumentEditModel model) =>
        NullIfBlank(model.PaymentTerms) is { } terms
            ? new PaymentTerms { Note = terms }
            : null;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
