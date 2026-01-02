using System.Collections.Concurrent;
using IntelliMaint.Core.Contracts;
using Opc.Ua;
using Opc.Ua.Client;

namespace IntelliMaint.Infrastructure.Protocols.OpcUa;

/// <summary>
/// Manages Subscription/MonitoredItem for an endpoint.
/// If subscription creation fails: fall back to polling (collector decides).
/// NodeId strings must be stored as-is; we construct NodeId from string only for SDK.
/// </summary>
public sealed class OpcUaSubscriptionManager
{
    private readonly ConcurrentDictionary<string, EndpointSubscription> _subs = new();

    public async Task<EndpointSubscription> EnsureSubscriptionAsync(
        string endpointId,
        ISession session,
        OpcUaEndpointConfig cfg,
        Action<OpcUaDataNotification> onNotification,
        Action<string> onNodeDisabled,
        CancellationToken ct)
    {
        var sub = _subs.GetOrAdd(endpointId, _ => new EndpointSubscription(endpointId));
        await sub.EnsureAsync(session, cfg, onNotification, onNodeDisabled, ct).ConfigureAwait(false);
        return sub;
    }

    public int GetHealthyNodeCount(string endpointId)
        => _subs.TryGetValue(endpointId, out var s) ? s.HealthyCount : 0;

    public int GetTotalNodeCount(string endpointId)
        => _subs.TryGetValue(endpointId, out var s) ? s.TotalCount : 0;

    public sealed class EndpointSubscription
    {
        private readonly string _endpointId;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private Subscription? _subscription;

        private readonly ConcurrentDictionary<string, bool> _disabled = new(StringComparer.Ordinal);
        public int TotalCount { get; private set; }
        public int HealthyCount => Math.Max(0, TotalCount - _disabled.Count);

        public EndpointSubscription(string endpointId) => _endpointId = endpointId;

        public bool IsNodeDisabled(string tagId) => _disabled.ContainsKey(tagId);

        public async Task EnsureAsync(
            ISession session,
            OpcUaEndpointConfig cfg,
            Action<OpcUaDataNotification> onNotification,
            Action<string> onNodeDisabled,
            CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                TotalCount = cfg.Nodes?.Count ?? 0;

                if (_subscription is not null && _subscription.Session == session && _subscription.Created)
                    return;

                // Remove old subscription if session changed
                if (_subscription is not null)
                {
                    try
                    {
                        _subscription.Delete(true);
                        session.RemoveSubscription(_subscription);
                    }
                    catch { /* best-effort */ }
                    _subscription = null;
                }

                var sCfg = cfg.Subscription;
                var publishingInterval = Math.Clamp(sCfg.PublishingIntervalMs, 100, 500);

                var sub = new Subscription(session.DefaultSubscription)
                {
                    PublishingInterval = publishingInterval,
                    LifetimeCount = (uint)sCfg.LifetimeCount,
                    KeepAliveCount = (uint)sCfg.MaxKeepAliveCount,
                    MaxNotificationsPerPublish = (uint)sCfg.MaxNotificationsPerPublish,
                    Priority = sCfg.Priority,
                    PublishingEnabled = true
                };

                var items = new List<MonitoredItem>();
                foreach (var n in cfg.Nodes)
                {
                    if (string.IsNullOrWhiteSpace(n.TagId) || string.IsNullOrWhiteSpace(n.NodeId))
                        continue;

                    if (_disabled.ContainsKey(n.TagId))
                        continue;

                    // QueueSize: >= 2x sampling interval is a rule of thumb
                    var queueSize = (uint)Math.Max(n.QueueSize, 2);

                    var mi = new MonitoredItem(sub.DefaultItem)
                    {
                        StartNodeId = NodeId.Parse(n.NodeId),
                        DisplayName = n.TagId,
                        SamplingInterval = n.SamplingIntervalMs,
                        QueueSize = queueSize,
                        DiscardOldest = n.DiscardOldest,
                        MonitoringMode = MonitoringMode.Reporting
                    };

                    // Capture node config for closure
                    var nodeConfig = n;
                    mi.Notification += (monItem, args) =>
                    {
                        try
                        {
                            if (args.NotificationValue is MonitoredItemNotification notification)
                            {
                                onNotification(new OpcUaDataNotification(
                                    EndpointId: _endpointId,
                                    TagId: monItem.DisplayName,
                                    NodeIdString: nodeConfig.NodeId,
                                    Value: notification.Value));
                            }
                        }
                        catch
                        {
                            // Ignore; health is handled upstream
                        }
                    };

                    items.Add(mi);
                }

                sub.AddItems(items);

                session.AddSubscription(sub);
                sub.Create();

                // Apply after create to ensure server accepts
                sub.ApplyChanges();

                _subscription = sub;
            }
            catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes.BadNodeIdUnknown || sre.StatusCode == StatusCodes.BadNodeIdInvalid)
            {
                // Disable all nodes as fallback
                foreach (var n in cfg.Nodes)
                {
                    _disabled.TryAdd(n.TagId, true);
                    onNodeDisabled(n.TagId);
                }
                throw;
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task DisableNodeAsync(string tagId, Action<string> onNodeDisabled, CancellationToken ct)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                _disabled.TryAdd(tagId, true);
                onNodeDisabled(tagId);

                if (_subscription is null) return;

                var item = _subscription.MonitoredItems.FirstOrDefault(i => string.Equals(i.DisplayName, tagId, StringComparison.Ordinal));
                if (item is not null)
                {
                    try
                    {
                        _subscription.RemoveItem(item);
                        _subscription.ApplyChanges();
                    }
                    catch { /* best-effort */ }
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}

/// <summary>
/// Internal notification record (renamed to avoid SDK collision)
/// </summary>
public sealed record OpcUaDataNotification(
    string EndpointId,
    string TagId,
    string NodeIdString,
    DataValue Value);
