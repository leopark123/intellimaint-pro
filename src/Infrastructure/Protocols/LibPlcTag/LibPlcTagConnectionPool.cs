using System.Collections.Concurrent;
using IntelliMaint.Core.Contracts;
using libplctag;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Infrastructure.Protocols.LibPlcTag;

/// <summary>
/// Connection pool per PLC endpoint.
/// libplctag is sensitive to connection explosion; we clamp MaxConnections by PlcType.
/// Uses LRU eviction + idle timeout + exponential backoff rebuild.
/// </summary>
public sealed class LibPlcTagConnectionPool : IDisposable
{
    private readonly LibPlcTagOptions _options;
    private readonly ILogger<LibPlcTagConnectionPool> _logger;
    private readonly ConcurrentDictionary<string, PlcPool> _pools = new();
    private readonly Timer _reaper;

    public LibPlcTagConnectionPool(
        IOptions<LibPlcTagOptions> options,
        ILogger<LibPlcTagConnectionPool> logger)
    {
        _options = options.Value;
        _logger = logger;
        _reaper = new Timer(_ => ReapIdle(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    public PooledTagGroup AcquireTagGroup(PlcEndpointConfig plc, TagGroupConfig group)
    {
        var pool = _pools.GetOrAdd(plc.PlcId, _ => new PlcPool(plc, _logger));
        return pool.AcquireTagGroup(group);
    }

    public void MarkFaulted(string plcId, string reason)
    {
        if (_pools.TryGetValue(plcId, out var pool))
        {
            pool.MarkFaulted(reason);
        }
    }

    public void MarkDegraded(string plcId, string reason)
    {
        if (_pools.TryGetValue(plcId, out var pool))
        {
            pool.MarkDegraded(reason);
        }
    }

    public int GetActiveConnectionCount(string plcId)
    {
        return _pools.TryGetValue(plcId, out var pool) ? pool.ActiveCount : 0;
    }

    private void ReapIdle()
    {
        foreach (var kv in _pools)
        {
            try
            {
                kv.Value.ReapIdle();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reaping idle connections for {PlcId}", kv.Key);
            }
        }
    }

    public void Dispose()
    {
        _reaper.Dispose();
        foreach (var p in _pools.Values) p.Dispose();
        _pools.Clear();
    }

    /// <summary>
    /// Per-PLC connection pool
    /// </summary>
    private sealed class PlcPool : IDisposable
    {
        private readonly PlcEndpointConfig _cfg;
        private readonly ILogger _logger;
        private readonly object _gate = new();
        private readonly Dictionary<string, Tag> _tags = new();
        private readonly TimeSpan _idleTimeout = TimeSpan.FromMinutes(5);

        private int _backoffStep;
        private DateTimeOffset _nextAllowedCreateUtc = DateTimeOffset.MinValue;
        private ConnState _state = ConnState.Healthy;
        private string? _lastError;
        private DateTimeOffset _lastUsedUtc = DateTimeOffset.UtcNow;

        public int ActiveCount => _tags.Count;

        public PlcPool(PlcEndpointConfig cfg, ILogger logger)
        {
            _cfg = cfg;
            _logger = logger;
        }

        public PooledTagGroup AcquireTagGroup(TagGroupConfig group)
        {
            lock (_gate)
            {
                if (_state == ConnState.Faulted && DateTimeOffset.UtcNow < _nextAllowedCreateUtc)
                {
                    throw new LibPlcTagPoolFaultedException(_cfg.PlcId, _lastError ?? "Faulted");
                }

                var max = ClampMaxConnections(_cfg.PlcType, _cfg.MaxConnections);
                var tags = new List<Tag>();

                foreach (var tagCfg in group.Tags)
                {
                    var tagKey = $"{tagCfg.Name}|{tagCfg.CipType}";
                    
                    if (!_tags.TryGetValue(tagKey, out var tag))
                    {
                        if (_tags.Count >= max)
                        {
                            throw new LibPlcTagPoolBusyException(_cfg.PlcId, _tags.Count, max);
                        }

                        tag = CreateTag(tagCfg);
                        _tags[tagKey] = tag;
                    }

                    tags.Add(tag);
                }

                _lastUsedUtc = DateTimeOffset.UtcNow;
                
                if (_state == ConnState.Faulted)
                {
                    _state = ConnState.Healthy;
                    _backoffStep = 0;
                }

                return new PooledTagGroup(tags, group);
            }
        }

        public void MarkFaulted(string reason)
        {
            lock (_gate)
            {
                _state = ConnState.Faulted;
                _lastError = reason;
                _backoffStep = Math.Min(_backoffStep + 1, 6);
                _nextAllowedCreateUtc = DateTimeOffset.UtcNow.Add(BackoffDelay(_backoffStep));
                _logger.LogWarning("PLC {PlcId} marked faulted: {Reason}, backoff {Delay}s", 
                    _cfg.PlcId, reason, BackoffDelay(_backoffStep).TotalSeconds);
            }
        }

        public void MarkDegraded(string reason)
        {
            lock (_gate)
            {
                _state = ConnState.Degraded;
                _lastError = reason;
            }
        }

        public void ReapIdle()
        {
            lock (_gate)
            {
                if (_tags.Count == 0) return;
                
                var now = DateTimeOffset.UtcNow;
                if (now - _lastUsedUtc > _idleTimeout)
                {
                    _logger.LogInformation("Reaping {Count} idle tags for PLC {PlcId}", _tags.Count, _cfg.PlcId);
                    foreach (var tag in _tags.Values)
                    {
                        try { tag.Dispose(); } catch { }
                    }
                    _tags.Clear();
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                foreach (var tag in _tags.Values)
                {
                    try { tag.Dispose(); } catch { }
                }
                _tags.Clear();
            }
        }

        private Tag CreateTag(PlcTagConfig tagCfg)
        {
            var timeout = _cfg.TimeoutMs > 0 ? _cfg.TimeoutMs : 5000;
            var elemCount = tagCfg.ArrayLength > 0 ? tagCfg.ArrayLength : 1;
            
            var tag = new Tag()
            {
                Name = tagCfg.Name,
                Gateway = _cfg.IpAddress,
                Path = _cfg.Path ?? "1,0",
                PlcType = ParsePlcType(_cfg.PlcType),
                Protocol = Protocol.ab_eip,
                Timeout = TimeSpan.FromMilliseconds(timeout),
                ElementCount = elemCount
            };

            tag.Initialize();
            
            _logger.LogDebug("Created tag: {Name} on {Gateway}", tagCfg.Name, _cfg.IpAddress);
            return tag;
        }

        private static PlcType ParsePlcType(string plcType)
        {
            return (plcType?.ToUpperInvariant()) switch
            {
                "CONTROLLOGIX" => PlcType.ControlLogix,
                "COMPACTLOGIX" => PlcType.ControlLogix,
                "MICRO800" => PlcType.Micro800,
                "MICROLOGIX" => PlcType.MicroLogix,
                "PLC5" => PlcType.Plc5,
                "SLC500" => PlcType.Slc500,
                "LOGIXPCCC" => PlcType.LogixPccc,
                "OMRON" => PlcType.Omron,
                _ => PlcType.ControlLogix
            };
        }

        private static int ClampMaxConnections(string plcType, int configured)
        {
            var t = (plcType ?? string.Empty).Trim().ToUpperInvariant();
            var maxHard = t switch
            {
                "CONTROLLOGIX" => 8,
                "COMPACTLOGIX" => 4,
                "MICRO800" => 2,
                _ => 4
            };

            if (configured <= 0) configured = 1;
            return Math.Min(configured, maxHard);
        }

        private static TimeSpan BackoffDelay(int step) => step switch
        {
            0 => TimeSpan.Zero,
            1 => TimeSpan.FromSeconds(1),
            2 => TimeSpan.FromSeconds(2),
            3 => TimeSpan.FromSeconds(5),
            4 => TimeSpan.FromSeconds(10),
            5 => TimeSpan.FromSeconds(30),
            _ => TimeSpan.FromSeconds(60),
        };

        private enum ConnState { Healthy, Degraded, Faulted }
    }
}

/// <summary>
/// Pooled tag group for batch operations
/// </summary>
public sealed class PooledTagGroup
{
    public IReadOnlyList<Tag> Tags { get; }
    public TagGroupConfig Config { get; }

    public PooledTagGroup(List<Tag> tags, TagGroupConfig config)
    {
        Tags = tags;
        Config = config;
    }
}

/// <summary>
/// Pool busy exception
/// </summary>
public sealed class LibPlcTagPoolBusyException : Exception
{
    public string PlcId { get; }
    public int Active { get; }
    public int Max { get; }

    public LibPlcTagPoolBusyException(string plcId, int active, int max)
        : base($"TOO_MANY_CONN plc={plcId} active={active} max={max}")
    {
        PlcId = plcId;
        Active = active;
        Max = max;
    }
}

/// <summary>
/// Pool faulted exception
/// </summary>
public sealed class LibPlcTagPoolFaultedException : Exception
{
    public string PlcId { get; }

    public LibPlcTagPoolFaultedException(string plcId, string reason)
        : base($"PLC {plcId} is faulted: {reason}")
    {
        PlcId = plcId;
    }
}
