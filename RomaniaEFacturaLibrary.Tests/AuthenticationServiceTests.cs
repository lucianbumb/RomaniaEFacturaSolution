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
        Assert.That(config.AuthorizeUrl, Is.Not.Null);
        Assert.That(config.TokenUrl, Is.Not.Null);
    }

    [Test]
    public void EFacturaConfig_ProductionEnvironment_HasCorrectUrls()
    {
        // Arrange
        var config = new EFacturaConfig { Environment = EFacturaEnvironment.Production };

        // Assert
        Assert.That(config.BaseUrl, Does.Contain("prod"));
        Assert.That(config.AuthorizeUrl, Is.Not.Null);
        Assert.That(config.TokenUrl, Is.Not.Null);
    }

    [Test]
    public void GetAuthorizationUrl_ValidParameters_ReturnsCorrectUrl()
    {
        // Arrange
        var redirectUri = "https://localhost:5000/callback";
        var state = "test-state";

        // Act
        var authUrl = _authService.GetAuthorizationUrl(redirectUri, state);

        // Assert
        Assert.That(authUrl, Does.Contain("logincert.anaf.ro"));
        Assert.That(authUrl, Does.Contain("response_type=code"));
        Assert.That(authUrl, Does.Contain("client_id=12345678"));
        Assert.That(authUrl, Does.Contain($"redirect_uri={Uri.EscapeDataString(redirectUri)}"));
        Assert.That(authUrl, Does.Contain($"state={state}"));
        Assert.That(authUrl, Does.Contain("scope=eFACTURA"));
    }

    [Test]
    public void GetAccessTokenAsync_NoToken_ThrowsAuthenticationException()
    {
        // Act & Assert
        Assert.ThrowsAsync<AuthenticationException>(
            async () => await _authService.GetAccessTokenAsync());
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

    [Test]
    public async Task GetAccessTokenAsync_ValidToken_ReturnsToken()
    {
        // Arrange
        var token = new TokenResponse
        {
            AccessToken = "valid-token",
            ExpiresIn = 3600,
            CreatedAt = DateTime.UtcNow
        };
        
        _authService.SetToken(token);

        // Act
        var accessToken = await _authService.GetAccessTokenAsync();

        // Assert
        Assert.That(accessToken, Is.EqualTo("valid-token"));
    }
}
