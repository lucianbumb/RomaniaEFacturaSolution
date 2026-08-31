using System.Net;
using System.Text;
using RomaniaEFactura.Transport;

namespace RomaniaEFactura.Tests.Transport;

/// <summary>
/// The envelope reader, tested directly on the response bodies ANAF actually sends.
/// </summary>
/// <remarks>
/// Bodies here are taken from ANAF's own OpenAPI examples, committed under
/// <c>tests/fixtures/anaf-openapi</c>. Every one of the failures below arrives with HTTP 200.
/// </remarks>
public class AnafEnvelopeTests
{
    // ------------------------------------------------- failures inside HTTP 200

    [Fact]
    public void UploadRejection_IsDetectedDespiteTheSuccessStatus()
    {
        var error = Detect("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <header xmlns="mfp:anaf:dgti:spv:respUploadFisier:v1" dateResponse="202210121034" ExecutionStatus="1">
                <Errors errorMessage="Nu aveti drept in SPV pentru CIF=1234"/>
            </header>
            """);

        Assert.NotNull(error);
        Assert.Equal(AnafErrorKind.NoRights, error!.Kind);
        Assert.Equal(200, error.StatusCode);
    }

    [Fact]
    public void UploadAcceptance_IsNotAnError()
    {
        var error = Detect("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <header xmlns="mfp:anaf:dgti:spv:respUploadFisier:v1" dateResponse="202108051140" ExecutionStatus="0" index_incarcare="3828"/>
            """);

        Assert.Null(error);
    }

    [Fact]
    public void StatusErrors_AreDetected()
    {
        var error = Detect("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <header xmlns="mfp:anaf:dgti:efactura:stareMesajFactura:v1">
                <Errors errorMessage="Nu exista factura cu id_incarcare= 15000"/>
            </header>
            """);

        Assert.NotNull(error);
        Assert.Equal(AnafErrorKind.NotFound, error!.Kind);
    }

    [Fact]
    public void ResolvedStatus_IsNotAnError()
    {
        var error = Detect("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <header xmlns="mfp:anaf:dgti:efactura:stareMesajFactura:v1" stare="nok" id_descarcare="123"/>
            """);

        // A document ANAF rejected is still a successful status call - stare="nok" is the answer,
        // not a failure to obtain one.
        Assert.Null(error);
    }

    [Fact]
    public void DownloadErrorBody_IsDetectedWhereAZipWasExpected()
    {
        var error = Detect("""{"eroare":"Nu aveti dreptul sa descarcati acesta factura","titlu":"Descarcare mesaj"}""");

        Assert.NotNull(error);
        Assert.Equal(AnafErrorKind.NoRights, error!.Kind);
    }

    [Fact]
    public void ZipBody_IsNeverTreatedAsAnError()
    {
        // Identified by the PK\x03\x04 magic number rather than by content type.
        var zip = new byte[] { (byte)'P', (byte)'K', 3, 4, 20, 0, 0, 0 };

        Assert.Null(AnafEnvelope.DetectError(new RawAnafResponse(HttpStatusCode.OK, zip)));
    }

    [Fact]
    public void PdfBody_IsNeverTreatedAsAnError()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n");

        Assert.Null(AnafEnvelope.DetectError(new RawAnafResponse(HttpStatusCode.OK, pdf)));
    }

    // ------------------------------------------------------------ classification

    [Theory]
    [InlineData("S-au facut deja 20 descarcari de mesaj in cursul zilei", AnafErrorKind.QuotaExhausted)]
    [InlineData("Nu aveti drept in SPV pentru CIF=1234", AnafErrorKind.NoRights)]
    [InlineData("Pentru id=21 nu exista inregistrata nici o factura", AnafErrorKind.NotFound)]
    [InlineData("CIF introdus= 123a nu este un numar", AnafErrorKind.InvalidRequest)]
    [InlineData("Valorile acceptate pentru parametrul standard sunt UBL, CN, CII sau RASP", AnafErrorKind.InvalidRequest)]
    public void AnafsWordingIsClassified(string message, AnafErrorKind expected)
    {
        var error = Detect($$"""{"eroare":"{{message}}","titlu":"Lista Mesaje"}""");

        Assert.NotNull(error);
        Assert.Equal(expected, error!.Kind);
    }

    [Fact]
    public void ExhaustedQuota_IsNotTreatedAsTransient()
    {
        // A rate limit clears in seconds; a spent daily budget does not clear until tomorrow, so
        // retrying it just burns calls.
        var quota = Detect("""{"eroare":"S-au facut deja 10 descarcari de mesaj in cursul zilei"}""");
        var rateLimit = AnafEnvelope.DetectError(
            new RawAnafResponse(HttpStatusCode.TooManyRequests, "rate limited"u8.ToArray()));

        Assert.False(quota!.IsTransient);
        Assert.True(rateLimit!.IsTransient);
    }

    // ------------------------------------------------- genuine transport failures

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AnafErrorKind.NotAuthorized)]
    [InlineData(HttpStatusCode.Forbidden, AnafErrorKind.NotAuthorized)]
    [InlineData(HttpStatusCode.TooManyRequests, AnafErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, AnafErrorKind.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway, AnafErrorKind.ServiceUnavailable)]
    public void RealErrorStatusCodes_AreHonoured(HttpStatusCode status, AnafErrorKind expected)
    {
        var error = AnafEnvelope.DetectError(new RawAnafResponse(status, "{}"u8.ToArray()));

        Assert.NotNull(error);
        Assert.Equal(expected, error!.Kind);
    }

    [Fact]
    public void Unauthorized_KeepsAnafsOwnWording()
    {
        var error = AnafEnvelope.DetectError(new RawAnafResponse(
            HttpStatusCode.Unauthorized, """{"message":"Unauthorized","status":"401"}"""u8.ToArray()));

        Assert.Equal(AnafErrorKind.NotAuthorized, error!.Kind);
        Assert.Equal("Unauthorized", error.Message);
    }

    [Fact]
    public void BadRequest_UsesTheThirdBodyShape()
    {
        // A genuine 400 carries yet another shape, so a client cannot assume one per endpoint.
        var error = AnafEnvelope.DetectError(new RawAnafResponse(
            HttpStatusCode.BadRequest,
            """{"timestamp":"05-08-2021 12:04:01","status":400,"error":"Bad Request","message":"Parametrii standard si cif sunt obligatorii"}"""u8.ToArray()));

        Assert.Equal(AnafErrorKind.InvalidRequest, error!.Kind);
        Assert.Contains("obligatorii", error.Message, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------- malformed input

    [Fact]
    public void EmptyBody_IsReportedAsUnreadable()
    {
        var error = AnafEnvelope.DetectError(new RawAnafResponse(HttpStatusCode.OK, []));

        Assert.Equal(AnafErrorKind.Unreadable, error!.Kind);
    }

    [Fact]
    public void MalformedXml_IsReportedAsUnreadableRatherThanThrowing()
    {
        var error = Detect("<header xmlns=\"mfp:anaf\" unclosed");

        Assert.Equal(AnafErrorKind.Unreadable, error!.Kind);
    }

    [Fact]
    public void ByteOrderMark_DoesNotPreventDetection()
    {
        // A BOM ahead of the declaration would otherwise make the body look like neither XML nor
        // JSON, and the error would be missed entirely.
        var body = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            .GetBytes("""{"eroare":"Nu aveti drept in SPV pentru CIF=1234"}""");

        var error = AnafEnvelope.DetectError(new RawAnafResponse(HttpStatusCode.OK, body));

        Assert.Equal(AnafErrorKind.NoRights, error!.Kind);
    }

    private static AnafError? Detect(string body) =>
        AnafEnvelope.DetectError(new RawAnafResponse(
            HttpStatusCode.OK, new UTF8Encoding(false).GetBytes(body)));
}
