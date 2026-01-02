using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Infrastructure.Protocols.LibPlcTag;

/// <summary>
/// Tracks per-PLC and overall libplctag collector health.
/// Thread-safe health state management.
/// </summary>
public sealed class LibPlcTagHealthChecker
{
    private readonly object _gate = new();
    
    private CollectorState _state = CollectorState.Disconnected;
    private DateTimeOffset _lastSuccessTime = DateTimeOffset.MinValue;
    private int _consecutiveErrors;
    private int _typeMismatchCount;
    private int _badTagCount;
    private int _noRouteCount;
    private int _timeoutCount;
    private int _tooManyConnCount;
    private string? _lastError;
    
    // Latency tracking
    private readonly List<double> _latencySamples = new(100);
    private double _avgLatencyMs;
    private double _p95LatencyMs;
    
    // Connection tracking
    private int _activeConnections;
    private int _totalTagCount;
    private int _healthyTagCount;

    public void MarkReadOk(double latencyMs = 0)
    {
        lock (_gate)
        {
            _lastSuccessTime = DateTimeOffset.UtcNow;
            _consecutiveErrors = 0;
            _lastError = null;
            
            if (_state == CollectorState.Disconnected)
                _state = CollectorState.Connected;
            
            // Update latency stats
            if (latencyMs > 0)
            {
                if (_latencySamples.Count >= 100)
                    _latencySamples.RemoveAt(0);
                _latencySamples.Add(latencyMs);
                UpdateLatencyStats();
            }
        }
    }

    public void MarkReadFail(LibPlcTagError err, string? errorMessage = null)
    {
        lock (_gate)
        {
            _consecutiveErrors++;
            _lastError = errorMessage ?? err.ToString();

            switch (err)
            {
                case LibPlcTagError.TIMEOUT:
                    _timeoutCount++;
                    _state = CollectorState.Degraded;
                    break;
                    
                case LibPlcTagError.TYPE_MISMATCH:
                    _typeMismatchCount++;
                    _state = CollectorState.Degraded;
                    break;
                    
                case LibPlcTagError.BAD_TAG:
                    _badTagCount++;
                    _state = CollectorState.Degraded;
                    break;
                    
                case LibPlcTagError.NO_ROUTE:
                    _noRouteCount++;
                    _state = CollectorState.Disconnected;
                    break;
                    
                case LibPlcTagError.TOO_MANY_CONN:
                    _tooManyConnCount++;
                    _state = CollectorState.Degraded;
                    break;
                    
                default:
                    _state = CollectorState.Degraded;
                    break;
            }
        }
    }

    public void UpdateConnectionStats(int activeConnections, int totalTags, int healthyTags)
    {
        lock (_gate)
        {
            _activeConnections = activeConnections;
            _totalTagCount = totalTags;
            _healthyTagCount = healthyTags;
        }
    }

    public CollectorHealth GetHealth()
    {
        lock (_gate)
        {
            return new CollectorHealth
            {
                Protocol = "libplctag",
                State = _state,
                LastSuccessTime = _lastSuccessTime,
                ConsecutiveErrors = _consecutiveErrors,
                TypeMismatchCount = _typeMismatchCount,
                AvgLatencyMs = _avgLatencyMs,
                P95LatencyMs = _p95LatencyMs,
                LastError = _lastError,
                ActiveConnections = _activeConnections,
                TotalTagCount = _totalTagCount,
                HealthyTagCount = _healthyTagCount
            };
        }
    }

    private void UpdateLatencyStats()
    {
        if (_latencySamples.Count == 0)
        {
            _avgLatencyMs = 0;
            _p95LatencyMs = 0;
            return;
        }

        _avgLatencyMs = _latencySamples.Average();
        
        var sorted = _latencySamples.OrderBy(x => x).ToList();
        var p95Index = (int)(sorted.Count * 0.95);
        _p95LatencyMs = sorted[Math.Min(p95Index, sorted.Count - 1)];
    }

    public void Reset()
    {
        lock (_gate)
        {
            _state = CollectorState.Disconnected;
            _lastSuccessTime = DateTimeOffset.MinValue;
            _consecutiveErrors = 0;
            _typeMismatchCount = 0;
            _badTagCount = 0;
            _noRouteCount = 0;
            _timeoutCount = 0;
            _tooManyConnCount = 0;
            _lastError = null;
            _latencySamples.Clear();
            _avgLatencyMs = 0;
            _p95LatencyMs = 0;
        }
    }
}

/// <summary>
/// libplctag error classification
/// </summary>
public enum LibPlcTagError
{
    OK = 0,
    TIMEOUT = 1,
    NO_ROUTE = 2,
    BAD_TAG = 3,
    TYPE_MISMATCH = 4,
    TOO_MANY_CONN = 5,
    UNKNOWN = 999
}
