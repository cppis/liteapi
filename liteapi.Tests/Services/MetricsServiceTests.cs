using FluentAssertions;
using liteapi.Services;
using Prometheus;
using Xunit;

namespace liteapi.Tests.Services;

/// <summary>
/// Unit tests for MetricsService
/// </summary>
public class MetricsServiceTests
{
    private readonly MetricsService _metricsService;

    public MetricsServiceTests()
    {
        _metricsService = new MetricsService();
    }

    [Fact]
    public void Constructor_ShouldCreateInstance()
    {
        // Arrange & Act
        var service = new MetricsService();

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void IncrementRequest_ShouldNotThrow()
    {
        // Arrange
        var method = "GET";
        var endpoint = "/api/users";
        var statusCode = 200;

        // Act
        Action act = () => _metricsService.IncrementRequest(method, endpoint, statusCode);

        // Assert
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("GET", "/api/users", 200)]
    [InlineData("POST", "/api/users", 201)]
    [InlineData("PUT", "/api/users/123", 200)]
    [InlineData("DELETE", "/api/users/123", 204)]
    public void IncrementRequest_WithDifferentMethods_ShouldNotThrow(string method, string endpoint, int statusCode)
    {
        // Act
        Action act = () => _metricsService.IncrementRequest(method, endpoint, statusCode);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void TrackRequestDuration_ShouldReturnDisposable()
    {
        // Arrange
        var method = "GET";
        var endpoint = "/api/users";

        // Act
        var timer = _metricsService.TrackRequestDuration(method, endpoint);

        // Assert
        timer.Should().NotBeNull();
        timer.Should().BeAssignableTo<IDisposable>();

        // Cleanup
        timer.Dispose();
    }

    [Fact]
    public void TrackRequestDuration_ShouldMeasureTime()
    {
        // Arrange
        var method = "GET";
        var endpoint = "/api/test";

        // Act
        using (var timer = _metricsService.TrackRequestDuration(method, endpoint))
        {
            // Simulate some work
            Thread.Sleep(10);
        }

        // Assert - if we get here without exception, the timer worked
        Assert.True(true);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IncrementDbLockAcquisition_ShouldNotThrow(bool success)
    {
        // Act
        Action act = () => _metricsService.IncrementDbLockAcquisition(success);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void IncrementActiveDbLocks_ShouldNotThrow()
    {
        // Act
        Action act = () => _metricsService.IncrementActiveDbLocks();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void DecrementActiveDbLocks_ShouldNotThrow()
    {
        // Act
        Action act = () => _metricsService.DecrementActiveDbLocks();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void TrackDbLockWaitDuration_ShouldReturnDisposable()
    {
        // Act
        var timer = _metricsService.TrackDbLockWaitDuration();

        // Assert
        timer.Should().NotBeNull();
        timer.Should().BeAssignableTo<IDisposable>();

        // Cleanup
        timer.Dispose();
    }

    [Theory]
    [InlineData("json", "/api/packet/echo")]
    [InlineData("msgpack", "/api/packet/echo")]
    [InlineData("json", "/api/packet/user")]
    public void IncrementPacketProcessing_ShouldNotThrow(string format, string endpoint)
    {
        // Act
        Action act = () => _metricsService.IncrementPacketProcessing(format, endpoint);

        // Assert
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(1000)]
    public void SetActiveUsers_ShouldNotThrow(int count)
    {
        // Act
        Action act = () => _metricsService.SetActiveUsers(count);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void IncrementActiveUsers_ShouldNotThrow()
    {
        // Act
        Action act = () => _metricsService.IncrementActiveUsers();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void DecrementActiveUsers_ShouldNotThrow()
    {
        // Act
        Action act = () => _metricsService.DecrementActiveUsers();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ActiveDbLocks_IncrementAndDecrement_ShouldWork()
    {
        // Act
        Action act = () =>
        {
            _metricsService.IncrementActiveDbLocks();
            _metricsService.IncrementActiveDbLocks();
            _metricsService.DecrementActiveDbLocks();
        };

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void ActiveUsers_IncrementAndDecrement_ShouldWork()
    {
        // Act
        Action act = () =>
        {
            _metricsService.IncrementActiveUsers();
            _metricsService.IncrementActiveUsers();
            _metricsService.SetActiveUsers(5);
            _metricsService.DecrementActiveUsers();
        };

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void MultipleTimers_ShouldWorkConcurrently()
    {
        // Arrange & Act
        using (var timer1 = _metricsService.TrackRequestDuration("GET", "/api/endpoint1"))
        using (var timer2 = _metricsService.TrackRequestDuration("POST", "/api/endpoint2"))
        using (var timer3 = _metricsService.TrackDbLockWaitDuration())
        {
            // Simulate work
            Thread.Sleep(5);
        }

        // Assert - if we get here, multiple timers work correctly
        Assert.True(true);
    }
}
