using Prometheus;

namespace liteapi.Services;

/// <summary>
/// Custom metrics service for tracking application-specific metrics with Prometheus
/// </summary>
public class MetricsService
{
    // Counters - Track total count of events
    private static readonly Counter RequestsTotal = Metrics.CreateCounter(
        "mini_server_requests_total",
        "Total number of HTTP requests",
        new CounterConfiguration
        {
            LabelNames = new[] { "method", "endpoint", "status_code" }
        });

    private static readonly Counter DbLockAcquisitionsTotal = Metrics.CreateCounter(
        "mini_server_db_lock_acquisitions_total",
        "Total number of database lock acquisitions",
        new CounterConfiguration
        {
            LabelNames = new[] { "result" } // "success" or "failed"
        });

    private static readonly Counter PacketProcessingTotal = Metrics.CreateCounter(
        "mini_server_packet_processing_total",
        "Total number of packets processed",
        new CounterConfiguration
        {
            LabelNames = new[] { "format", "endpoint" } // format: "json" or "msgpack"
        });

    // Gauges - Track current value
    private static readonly Gauge ActiveDbLocks = Metrics.CreateGauge(
        "mini_server_active_db_locks",
        "Number of currently active database locks");

    private static readonly Gauge ActiveUsers = Metrics.CreateGauge(
        "mini_server_active_users",
        "Number of currently active users");

    // Histograms - Track distribution of values (e.g., request duration)
    private static readonly Histogram RequestDuration = Metrics.CreateHistogram(
        "mini_server_request_duration_seconds",
        "HTTP request duration in seconds",
        new HistogramConfiguration
        {
            LabelNames = new[] { "method", "endpoint" },
            Buckets = Histogram.ExponentialBuckets(0.001, 2, 10) // 1ms to ~1s
        });

    private static readonly Histogram DbLockWaitDuration = Metrics.CreateHistogram(
        "mini_server_db_lock_wait_duration_seconds",
        "Time spent waiting for database locks in seconds",
        new HistogramConfiguration
        {
            Buckets = Histogram.ExponentialBuckets(0.01, 2, 10) // 10ms to ~10s
        });

    // Request tracking methods
    public void IncrementRequest(string method, string endpoint, int statusCode)
    {
        RequestsTotal.WithLabels(method, endpoint, statusCode.ToString()).Inc();
    }

    public IDisposable TrackRequestDuration(string method, string endpoint)
    {
        return RequestDuration.WithLabels(method, endpoint).NewTimer();
    }

    // DB Lock tracking methods
    public void IncrementDbLockAcquisition(bool success)
    {
        DbLockAcquisitionsTotal.WithLabels(success ? "success" : "failed").Inc();
    }

    public void IncrementActiveDbLocks()
    {
        ActiveDbLocks.Inc();
    }

    public void DecrementActiveDbLocks()
    {
        ActiveDbLocks.Dec();
    }

    public IDisposable TrackDbLockWaitDuration()
    {
        return DbLockWaitDuration.NewTimer();
    }

    // Packet processing tracking methods
    public void IncrementPacketProcessing(string format, string endpoint)
    {
        PacketProcessingTotal.WithLabels(format, endpoint).Inc();
    }

    // User tracking methods
    public void SetActiveUsers(int count)
    {
        ActiveUsers.Set(count);
    }

    public void IncrementActiveUsers()
    {
        ActiveUsers.Inc();
    }

    public void DecrementActiveUsers()
    {
        ActiveUsers.Dec();
    }
}
