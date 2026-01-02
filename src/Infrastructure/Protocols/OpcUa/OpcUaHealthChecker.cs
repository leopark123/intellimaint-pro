using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Infrastructure.Protocols.OpcUa;

/// <summary>
/// OPC UA health tracker with latency stats.
/// </summary>
public sealed class OpcUaHealthChecker
{
    private readonly object _gate = new();

    private CollectorState _state = CollectorState.Disconnected;
    private DateTimeOffset _lastSuccess = DateTimeOffset.MinValue;
    private int _consecutiveErrors = 0;
    private int _typeMismatch = 0;
    private string? _lastError;
    private int _activeConnections = 0;
    private int _totalTags = 0;
    private int _healthyTags = 0;

    // Latency stats: rolling window
    private readonly Queue<double> _latency = new();
    private const int Window = 200;

    public void SetInventory(int totalTags) { lock (_gate) _totalTags = totalTags; }
    public void SetHealthyTags(int healthy) { lock (_gate) _healthyTags = healthy; }

    public void MarkConnected(int activeConnections)
    {
        lock (_gate)
        {
            _activeConnections = activeConnections;
            if (_state == CollectorState.Disconnected)
                _state = CollectorState.Connected;
        }
    }

    public void MarkSuccess(double latencyMs)
    {
        lock (_gate)
        {
            _lastSuccess = DateTimeOffset.UtcNow;
            _consecutiveErrors = 0;
            _lastError = null;
            if (_state == CollectorState.Disconnected) _state = CollectorState.Connected;

            _latency.Enqueue(latencyMs);
            while (_latency.Count > Window) _latency.Dequeue();
        }
    }

    public void MarkDegraded(string reason)
    {
        lock (_gate)
        {
            _state = CollectorState.Degraded;
            _consecutiveErrors++;
            _lastError = reason;
        }
    }

    public void MarkDisconnected(string reason)
    {
        lock (_gate)
        {
            _state = CollectorState.Disconnected;
            _consecutiveErrors++;
            _lastError = reason;
        }
    }

    public void MarkTypeMismatch(string reason)
    {
        lock (_gate)
        {
            _typeMismatch++;
            _state = CollectorState.Degraded;
            _consecutiveErrors++;
            _lastError = reason;
        }
    }

    public CollectorHealth Snapshot()
    {
        lock (_gate)
        {
            var arr = _latency.ToArray();
            Array.Sort(arr);
            double avg = arr.Length == 0 ? 0 : arr.Average();
            double p95 = arr.Length == 0 ? 0 : arr[(int)Math.Floor(0.95 * (arr.Length - 1))];

            return new CollectorHealth
            {
                Protocol = "opcua",
                State = _state,
                LastSuccessTime = _lastSuccess,
                ConsecutiveErrors = _consecutiveErrors,
                TypeMismatchCount = _typeMismatch,
                AvgLatencyMs = avg,
                P95LatencyMs = p95,
                LastError = _lastError,
                ActiveConnections = _activeConnections,
                TotalTagCount = _totalTags,
                HealthyTagCount = _healthyTags
            };
        }
    }
}
