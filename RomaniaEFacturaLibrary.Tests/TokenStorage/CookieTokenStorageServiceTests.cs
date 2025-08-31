using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using RomaniaEFacturaLibrary.Models.Authentication;
using RomaniaEFacturaLibrary.Services.TokenStorage;
using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace RomaniaEFacturaLibrary.Tests.TokenStorage;

public class CookieTokenStorageServiceTests
{
    private readonly Mock<ILogger<CookieTokenStorageService>> _loggerMock;
    private readonly CookieTokenStorageService _service;

    public CookieTokenStorageServiceTests()
    {
        _loggerMock = new Mock<ILogger<CookieTokenStorageService>>();
        _service = new CookieTokenStorageService(_loggerMock.Object);
    }

    [Fact]
    public async Task SetTokenAsync_WithStringUserName_ThrowsInvalidOperationException()
    {
        // Arrange
        var userName = "testuser";
        var token = new TokenDto
        {
            AccessToken = "test_access_token",
            RefreshToken = "test_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SetTokenAsync(userName, token));
        
        Assert.Contains("Cookie storage requires HttpContext", exception.Message);
    }

    [Fact]
    public async Task GetTokenAsync_WithStringUserName_ThrowsInvalidOperationException()
    {
        // Arrange
        var userName = "testuser";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.GetTokenAsync(userName));
        
        Assert.Contains("Cookie storage requires HttpContext", exception.Message);
    }

    [Fact]
    public async Task SetTokenAsync_WithNullHttpContext_ThrowsArgumentNullException()
    {
        // Arrange
        HttpContext? httpContext = null;
        var token = new TokenDto
        {
            AccessToken = "test_access_token",
            RefreshToken = "test_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.SetTokenAsync(httpContext!, token));
    }

    [Fact]
    public async Task SetTokenAsync_WithNullToken_ThrowsArgumentNullException()
    {
        // Arrange
        var httpContext = CreateHttpContextWithUser("testuser");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.SetTokenAsync(httpContext, null!));
    }

    [Fact]
    public async Task SetTokenAsync_WithHttpContext_SetsCookieWithCorrectOptions()
    {
        // Arrange
        var httpContext = CreateHttpContextWithUser("testuser");
        var token = new TokenDto
        {
            AccessToken = "test_access_token",
            RefreshToken = "test_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        var mockCookies = new Mock<IResponseCookies>();
        httpContext.Response.Cookies.Returns(mockCookies.Object);

        // Act
        await _service.SetTokenAsync(httpContext, token);

        // Assert
        mockCookies.Verify(x => x.Append(
            "efactura_token_testuser_efactura",
            It.IsAny<string>(),
            It.Is<CookieOptions>(opts => 
                opts.HttpOnly == true &&
                opts.SameSite == SameSiteMode.Strict &&
                opts.IsEssential == true)
        ), Times.Once);

        Assert.Equal("testuser", token.UserName);
    }

    [Fact]
    public async Task GetTokenAsync_WithValidCookie_ReturnsToken()
    {
        // Arrange
        var httpContext = CreateHttpContextWithUser("testuser");
        var token = new TokenDto
        {
            AccessToken = "test_access_token",
            RefreshToken = "test_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            UserName = "testuser"
        };

        var tokenJson = JsonSerializer.Serialize(token);
        var mockCookies = new Mock<IRequestCookieCollection>();
        mockCookies.Setup(x => x.TryGetValue("efactura_token_testuser_efactura", out tokenJson))
                  .Returns(true);
        httpContext.Request.Cookies.Returns(mockCookies.Object);

        // Act
        var result = await _service.GetTokenAsync(httpContext);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(token.AccessToken, result.AccessToken);
        Assert.Equal(token.UserName, result.UserName);
    }

    [Fact]
    public async Task GetTokenAsync_WithExpiredCookie_ReturnsNullAndDeletesCookie()
    {
        // Arrange
        var httpContext = CreateHttpContextWithUser("testuser");
        var expiredToken = new TokenDto
        {
            AccessToken = "test_access_token",
            RefreshToken = "test_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1), // Expired
            UserName = "testuser"
        };

        var tokenJson = JsonSerializer.Serialize(expiredToken);
        var mockRequestCookies = new Mock<IRequestCookieCollection>();
        mockRequestCookies.Setup(x => x.TryGetValue("efactura_token_testuser_efactura", out tokenJson))
                         .Returns(true);
        httpContext.Request.Cookies.Returns(mockRequestCookies.Object);

        var mockResponseCookies = new Mock<IResponseCookies>();
        httpContext.Response.Cookies.Returns(mockResponseCookies.Object);

        // Act
        var result = await _service.GetTokenAsync(httpContext);

        // Assert
        Assert.Null(result);
        mockResponseCookies.Verify(x => x.Delete("efactura_token_testuser_efactura"), Times.Once);
    }

    [Fact]
    public async Task GetTokenAsync_WithNoCookie_ReturnsNull()
    {
        // Arrange
        var httpContext = CreateHttpContextWithUser("testuser");
        var mockCookies = new Mock<IRequestCookieCollection>();
        string? nullValue = null;
        mockCookies.Setup(x => x.TryGetValue("efactura_token_testuser_efactura", out nullValue))
                  .Returns(false);
        httpContext.Request.Cookies.Returns(mockCookies.Object);

        // Act
        var result = await _service.GetTokenAsync(httpContext);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveTokenAsync_WithHttpContext_DeletesCookie()
    {
        // Arrange
        var httpContext = CreateHttpContextWithUser("testuser");
        var mockCookies = new Mock<IResponseCookies>();
        httpContext.Response.Cookies.Returns(mockCookies.Object);

        // Act
        await _service.RemoveTokenAsync(httpContext);

        // Assert
        mockCookies.Verify(x => x.Delete("efactura_token_testuser_efactura"), Times.Once);
    }

    [Fact]
    public async Task HasValidTokenAsync_WithValidCookie_ReturnsTrue()
    {
        // Arrange
        var httpContext = CreateHttpContextWithUser("testuser");
        var token = new TokenDto
        {
            AccessToken = "test_access_token",
            RefreshToken = "test_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            UserName = "testuser"
        };

        var tokenJson = JsonSerializer.Serialize(token);
        var mockCookies = new Mock<IRequestCookieCollection>();
        mockCookies.Setup(x => x.TryGetValue("efactura_token_testuser_efactura", out tokenJson))
                  .Returns(true);
        httpContext.Request.Cookies.Returns(mockCookies.Object);

        // Act
        var result = await _service.HasValidTokenAsync(httpContext);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasValidTokenAsync_WithExpiredCookie_ReturnsFalse()
    {
        // Arrange
        var httpContext = CreateHttpContextWithUser("testuser");
        var expiredToken = new TokenDto
        {
            AccessToken = "test_access_token",
            RefreshToken = "test_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1), // Expired
            UserName = "testuser"
        };

        var tokenJson = JsonSerializer.Serialize(expiredToken);
        var mockRequestCookies = new Mock<IRequestCookieCollection>();
        mockRequestCookies.Setup(x => x.TryGetValue("efactura_token_testuser_efactura", out tokenJson))
                         .Returns(true);
        httpContext.Request.Cookies.Returns(mockRequestCookies.Object);

        var mockResponseCookies = new Mock<IResponseCookies>();
        httpContext.Response.Cookies.Returns(mockResponseCookies.Object);

        // Act
        var result = await _service.HasValidTokenAsync(httpContext);

        // Assert
        Assert.False(result);
    }

    private static DefaultHttpContext CreateHttpContextWithUser(string userName)
    {
        var httpContext = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, userName),
            new(ClaimTypes.NameIdentifier, userName)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);
        httpContext.User = principal;

        // Mock the request to return HTTPS
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("localhost");

        return httpContext;
    }
}
