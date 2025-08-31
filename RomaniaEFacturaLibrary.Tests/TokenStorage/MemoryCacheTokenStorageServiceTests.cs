using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using RomaniaEFacturaLibrary.Models.Authentication;
using RomaniaEFacturaLibrary.Services.TokenStorage;
using System.Security.Claims;
using Xunit;

namespace RomaniaEFacturaLibrary.Tests.TokenStorage;

public class MemoryCacheTokenStorageServiceTests
{
    private readonly Mock<IMemoryCache> _memoryCacheMock;
    private readonly Mock<ILogger<MemoryCacheTokenStorageService>> _loggerMock;
    private readonly MemoryCacheTokenStorageService _service;

    public MemoryCacheTokenStorageServiceTests()
    {
        _memoryCacheMock = new Mock<IMemoryCache>();
        _loggerMock = new Mock<ILogger<MemoryCacheTokenStorageService>>();
        _service = new MemoryCacheTokenStorageService(_memoryCacheMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task SetTokenAsync_WithValidUserName_StoresTokenInCache()
    {
        // Arrange
        var userName = "testuser";
        var token = new TokenDto
        {
            AccessToken = "test_access_token",
            RefreshToken = "test_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        var cacheEntry = new Mock<ICacheEntry>();
        _memoryCacheMock.Setup(x => x.CreateEntry(It.IsAny<object>())).Returns(cacheEntry.Object);

        // Act
        await _service.SetTokenAsync(userName, token);

        // Assert
        _memoryCacheMock.Verify(x => x.CreateEntry("efactura_token_testuser_efactura"), Times.Once);
        Assert.Equal(userName, token.UserName);
    }

    [Fact]
    public async Task SetTokenAsync_WithNullUserName_ThrowsArgumentException()
    {
        // Arrange
        var token = new TokenDto
        {
            AccessToken = "test_access_token",
            RefreshToken = "test_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SetTokenAsync(null!, token));
    }

    [Fact]
    public async Task SetTokenAsync_WithNullToken_ThrowsArgumentNullException()
    {
        // Arrange
        var userName = "testuser";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.SetTokenAsync(userName, null!));
    }

    [Fact]
    public async Task GetTokenAsync_WithValidToken_ReturnsToken()
    {
        // Arrange
        var userName = "testuser";
        var token = new TokenDto
        {
            AccessToken = "test_access_token",
            RefreshToken = "test_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            UserName = userName
        };

        object? cacheValue = token;
        _memoryCacheMock.Setup(x => x.TryGetValue("efactura_token_testuser_efactura", out cacheValue))
                       .Returns(true);

        // Act
        var result = await _service.GetTokenAsync(userName);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(token.AccessToken, result.AccessToken);
        Assert.Equal(userName, result.UserName);
    }

    [Fact]
    public async Task GetTokenAsync_WithExpiredToken_ReturnsNull()
    {
        // Arrange
        var userName = "testuser";
        var expiredToken = new TokenDto
        {
            AccessToken = "test_access_token",
            RefreshToken = "test_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1), // Expired
            UserName = userName
        };

        object? cacheValue = expiredToken;
        _memoryCacheMock.Setup(x => x.TryGetValue("efactura_token_testuser_efactura", out cacheValue))
                       .Returns(true);

        // Act
        var result = await _service.GetTokenAsync(userName);

        // Assert
        Assert.Null(result);
        _memoryCacheMock.Verify(x => x.Remove("efactura_token_testuser_efactura"), Times.Once);
    }

    [Fact]
    public async Task SetTokenAsync_WithHttpContext_ExtractsUserNameAndStoresToken()
    {
        // Arrange
        var httpContext = CreateHttpContextWithUser("testuser");
        var token = new TokenDto
        {
            AccessToken = "test_access_token",
            RefreshToken = "test_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        var cacheEntry = new Mock<ICacheEntry>();
        _memoryCacheMock.Setup(x => x.CreateEntry(It.IsAny<object>())).Returns(cacheEntry.Object);

        // Act
        await _service.SetTokenAsync(httpContext, token);

        // Assert
        _memoryCacheMock.Verify(x => x.CreateEntry("efactura_token_testuser_efactura"), Times.Once);
        Assert.Equal("testuser", token.UserName);
    }

    [Fact]
    public async Task GetTokenAsync_WithHttpContext_ExtractsUserNameAndReturnsToken()
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

        object? cacheValue = token;
        _memoryCacheMock.Setup(x => x.TryGetValue("efactura_token_testuser_efactura", out cacheValue))
                       .Returns(true);

        // Act
        var result = await _service.GetTokenAsync(httpContext);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(token.AccessToken, result.AccessToken);
    }

    [Fact]
    public async Task HasValidTokenAsync_WithValidToken_ReturnsTrue()
    {
        // Arrange
        var userName = "testuser";
        var token = new TokenDto
        {
            AccessToken = "test_access_token",
            RefreshToken = "test_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            UserName = userName
        };

        object? cacheValue = token;
        _memoryCacheMock.Setup(x => x.TryGetValue("efactura_token_testuser_efactura", out cacheValue))
                       .Returns(true);

        // Act
        var result = await _service.HasValidTokenAsync(userName);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task HasValidTokenAsync_WithExpiredToken_ReturnsFalse()
    {
        // Arrange
        var userName = "testuser";
        var expiredToken = new TokenDto
        {
            AccessToken = "test_access_token",
            RefreshToken = "test_refresh_token",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1), // Expired
            UserName = userName
        };

        object? cacheValue = expiredToken;
        _memoryCacheMock.Setup(x => x.TryGetValue("efactura_token_testuser_efactura", out cacheValue))
                       .Returns(true);

        // Act
        var result = await _service.HasValidTokenAsync(userName);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RemoveTokenAsync_WithValidUserName_RemovesTokenFromCache()
    {
        // Arrange
        var userName = "testuser";

        // Act
        await _service.RemoveTokenAsync(userName);

        // Assert
        _memoryCacheMock.Verify(x => x.Remove("efactura_token_testuser_efactura"), Times.Once);
    }

    private static HttpContext CreateHttpContextWithUser(string userName)
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
        return httpContext;
    }
}
