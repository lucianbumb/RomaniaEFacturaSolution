namespace RomaniaEFactura.Validation;

/// <summary>
/// The maximum length CIUS-RO allows for each business term.
/// </summary>
/// <remarks>
/// <para>
/// Romania caps roughly sixty fields that EN16931 leaves unbounded, in the <c>BR-RO-L*</c> rule
/// family. Each constant here names the rule it comes from, so a number can be traced rather than
/// trusted.
/// </para>
/// <para>
/// This exists because the first version of the edit models carried invented limits, and they were
/// wrong in both directions. Too generous is the dangerous kind — the model accepts a 150-character
/// item name and ANAF refuses the document, which breaks the library's central promise. Too strict
/// is merely obstructive, but it refuses documents that are perfectly legal: a 60-character invoice
/// number is fine, and was being rejected.
/// </para>
/// <para>
/// <see cref="Tests"/> in the unit suite parses ANAF's own Schematron and asserts every constant
/// below matches it, so the table stays faithful rather than becoming a stale hand-copy.
/// </para>
/// </remarks>
public static class CiusRoLengths
{
    // ------------------------------------------------------------------ document

    /// <summary>Invoice number, BT-1 (BR-RO-L155).</summary>
    public const int DocumentNumber = 200;

    /// <summary>Preceding invoice number, BT-25 (BR-RO-L156).</summary>
    public const int PrecedingDocumentNumber = 200;

    /// <summary>Contract reference, BT-12 (BR-RO-L0302).</summary>
    public const int ContractReference = 200;

    /// <summary>Purchase order reference, BT-13 (BR-RO-L0303).</summary>
    public const int OrderReference = 200;

    /// <summary>Buyer accounting reference, BT-19 (BR-RO-L1001).</summary>
    public const int AccountingReference = 100;

    /// <summary>Payment terms, BT-20 (BR-RO-L301).</summary>
    public const int PaymentTerms = 300;

    /// <summary>Invoice note, BT-22 (BR-RO-L302).</summary>
    public const int Note = 300;

    /// <summary>Invoiced object identifier and supporting document reference, BT-18 and BT-122 (BR-RO-L0308).</summary>
    public const int ObjectIdentifier = 200;

    /// <summary>Supporting document description, BT-123 (BR-RO-L1020).</summary>
    public const int SupportingDocumentDescription = 100;

    // -------------------------------------------------------------------- parties

    /// <summary>Seller and buyer name, BT-27 and BT-44 (BR-RO-L201, BR-RO-L203).</summary>
    public const int PartyName = 200;

    /// <summary>Seller and buyer trading name, BT-28 and BT-45 (BR-RO-L202, BR-RO-L204).</summary>
    public const int TradingName = 200;

    /// <summary>Seller additional legal information, BT-33 (BR-RO-L1000).</summary>
    public const int CompanyLegalForm = 1000;

    /// <summary>Payee name, BT-59 (BR-RO-L205).</summary>
    public const int PayeeName = 200;

    /// <summary>Contact point, BT-41 and BT-56 (BR-RO-L1004, BR-RO-L1009).</summary>
    public const int ContactName = 100;

    /// <summary>Contact telephone, BT-42 and BT-57 (BR-RO-L1005, BR-RO-L1010).</summary>
    public const int ContactTelephone = 100;

    /// <summary>Contact email, BT-43 and BT-58 (BR-RO-L1006, BR-RO-L1011).</summary>
    public const int ContactEmail = 100;

    // ------------------------------------------------------------------ addresses

    /// <summary>Address line 1, BT-35, BT-50 and BT-75 (BR-RO-L151, L152, L154).</summary>
    public const int AddressLine1 = 150;

    /// <summary>Address line 2, BT-36, BT-51 and BT-76 (BR-RO-L1002, L1007, L1014).</summary>
    public const int AddressLine2 = 100;

    /// <summary>City, BT-37, BT-52 and BT-77 (BR-RO-L0501, L0502, L0504).</summary>
    public const int City = 50;

    /// <summary>Post code, BT-38, BT-53 and BT-78 (BR-RO-L0201, L0202, L0204).</summary>
    public const int PostalCode = 20;

    // -------------------------------------------------------------------- payment

    /// <summary>Payment means text, BT-82 (BR-RO-L1016).</summary>
    public const int PaymentMeansText = 100;

    /// <summary>Remittance information, BT-83 (BR-RO-L140).</summary>
    public const int RemittanceInformation = 140;

    /// <summary>Payment account name, BT-85 (BR-RO-L208).</summary>
    public const int PaymentAccountName = 200;

    // ------------------------------------------------------- adjustments and VAT

    /// <summary>Document-level allowance and charge reason, BT-97 and BT-104 (BR-RO-L1017, L1018).</summary>
    public const int DocumentAdjustmentReason = 100;

    /// <summary>Line-level allowance and charge reason, BT-139 and BT-144 (BR-RO-L1022, L1023).</summary>
    public const int LineAdjustmentReason = 100;

    /// <summary>VAT exemption reason text, BT-120 (BR-RO-L1019).</summary>
    public const int VatExemptionReason = 100;

    // ---------------------------------------------------------------------- lines

    /// <summary>Invoice line note, BT-127 (BR-RO-L303).</summary>
    public const int LineNote = 300;

    /// <summary>Invoice line buyer accounting reference, BT-133 (BR-RO-L1021).</summary>
    public const int LineAccountingReference = 100;

    /// <summary>Item name, BT-153 (BR-RO-L1024).</summary>
    public const int ItemName = 100;

    /// <summary>Item description, BT-154 (BR-RO-L212).</summary>
    public const int ItemDescription = 200;

    /// <summary>Item attribute name, BT-160 (BR-RO-L0505).</summary>
    public const int ItemAttributeName = 50;

    /// <summary>Item attribute value, BT-161 (BR-RO-L1025).</summary>
    public const int ItemAttributeValue = 100;

    // --------------------------------------------------------- repeating groups

    /// <summary>Invoice notes, BG-1 (BR-RO-A020).</summary>
    public const int MaxNotes = 20;

    /// <summary>Supporting documents, BG-24 (BR-RO-A051).</summary>
    public const int MaxSupportingDocuments = 50;

    /// <summary>Item attributes, BG-32 (BR-RO-A052).</summary>
    public const int MaxItemAttributes = 50;

    /// <summary>Preceding invoice references, BG-3 (BR-RO-A500).</summary>
    public const int MaxPrecedingDocuments = 500;

    /// <summary>Where the constants above are checked against ANAF's own Schematron.</summary>
    /// <remarks>Named only so the class documentation can point at it.</remarks>
    internal const string Tests = "CiusRoLengthTableTests";
}
