using RomaniaEFactura.Ubl;

namespace RomaniaEFactura.Validation;

/// <summary>
/// The <c>BR-RO-L*</c> length caps and <c>BR-RO-A*</c> occurrence caps, applied to a UBL document.
/// </summary>
/// <remarks>
/// <para>
/// The edit models enforce these through <c>StringLength</c> attributes, which is where a form can
/// act on them. This is the same set applied to a document built directly as UBL — the path that
/// bypasses the edit models entirely, and the reason the library's guarantee had to be stated for
/// the models alone until now.
/// </para>
/// <para>
/// Written as a table walked once rather than as sixty near-identical blocks. Each entry is a rule
/// id, the value it caps, and where it lives; adding a rule is adding a row.
/// </para>
/// </remarks>
internal static class CiusRoLengthRules
{
    /// <summary>Adds a finding for every capped value that is too long.</summary>
    public static void Check(DocumentView doc, List<ValidationFinding> findings)
    {
        foreach (var (rule, limit, value, term, path) in Capped(doc))
        {
            if (value is null || value.Length <= limit) continue;

            findings.Add(new(rule,
                $"{term} is {value.Length} characters; CIUS-RO allows {limit}.",
                Path: path));
        }

        CheckOccurrences(doc, findings);
    }

    private static IEnumerable<(string Rule, int Limit, string? Value, string Term, string Path)> Capped(
        DocumentView doc)
    {
        yield return ("BR-RO-L155", CiusRoLengths.DocumentNumber, doc.Id, "The document number (BT-1)", "Id");

        foreach (var entry in ForParty(doc.Seller, "Seller", "BR-RO-L201", "BR-RO-L202",
                     "BR-RO-L151", "BR-RO-L1002", "BR-RO-L0501", "BR-RO-L0201",
                     "BR-RO-L1004", "BR-RO-L1005", "BR-RO-L1006"))
        {
            yield return entry;
        }

        foreach (var entry in ForParty(doc.Buyer, "Buyer", "BR-RO-L203", "BR-RO-L204",
                     "BR-RO-L152", "BR-RO-L1007", "BR-RO-L0502", "BR-RO-L0202",
                     "BR-RO-L1009", "BR-RO-L1010", "BR-RO-L1011"))
        {
            yield return entry;
        }

        yield return ("BR-RO-L1000", CiusRoLengths.CompanyLegalForm,
            doc.Seller.PartyLegalEntity?.CompanyLegalForm,
            "The seller's additional legal information (BT-33)", "Seller.PartyLegalEntity");

        yield return ("BR-RO-L301", CiusRoLengths.PaymentTerms,
            doc.PaymentTerms?.Note, "The payment terms (BT-20)", "PaymentTerms");

        foreach (var (note, index) in doc.Notes.Select((note, index) => (note, index)))
        {
            yield return ("BR-RO-L302", CiusRoLengths.Note, note,
                $"Note {index + 1} (BT-22)", $"Notes[{index}]");
        }

        if (doc.TaxRepresentative is { } representative)
        {
            yield return ("BR-RO-L206", CiusRoLengths.PartyName, representative.PartyName?.Name,
                "The tax representative's name (BT-62)", "TaxRepresentative.PartyName");

            var address = representative.PostalAddress;
            yield return ("BR-RO-L153", CiusRoLengths.AddressLine1, address?.StreetName,
                "The tax representative's street (BT-64)", "TaxRepresentative.PostalAddress.StreetName");
            yield return ("BR-RO-L1012", CiusRoLengths.AddressLine2, address?.AdditionalStreetName,
                "The tax representative's address line 2 (BT-65)", "TaxRepresentative.PostalAddress.AdditionalStreetName");
            yield return ("BR-RO-L0503", CiusRoLengths.City, address?.CityName,
                "The tax representative's city (BT-66)", "TaxRepresentative.PostalAddress.CityName");
            yield return ("BR-RO-L0203", CiusRoLengths.PostalCode, address?.PostalZone,
                "The tax representative's post code (BT-67)", "TaxRepresentative.PostalAddress.PostalZone");
        }

        // The delivery address, which the seller and buyer loop above does not reach.
        var delivery = doc.Delivery?.DeliveryLocation?.Address;
        if (delivery is not null)
        {
            yield return ("BR-RO-L154", CiusRoLengths.AddressLine1, delivery.StreetName,
                "The delivery street (BT-75)", "Delivery.Address.StreetName");
            yield return ("BR-RO-L1014", CiusRoLengths.AddressLine2, delivery.AdditionalStreetName,
                "The delivery address line 2 (BT-76)", "Delivery.Address.AdditionalStreetName");
            yield return ("BR-RO-L0504", CiusRoLengths.City, delivery.CityName,
                "The delivery city (BT-77)", "Delivery.Address.CityName");
            yield return ("BR-RO-L0204", CiusRoLengths.PostalCode, delivery.PostalZone,
                "The delivery post code (BT-78)", "Delivery.Address.PostalZone");
        }

        foreach (var (adjustment, index) in doc.AllowanceCharges.Select((a, i) => (a, i)))
        {
            yield return (adjustment.ChargeIndicator ? "BR-RO-L1018" : "BR-RO-L1017",
                CiusRoLengths.DocumentAdjustmentReason, adjustment.Reason,
                $"The reason for document {(adjustment.ChargeIndicator ? "charge" : "allowance")} "
                + $"{index + 1} (BT-{(adjustment.ChargeIndicator ? "104" : "97")})",
                $"AllowanceCharges[{index}]");
        }

        foreach (var subtotal in doc.TaxTotals.SelectMany(total => total.TaxSubtotals))
        {
            yield return ("BR-RO-L1019", CiusRoLengths.VatExemptionReason,
                subtotal.TaxCategory?.TaxExemptionReason,
                "The VAT exemption reason (BT-120)", "TaxTotals");
        }

        foreach (var line in doc.Lines)
        {
            yield return ("BR-RO-L1024", CiusRoLengths.ItemName, line.Item?.Name,
                "The item name (BT-153)", line.Path);
            yield return ("BR-RO-L212", CiusRoLengths.ItemDescription, line.Item?.Description,
                "The item description (BT-154)", line.Path);
            yield return ("BR-RO-L303", CiusRoLengths.LineNote, line.Note,
                "The line note (BT-127)", line.Path);

            foreach (var (adjustment, index) in line.AllowanceCharges.Select((a, i) => (a, i)))
            {
                yield return (adjustment.ChargeIndicator ? "BR-RO-L1023" : "BR-RO-L1022",
                    CiusRoLengths.LineAdjustmentReason, adjustment.Reason,
                    $"The reason for line {(adjustment.ChargeIndicator ? "charge" : "allowance")} "
                    + $"{index + 1} (BT-{(adjustment.ChargeIndicator ? "144" : "139")})",
                    line.Path);
            }
        }
    }

    private static IEnumerable<(string Rule, int Limit, string? Value, string Term, string Path)> ForParty(
        Party party,
        string role,
        string nameRule,
        string tradingNameRule,
        string addressLine1Rule,
        string addressLine2Rule,
        string cityRule,
        string postalCodeRule,
        string contactNameRule,
        string telephoneRule,
        string emailRule)
    {
        var noun = role.ToLowerInvariant();
        var address = party.PostalAddress;

        yield return (nameRule, CiusRoLengths.PartyName, party.PartyLegalEntity?.RegistrationName,
            $"The {noun}'s registered name", $"{role}.PartyLegalEntity");
        yield return (tradingNameRule, CiusRoLengths.TradingName, party.PartyName?.Name,
            $"The {noun}'s trading name", $"{role}.PartyName");
        yield return (addressLine1Rule, CiusRoLengths.AddressLine1, address?.StreetName,
            $"The {noun}'s street", $"{role}.PostalAddress.StreetName");
        yield return (addressLine2Rule, CiusRoLengths.AddressLine2, address?.AdditionalStreetName,
            $"The {noun}'s address line 2", $"{role}.PostalAddress.AdditionalStreetName");
        yield return (cityRule, CiusRoLengths.City, address?.CityName,
            $"The {noun}'s city", $"{role}.PostalAddress.CityName");
        yield return (postalCodeRule, CiusRoLengths.PostalCode, address?.PostalZone,
            $"The {noun}'s post code", $"{role}.PostalAddress.PostalZone");
        yield return (contactNameRule, CiusRoLengths.ContactName, party.Contact?.Name,
            $"The {noun}'s contact name", $"{role}.Contact.Name");
        yield return (telephoneRule, CiusRoLengths.ContactTelephone, party.Contact?.Telephone,
            $"The {noun}'s telephone", $"{role}.Contact.Telephone");
        yield return (emailRule, CiusRoLengths.ContactEmail, party.Contact?.ElectronicMail,
            $"The {noun}'s email", $"{role}.Contact.ElectronicMail");
    }

    private static void CheckOccurrences(DocumentView doc, List<ValidationFinding> findings)
    {
        if (doc.Notes.Count > CiusRoLengths.MaxNotes)
        {
            findings.Add(new("BR-RO-A020",
                $"A document may carry at most {CiusRoLengths.MaxNotes} notes (BG-1); "
                + $"this one has {doc.Notes.Count}.",
                Path: "Notes"));
        }

        if (doc.PrecedingDocumentCount > CiusRoLengths.MaxPrecedingDocuments)
        {
            findings.Add(new("BR-RO-A500",
                $"A document may reference at most {CiusRoLengths.MaxPrecedingDocuments} preceding "
                + $"documents (BG-3); this one references {doc.PrecedingDocumentCount}.",
                Path: "BillingReferences"));
        }

        if (doc.SupportingDocumentCount > CiusRoLengths.MaxSupportingDocuments)
        {
            findings.Add(new("BR-RO-A051",
                $"A document may carry at most {CiusRoLengths.MaxSupportingDocuments} supporting "
                + $"documents (BG-24); this one carries {doc.SupportingDocumentCount}.",
                Path: "AdditionalDocumentReferences"));
        }
    }
}
