using FluentAssertions;
using liteapi.Models;
using Xunit;

namespace liteapi.Tests.Models;

/// <summary>
/// Unit tests for RequestContext
/// </summary>
public class RequestContextTests
{
    [Fact]
    public void RequestContext_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var context = new RequestContext();

        // Assert
        context.UserId.Should().Be(0);
        context.SessionToken.Should().BeNull();
        context.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public void IsAuthenticated_WhenUserIdIsZero_ShouldReturnFalse()
    {
        // Arrange
        var context = new RequestContext
        {
            UserId = 0,
            SessionToken = "some-token"
        };

        // Act & Assert
        context.IsAuthenticated.Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(12345)]
    [InlineData(ulong.MaxValue)]
    public void IsAuthenticated_WhenUserIdIsGreaterThanZero_ShouldReturnTrue(ulong userId)
    {
        // Arrange
        var context = new RequestContext
        {
            UserId = userId
        };

        // Act & Assert
        context.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void SetUserId_ShouldUpdateIsAuthenticated()
    {
        // Arrange
        var context = new RequestContext();

        // Act
        context.UserId = 12345;

        // Assert
        context.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void SessionToken_CanBeSetAndRetrieved()
    {
        // Arrange
        var context = new RequestContext();
        var expectedToken = "test-session-token-123";

        // Act
        context.SessionToken = expectedToken;

        // Assert
        context.SessionToken.Should().Be(expectedToken);
    }

    [Fact]
    public void RequestContext_FullyPopulated_ShouldHaveAllProperties()
    {
        // Arrange
        var userId = 12345ul;
        var sessionToken = "session-token-xyz";

        // Act
        var context = new RequestContext
        {
            UserId = userId,
            SessionToken = sessionToken
        };

        // Assert
        context.UserId.Should().Be(userId);
        context.SessionToken.Should().Be(sessionToken);
        context.IsAuthenticated.Should().BeTrue();
    }
}
