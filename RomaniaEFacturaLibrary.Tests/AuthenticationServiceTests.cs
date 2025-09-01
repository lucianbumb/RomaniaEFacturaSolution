using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using RomaniaEFacturaLibrary.Configuration;
using RomaniaEFacturaLibrary.Models.Authentication;
using RomaniaEFacturaLibrary.Services.Authentication;
using System.Net;
using Microsoft.Extensions.Caching.Memory;

namespace RomaniaEFacturaLibrary.Tests;

public class AuthenticationServiceTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly IOptions<EFacturaConfig> _config;
    private readonly ILogger<AuthenticationService> _logger;
    private readonly Mock<ITokenStorageService> _tokenStorageMock;
    private readonly AuthenticationService _authService;

    public AuthenticationServiceTests()
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
        _tokenStorageMock = new Mock<ITokenStorageService>();
        
        _authService = new AuthenticationService(
            _httpClientFactoryMock.Object, 
            _config, 
            _logger,
            _tokenStorageMock.Object);
    }

    [Fact]
    public void EFacturaConfig_TestEnvironment_HasCorrectUrls()
    {
        // Arrange
        var config = new EFacturaConfig { Environment = EFacturaEnvironment.Test };

        // Assert
        Assert.Contains("test", config.BaseUrl);
        Assert.NotNull(config.AuthorizeUrl);
        Assert.NotNull(config.TokenUrl);
        Assert.Contains("logincert.anaf.ro", config.OAuthBaseUrl);
    }

    [Fact]
    public void EFacturaConfig_ProductionEnvironment_HasCorrectUrls()
    {
        // Arrange
        var config = new EFacturaConfig { Environment = EFacturaEnvironment.Production };

        // Assert
        Assert.Contains("prod", config.BaseUrl);
        Assert.NotNull(config.AuthorizeUrl);
        Assert.NotNull(config.TokenUrl);
        Assert.Contains("logincert.anaf.ro", config.OAuthBaseUrl);
    }

    [Fact]
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
        Assert.Contains("logincert.anaf.ro", authUrl);
        Assert.Contains("response_type=code", authUrl);
        Assert.Contains($"client_id={clientId}", authUrl);
        Assert.Contains("token_content_type=jwt", authUrl);
        Assert.Contains($"scope={scope}", authUrl);
        Assert.Contains($"state={state}", authUrl);
    }

    [Fact]
    public void GetAuthorizationUrl_WithConfigValues_ReturnsCorrectUrl()
    {
        // Act
        var authUrl = _authService.GetAuthorizationUrl("efactura", "test-state");

        // Assert
        Assert.Contains("logincert.anaf.ro", authUrl);
        Assert.Contains("response_type=code", authUrl);
        Assert.Contains("client_id=test-client-id", authUrl);
        Assert.Contains("redirect_uri=", authUrl); // Just check the parameter exists
        Assert.Contains("localhost", authUrl); // Check the host is there
        Assert.Contains("callback", authUrl); // Check the path is there
        Assert.Contains("token_content_type=jwt", authUrl);
        Assert.Contains("scope=efactura", authUrl);
        Assert.Contains("state=test-state", authUrl);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_NoToken_ThrowsAuthenticationException()
    {
        // Arrange - setup token storage to return null
        _tokenStorageMock.Setup(x => x.GetTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((TokenDto?)null);

        // Act & Assert
        await Assert.ThrowsAsync<AuthenticationException>(
            () => _authService.GetValidAccessTokenAsync());
    }

    [Fact]
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
        Assert.True(token.IsValid);
    }

    [Fact]
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
        Assert.False(token.IsValid);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_ValidToken_ReturnsToken()
    {
        // Arrange
        var validTokenDto = new TokenDto
        {
            AccessToken = "valid-token",
            ExpiresIn = 3600,
            CreatedAt = DateTime.UtcNow,
            UserName = "default_user"
        };
        
        _tokenStorageMock.Setup(x => x.GetTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(validTokenDto);

        // Act
        var accessToken = await _authService.GetValidAccessTokenAsync();

        // Assert
        Assert.Equal("valid-token", accessToken);
    }
}
