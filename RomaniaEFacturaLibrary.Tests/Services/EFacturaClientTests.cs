using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RomaniaEFacturaLibrary.Configuration;
using RomaniaEFacturaLibrary.Models.Api;
using RomaniaEFacturaLibrary.Models.Ubl;
using RomaniaEFacturaLibrary.Services;
using RomaniaEFacturaLibrary.Services.Api;
using RomaniaEFacturaLibrary.Services.Xml;
using Xunit;

namespace RomaniaEFacturaLibrary.Tests.Services;

public class EFacturaClientTests
{
    private readonly Mock<IEFacturaApiClient> _apiClientMock;
    private readonly Mock<IXmlService> _xmlServiceMock;
    private readonly Mock<ILogger<EFacturaClient>> _loggerMock;
    private readonly Mock<IOptions<EFacturaConfig>> _configMock;
    private readonly EFacturaClient _client;

    public EFacturaClientTests()
    {
        _apiClientMock = new Mock<IEFacturaApiClient>();
        _xmlServiceMock = new Mock<IXmlService>();
        _loggerMock = new Mock<ILogger<EFacturaClient>>();
        _configMock = new Mock<IOptions<EFacturaConfig>>();
        
        var config = new EFacturaConfig
        {
            BaseUrl = "https://api.anaf.ro/test",
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret"
        };
        _configMock.Setup(x => x.Value).Returns(config);

        _client = new EFacturaClient(
            _apiClientMock.Object,
            _xmlServiceMock.Object,
            _loggerMock.Object,
            _configMock.Object);
    }

    [Fact]
    public async Task ValidateInvoiceAsync_WithValidInvoice_ReturnsSuccessfulValidation()
    {
        // Arrange
        var cif = "12345678";
        var invoice = CreateTestInvoice();
        var xmlContent = "<xml>test</xml>";
        
        var localValidation = new XmlValidationResult { IsValid = true, Errors = new List<string>() };
        var anafValidation = new ValidationResult { Success = true, Errors = new List<ValidationError>() };

        _xmlServiceMock.Setup(x => x.SerializeInvoiceAsync(invoice, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(xmlContent);
        _xmlServiceMock.Setup(x => x.ValidateXmlAsync(xmlContent, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(localValidation);
        _apiClientMock.Setup(x => x.ValidateInvoiceAsync(xmlContent, cif, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(anafValidation);

        // Act
        var result = await _client.ValidateInvoiceAsync(invoice, cif);

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        _xmlServiceMock.Verify(x => x.SerializeInvoiceAsync(invoice, It.IsAny<CancellationToken>()), Times.Once);
        _xmlServiceMock.Verify(x => x.ValidateXmlAsync(xmlContent, It.IsAny<CancellationToken>()), Times.Once);
        _apiClientMock.Verify(x => x.ValidateInvoiceAsync(xmlContent, cif, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ValidateInvoiceAsync_WithInvalidLocalValidation_ReturnsFailedValidation()
    {
        // Arrange
        var cif = "12345678";
        var invoice = CreateTestInvoice();
        var xmlContent = "<xml>test</xml>";
        
        var localValidation = new XmlValidationResult 
        { 
            IsValid = false, 
            Errors = new List<string> { "Invalid XML structure" } 
        };

        _xmlServiceMock.Setup(x => x.SerializeInvoiceAsync(invoice, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(xmlContent);
        _xmlServiceMock.Setup(x => x.ValidateXmlAsync(xmlContent, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(localValidation);

        // Act
        var result = await _client.ValidateInvoiceAsync(invoice, cif);

        // Assert
        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Equal("Invalid XML structure", result.Errors[0].Message);
        
        // Verify that ANAF validation is not called for locally invalid XML
        _apiClientMock.Verify(x => x.ValidateInvoiceAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadInvoiceAsync_WithValidInvoice_ReturnsSuccessfulUpload()
    {
        // Arrange
        var cif = "12345678";
        var environment = "test";
        var invoice = CreateTestInvoice();
        var xmlContent = "<xml>test</xml>";
        
        var localValidation = new XmlValidationResult { IsValid = true, Errors = new List<string>() };
        var anafValidation = new ValidationResult { Success = true, Errors = new List<ValidationError>() };
        var uploadResponse = new UploadResponse { UploadId = "test-upload-id", Success = true };

        _xmlServiceMock.Setup(x => x.SerializeInvoiceAsync(invoice, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(xmlContent);
        _xmlServiceMock.Setup(x => x.ValidateXmlAsync(xmlContent, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(localValidation);
        _apiClientMock.Setup(x => x.ValidateInvoiceAsync(xmlContent, cif, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(anafValidation);
        _apiClientMock.Setup(x => x.UploadInvoiceAsync(invoice, cif, environment, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(uploadResponse);

        // Act
        var result = await _client.UploadInvoiceAsync(invoice, cif, environment);

        // Assert
        Assert.Equal("test-upload-id", result.UploadId);
        Assert.True(result.Success);
        _apiClientMock.Verify(x => x.UploadInvoiceAsync(invoice, cif, environment, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadInvoiceAsync_WithInvalidInvoice_ThrowsInvalidOperationException()
    {
        // Arrange
        var cif = "12345678";
        var invoice = CreateTestInvoice();
        var xmlContent = "<xml>test</xml>";
        
        var localValidation = new XmlValidationResult { IsValid = true, Errors = new List<string>() };
        var anafValidation = new ValidationResult 
        { 
            Success = false, 
            Errors = new List<ValidationError> 
            { 
                new() { Message = "Invalid invoice data" } 
            } 
        };

        _xmlServiceMock.Setup(x => x.SerializeInvoiceAsync(invoice, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(xmlContent);
        _xmlServiceMock.Setup(x => x.ValidateXmlAsync(xmlContent, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(localValidation);
        _apiClientMock.Setup(x => x.ValidateInvoiceAsync(xmlContent, cif, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(anafValidation);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _client.UploadInvoiceAsync(invoice, cif));
        
        Assert.Contains("Invoice validation failed", exception.Message);
        Assert.Contains("Invalid invoice data", exception.Message);
    }

    [Fact]
    public async Task GetUploadStatusAsync_WithValidUploadId_ReturnsStatus()
    {
        // Arrange
        var uploadId = "test-upload-id";
        var statusResponse = new StatusResponse { Status = "completed", UploadId = uploadId };

        _apiClientMock.Setup(x => x.GetUploadStatusAsync(uploadId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(statusResponse);

        // Act
        var result = await _client.GetUploadStatusAsync(uploadId);

        // Assert
        Assert.Equal("completed", result.Status);
        Assert.Equal(uploadId, result.UploadId);
        _apiClientMock.Verify(x => x.GetUploadStatusAsync(uploadId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetInvoicesAsync_WithValidParameters_ReturnsInvoiceList()
    {
        // Arrange
        var cif = "12345678";
        var from = DateTime.UtcNow.AddDays(-7);
        var to = DateTime.UtcNow;
        
        var messagesResponse = new MessagesResponse
        {
            Messages = new List<MessageInfo>
            {
                new() { Id = "msg1", Cif = cif, Type = "FACT1", CreationDate = from.AddDays(1) },
                new() { Id = "msg2", Cif = cif, Type = "FACT1", CreationDate = from.AddDays(2) }
            }
        };

        _apiClientMock.Setup(x => x.GetMessagesAsync(cif, from, to, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(messagesResponse);

        // Act
        var result = await _client.GetInvoicesAsync(cif, from, to);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("msg1", result[0].Id);
        Assert.Equal("msg2", result[1].Id);
        Assert.All(result, invoice => Assert.Equal(cif, invoice.Cif));
        _apiClientMock.Verify(x => x.GetMessagesAsync(cif, from, to, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DownloadRawInvoiceAsync_WithValidMessageId_ReturnsRawData()
    {
        // Arrange
        var messageId = "test-message-id";
        var rawData = new byte[] { 1, 2, 3, 4, 5 };

        _apiClientMock.Setup(x => x.DownloadInvoiceAsync(messageId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(rawData);

        // Act
        var result = await _client.DownloadRawInvoiceAsync(messageId);

        // Assert
        Assert.Equal(rawData, result);
        _apiClientMock.Verify(x => x.DownloadInvoiceAsync(messageId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConvertToPdfAsync_WithValidInvoice_ReturnsPdfData()
    {
        // Arrange
        var invoice = CreateTestInvoice();
        var xmlContent = "<xml>test</xml>";
        var pdfData = new byte[] { 80, 68, 70 }; // PDF header bytes

        _xmlServiceMock.Setup(x => x.SerializeInvoiceAsync(invoice, It.IsAny<CancellationToken>()))
                      .ReturnsAsync(xmlContent);
        _apiClientMock.Setup(x => x.ConvertXmlToPdfAsync(xmlContent, "FACT1", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(pdfData);

        // Act
        var result = await _client.ConvertToPdfAsync(invoice);

        // Assert
        Assert.Equal(pdfData, result);
        _xmlServiceMock.Verify(x => x.SerializeInvoiceAsync(invoice, It.IsAny<CancellationToken>()), Times.Once);
        _apiClientMock.Verify(x => x.ConvertXmlToPdfAsync(xmlContent, "FACT1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WaitForUploadCompletionAsync_WithSuccessfulCompletion_ReturnsCompletedStatus()
    {
        // Arrange
        var uploadId = "test-upload-id";
        var timeout = TimeSpan.FromSeconds(5);
        
        var completedStatus = new StatusResponse { Status = "completed", UploadId = uploadId };

        _apiClientMock.Setup(x => x.GetUploadStatusAsync(uploadId, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(completedStatus);

        // Act
        var result = await _client.WaitForUploadCompletionAsync(uploadId, timeout);

        // Assert
        Assert.Equal("completed", result.Status);
        Assert.Equal(uploadId, result.UploadId);
    }

    private static UblInvoice CreateTestInvoice()
    {
        return new UblInvoice
        {
            Id = "TEST-001",
            IssueDate = DateTime.UtcNow.Date,
            InvoiceTypeCode = "380",
            DocumentCurrencyCode = "RON",
            AccountingSupplierParty = new Party
            {
                PartyLegalEntity = new PartyLegalEntity
                {
                    RegistrationName = "Test Supplier",
                    CompanyId = "12345678"
                }
            },
            AccountingCustomerParty = new Party
            {
                PartyLegalEntity = new PartyLegalEntity
                {
                    RegistrationName = "Test Customer",
                    CompanyId = "87654321"
                }
            },
            InvoiceLines = new List<InvoiceLine>
            {
                new()
                {
                    Id = "1",
                    InvoicedQuantity = new Quantity { Value = 1, UnitCode = "EA" },
                    LineExtensionAmount = new Amount { Value = 100.00m, CurrencyId = "RON" },
                    Item = new Item { Name = "Test Item" },
                    Price = new Price 
                    { 
                        PriceAmount = new Amount { Value = 100.00m, CurrencyId = "RON" } 
                    }
                }
            },
            LegalMonetaryTotal = new MonetaryTotal
            {
                LineExtensionAmount = new Amount { Value = 100.00m, CurrencyId = "RON" },
                TaxExclusiveAmount = new Amount { Value = 100.00m, CurrencyId = "RON" },
                TaxInclusiveAmount = new Amount { Value = 119.00m, CurrencyId = "RON" },
                PayableAmount = new Amount { Value = 119.00m, CurrencyId = "RON" }
            }
        };
    }
}
