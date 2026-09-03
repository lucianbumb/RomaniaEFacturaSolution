namespace RomaniaEFactura.Ubl;

/// <summary>
/// XML namespaces used by UBL 2.1 documents, and the CIUS-RO identifiers ANAF requires.
/// </summary>
public static class UblNamespaces
{
    /// <summary>Common basic components (<c>cbc</c>) — leaf elements such as ID and IssueDate.</summary>
    public const string Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    /// <summary>Common aggregate components (<c>cac</c>) — composite elements such as Party.</summary>
    public const string Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";

    /// <summary>Document namespace for <c>Invoice</c>.</summary>
    public const string Invoice = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";

    /// <summary>Document namespace for <c>CreditNote</c>.</summary>
    public const string CreditNote = "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2";

    /// <summary>Document namespace for <c>DebitNote</c>.</summary>
    public const string DebitNote = "urn:oasis:names:specification:ubl:schema:xsd:DebitNote-2";

    /// <summary>
    /// The CIUS-RO specification identifier (BT-24). Rule BR-RO-001 requires this exact value;
    /// ANAF's own published examples still declare 1.0.0 and are rejected by its current validator.
    /// </summary>
    public const string CustomizationId =
        "urn:cen.eu:en16931:2017#compliant#urn:efactura.mfinante.ro:CIUS-RO:1.0.1";

    /// <summary>The UBL version (BT-23) carried by every document.</summary>
    public const string UblVersionId = "2.1";
}
