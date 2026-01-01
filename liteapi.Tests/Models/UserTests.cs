using FluentAssertions;
using liteapi.Models;
using Xunit;

namespace liteapi.Tests.Models;

/// <summary>
/// Unit tests for User entity
/// </summary>
public class UserTests
{
    [Fact]
    public void User_DefaultValues_ShouldBeCorrect()
    {
        // Arrange & Act
        var user = new User();

        // Assert
        user.UserId.Should().Be(0);
        user.Username.Should().BeNullOrEmpty();
        user.Email.Should().BeNullOrEmpty();
        user.Level.Should().Be(1); // Default level is 1 for new users
        user.Experience.Should().Be(0);
        user.Gold.Should().Be(0);
        user.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        user.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void User_CanSetAllProperties()
    {
        // Arrange
        var userId = 12345ul;
        var username = "TestUser";
        var email = "test@example.com";
        var level = 10;
        var experience = 5000;
        var gold = 10000;
        var now = DateTime.UtcNow;

        // Act
        var user = new User
        {
            UserId = userId,
            Username = username,
            Email = email,
            Level = level,
            Experience = experience,
            Gold = gold,
            CreatedAt = now,
            UpdatedAt = now
        };

        // Assert
        user.UserId.Should().Be(userId);
        user.Username.Should().Be(username);
        user.Email.Should().Be(email);
        user.Level.Should().Be(level);
        user.Experience.Should().Be(experience);
        user.Gold.Should().Be(gold);
        user.CreatedAt.Should().Be(now);
        user.UpdatedAt.Should().Be(now);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(-100)]
    public void User_GoldValue_CanBeNegative(int goldAmount)
    {
        // Arrange
        var user = new User();

        // Act
        user.Gold = goldAmount;

        // Assert
        user.Gold.Should().Be(goldAmount);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(10, 5000)]
    [InlineData(100, 999999)]
    public void User_LevelAndExperience_CanBeSet(int level, int experience)
    {
        // Arrange
        var user = new User();

        // Act
        user.Level = level;
        user.Experience = experience;

        // Assert
        user.Level.Should().Be(level);
        user.Experience.Should().Be(experience);
    }

    [Fact]
    public void User_Timestamps_ShouldBeIndependent()
    {
        // Arrange
        var createdAt = DateTime.UtcNow;
        var updatedAt = createdAt.AddHours(1);

        // Act
        var user = new User
        {
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };

        // Assert
        user.CreatedAt.Should().Be(createdAt);
        user.UpdatedAt.Should().Be(updatedAt);
        user.UpdatedAt.Should().BeAfter(user.CreatedAt);
    }
}
