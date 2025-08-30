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
    private Mock<IHttpClientFactory> _httpClientFactoryMock;
    private IOptions<EFacturaConfig> _config;
    private ILogger<AuthenticationService> _logger;
    private AuthenticationService _authService;

    [SetUp]
    public void Setup()
    {
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var httpClient = new HttpClient();
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);
        
        _config = Options.Create(new EFacturaConfig
        {
            Environment = EFacturaEnvironment.Test,
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            RedirectUri = "https://localhost:5000/callback",
            Cif = "12345678",
            TimeoutSeconds = 30
        });
        
        _logger = Mock.Of<ILogger<AuthenticationService>>();
        _authService = new AuthenticationService(_httpClientFactoryMock.Object, _config, _logger);
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
        Assert.That(config.OAuthBaseUrl, Does.Contain("logincert.anaf.ro"));
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
        Assert.That(config.OAuthBaseUrl, Does.Contain("logincert.anaf.ro"));
    }

    [Test]
    public void GetAuthorizationUrl_ValidParameters_ReturnsCorrectUrl()
    {
        // Arrange
        var clientId = "test-client";
        var redirectUri = "https://localhost:5000/callback";
        var scope = "efactura";
        var state = "test-state";

        // Act
        var authUrl = _authService.GetAuthorizationUrl(clientId, redirectUri, scope, state);

        // Assert
        Assert.That(authUrl, Does.Contain("logincert.anaf.ro"));
        Assert.That(authUrl, Does.Contain("response_type=code"));
        Assert.That(authUrl, Does.Contain($"client_id={clientId}"));
        Assert.That(authUrl, Does.Contain("token_content_type=jwt"));
        Assert.That(authUrl, Does.Contain($"scope={scope}"));
        Assert.That(authUrl, Does.Contain($"state={state}"));
    }

    [Test]
    public void GetAuthorizationUrl_WithConfigValues_ReturnsCorrectUrl()
    {
        // Act
        var authUrl = _authService.GetAuthorizationUrl("efactura", "test-state");

        // Assert
        Assert.That(authUrl, Does.Contain("logincert.anaf.ro"));
        Assert.That(authUrl, Does.Contain("response_type=code"));
        Assert.That(authUrl, Does.Contain("client_id=test-client-id"));
        Assert.That(authUrl, Does.Contain("redirect_uri=https%3A%2F%2Flocalhost%3A5000%2Fcallback"));
        Assert.That(authUrl, Does.Contain("token_content_type=jwt"));
        Assert.That(authUrl, Does.Contain("scope=efactura"));
        Assert.That(authUrl, Does.Contain("state=test-state"));
    }

    [Test]
    public void GetValidAccessTokenAsync_NoToken_ThrowsAuthenticationException()
    {
        // Act & Assert
        Assert.ThrowsAsync<AuthenticationException>(
            async () => await _authService.GetValidAccessTokenAsync());
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
    public async Task GetValidAccessTokenAsync_ValidToken_ReturnsToken()
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
        var accessToken = await _authService.GetValidAccessTokenAsync();

        // Assert
        Assert.That(accessToken, Is.EqualTo("valid-token"));
    }
}
