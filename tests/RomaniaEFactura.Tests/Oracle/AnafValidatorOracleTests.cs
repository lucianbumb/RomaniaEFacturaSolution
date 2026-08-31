using RomaniaEFactura.Ubl;

namespace RomaniaEFactura.Tests.Oracle;

/// <summary>
/// Checks the library's output against ANAF's own offline validator.
/// </summary>
/// <remarks>
/// This is the milestone's acceptance criterion in miniature: whatever the models emit must be
/// accepted by the authority, not merely by our reading of the specification.
/// </remarks>
public class AnafValidatorOracleTests
{
    [RequiresAnafValidatorFact]
    public void MinimalInvoice_IsAcceptedByAnafValidator()
    {
        var xml = UblSerializer.Serialize(SampleDocuments.MinimalInvoice());

        var result = AnafValidator.Validate(xml, "FACT1");

        Assert.True(result.IsValid, $"ANAF validator rejected the invoice — {result}");
    }

    [RequiresAnafValidatorFact]
    public void MinimalCreditNote_IsAcceptedByAnafValidator()
    {
        var xml = UblSerializer.Serialize(SampleDocuments.MinimalCreditNote());

        var result = AnafValidator.Validate(xml, "FCN");

        Assert.True(result.IsValid, $"ANAF validator rejected the credit note — {result}");
    }

    [RequiresAnafValidatorFact]
    public void WrongCustomizationId_IsRejectedWithBrRo001()
    {
        // Proves the oracle actually discriminates: a document that should fail, does, with the
        // rule code we expect. Without this, a broken harness that always reports "valid" would
        // make the two tests above meaningless.
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.CustomizationId =
            "urn:cen.eu:en16931:2017#compliant#urn:efactura.mfinante.ro:CIUS-RO:1.0.0";

        var result = AnafValidator.Validate(UblSerializer.Serialize(invoice), "FACT1");

        Assert.False(result.IsValid);
        Assert.Contains("BR-RO-001", result.Codes);
    }

    [RequiresAnafValidatorFact]
    public void BuyerCifWithBadControlDigit_IsRejected()
    {
        // ANAF checks the Romanian CIF control digit outside the CIUS-RO Schematron, so the C#
        // engine has to implement it too. This pins the behaviour the port must reproduce.
        var invoice = SampleDocuments.MinimalInvoice();
        invoice.AccountingCustomerParty.Party.PartyLegalEntity!.CompanyId =
            new Identifier("RO23456784");   // last digit altered; control digit no longer valid
        invoice.AccountingCustomerParty.Party.PartyTaxSchemes[0].CompanyId = "RO23456784";

        var result = AnafValidator.Validate(UblSerializer.Serialize(invoice), "FACT1");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Parse_ValidReport_ReportsValid()
    {
        var result = AnafValidator.Parse("document.xml este valid.\n-------\n");

        Assert.True(result.IsValid);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Parse_RuleFinding_ExtractsCodeAndMessage()
    {
        const string report = """
            document.xml are erori.
            	- textEroare=[BR-RO-001]-Identificatorul specificatie (BT-24) trebuie sa corespunda.        #The specification identifier must correspond.

            -------
            """;

        var result = AnafValidator.Parse(report);

        Assert.False(result.IsValid);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("BR-RO-001", finding.Code);
        Assert.StartsWith("Identificatorul specificatie", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_FindingWithoutRuleCode_KeepsMessageAndReportsNoCode()
    {
        // The CIF control-digit check and XSD failures arrive without a rule code.
        var result = AnafValidator.Parse("document.xml are erori.\n\t- textEroare=CUI cumparator incorect\n");

        Assert.False(result.IsValid);
        var finding = Assert.Single(result.Findings);
        Assert.Null(finding.Code);
        Assert.Equal("CUI cumparator incorect", finding.Message);
        Assert.Empty(result.Codes);
    }
}
