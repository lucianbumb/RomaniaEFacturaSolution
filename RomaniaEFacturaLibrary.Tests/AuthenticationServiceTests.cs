using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RomaniaEFacturaLibrary.Configuration;
using RomaniaEFacturaLibrary.Models.Authentication;
using RomaniaEFacturaLibrary.Services.Authentication;
using System.Net;

namespace RomaniaEFacturaLibrary.Tests;

[TestFixture]
public class AuthenticationServiceTests
{
    private Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private HttpClient _httpClient;
    private IOptions<EFacturaConfig> _config;
    private ILogger<AuthenticationService> _logger;
    private AuthenticationService _authService;

    [SetUp]
    public void Setup()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);
        
        _config = Options.Create(new EFacturaConfig
        {
            Environment = EFacturaEnvironment.Test,
            CertificatePath = "", // No certificate for unit tests
            CertificatePassword = "",
            Cif = "12345678",
            TimeoutSeconds = 30
        });
        
        _logger = Mock.Of<ILogger<AuthenticationService>>();
        _authService = new AuthenticationService(_httpClient, _config, _logger);
    }

    [TearDown]
    public void TearDown()
    {
        _httpClient?.Dispose();
    }

    [Test]
    public void EFacturaConfig_TestEnvironment_HasCorrectUrls()
    {
        // Arrange
        var config = new EFacturaConfig { Environment = EFacturaEnvironment.Test };

        // Assert
        Assert.That(config.BaseUrl, Does.Contain("test"));
        Assert.That(config.OAuthUrl, Is.Not.Null);
        Assert.That(config.TokenUrl, Is.Not.Null);
    }

    [Test]
    public void EFacturaConfig_ProductionEnvironment_HasCorrectUrls()
    {
        // Arrange
        var config = new EFacturaConfig { Environment = EFacturaEnvironment.Production };

        // Assert
        Assert.That(config.BaseUrl, Does.Contain("prod"));
        Assert.That(config.OAuthUrl, Is.Not.Null);
        Assert.That(config.TokenUrl, Is.Not.Null);
    }

    [Test]
    public void TokenResponse_ValidToken_IsValidReturnsTrue()
    {
        // Arrange
        var token = new TokenResponse
        {
            AccessToken = "test-token",
            ExpiresIn = 3600,
            CreatedAt = DateTime.UtcNow
        };

        // Assert
        Assert.That(token.IsValid, Is.True);
    }

    [Test]
    public void TokenResponse_ExpiredToken_IsValidReturnsFalse()
    {
        // Arrange
        var token = new TokenResponse
        {
            AccessToken = "test-token",
            ExpiresIn = 3600,
            CreatedAt = DateTime.UtcNow.AddHours(-2)
        };

        // Assert
        Assert.That(token.IsValid, Is.False);
    }
}
