using System.Collections.Concurrent;
using System.Text.Json;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Serilog;

namespace IntelliMaint.Host.Api.Services;

/// <summary>
/// 采集规则引擎 - 后台服务
/// 定期轮询遥测数据，评估采集规则条件，管理采集状态
/// </summary>
public sealed class CollectionRuleEngine : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(500);

    // 规则状态管理
    private readonly ConcurrentDictionary<string, RuleState> _ruleStates = new();
    
    // 当前遥测值缓存 (deviceId|tagId -> value)
    private readonly ConcurrentDictionary<string, TagValue> _currentValues = new();
    
    // 配置版本（用于检测配置变更）
    private long _lastConfigRevision = -1;

    public CollectionRuleEngine(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Information("CollectionRuleEngine started");

        // 初始加载规则
        await LoadRulesAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CollectionRuleEngine tick failed");
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        // 关闭所有进行中的采集
        await FinalizeAllSegmentsAsync(CancellationToken.None);

        Log.Information("CollectionRuleEngine stopped");
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var telemetryRepo = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
        var ruleRepo = scope.ServiceProvider.GetRequiredService<ICollectionRuleRepository>();
        var segmentRepo = scope.ServiceProvider.GetRequiredService<ICollectionSegmentRepository>();
        var configRevision = scope.ServiceProvider.GetRequiredService<IConfigRevisionProvider>();

        // 检查配置是否变更
        var currentRevision = await configRevision.GetRevisionAsync(ct);
        if (currentRevision != _lastConfigRevision)
        {
            await LoadRulesAsync(ct);
            _lastConfigRevision = currentRevision;
        }

        // 获取最新遥测数据
        var latestPoints = await telemetryRepo.GetLatestAsync(null, null, ct);
        UpdateCurrentValues(latestPoints);

        // 评估每个规则
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (var state in _ruleStates.Values)
        {
            if (!state.Enabled)
                continue;

            await EvaluateRuleAsync(state, now, segmentRepo, ruleRepo, ct);
        }
    }

    private void UpdateCurrentValues(IReadOnlyList<TelemetryPoint> points)
    {
        foreach (var p in points)
        {
            var key = MakeKey(p.DeviceId, p.TagId);
            var value = ExtractNumericValue(p);

            _currentValues[key] = new TagValue
            {
                DeviceId = p.DeviceId,
                TagId = p.TagId,
                Value = value,
                Timestamp = p.Ts
            };
        }
    }

    private async Task EvaluateRuleAsync(
        RuleState state,
        long now,
        ICollectionSegmentRepository segmentRepo,
        ICollectionRuleRepository ruleRepo,
        CancellationToken ct)
    {
        var startConditionMet = EvaluateCondition(state.StartCondition, state.DeviceId);
        var stopConditionMet = EvaluateCondition(state.StopCondition, state.DeviceId);

        switch (state.Phase)
        {
            case CollectionPhase.Idle:
                if (startConditionMet)
                {
                    // 开始采集
                    await StartCollectionAsync(state, now, segmentRepo, ruleRepo, ct);
                }
                break;

            case CollectionPhase.Collecting:
                if (stopConditionMet)
                {
                    // 检查停止条件持续时间
                    if (state.StopConditionStartTime == null)
                    {
                        state.StopConditionStartTime = now;
                    }

                    var durationCondition = GetDurationCondition(state.StopCondition);
                    var requiredDurationMs = (durationCondition?.Seconds ?? 0) * 1000;

                    if (now - state.StopConditionStartTime.Value >= requiredDurationMs)
                    {
                        // 进入后置缓冲
                        state.Phase = CollectionPhase.PostBuffer;
                        state.PostBufferStartTime = now;
                        Log.Debug("Rule {RuleId} entering post-buffer phase", state.RuleId);
                    }
                }
                else
                {
                    // 停止条件不满足，重置计时器
                    state.StopConditionStartTime = null;
                }
                break;

            case CollectionPhase.PostBuffer:
                var postBufferMs = state.CollectionConfig.PostBufferSeconds * 1000;
                if (now - state.PostBufferStartTime!.Value >= postBufferMs)
                {
                    // 完成采集
                    await CompleteCollectionAsync(state, now, segmentRepo, ct);
                }
                break;
        }
    }

    private bool EvaluateCondition(ConditionConfig config, string deviceId)
    {
        // 空条件列表应该返回 false（不触发）
        if (config.Conditions == null || config.Conditions.Count == 0)
        {
            Log.Debug("Empty conditions list, returning false");
            return false;
        }

        var results = new List<bool>();

        foreach (var cond in config.Conditions)
        {
            bool result;

            if (cond.Type.Equals("tag", StringComparison.OrdinalIgnoreCase))
            {
                var key = MakeKey(deviceId, cond.TagId!);
                if (!_currentValues.TryGetValue(key, out var tagValue))
                {
                    result = false; // 没有数据，条件不满足
                }
                else
                {
                    result = EvaluateTagCondition(tagValue.Value, cond.Operator!, cond.Value!.Value);
                }
            }
            else if (cond.Type.Equals("duration", StringComparison.OrdinalIgnoreCase))
            {
                // Duration 条件由外层逻辑处理，这里返回 true
                result = true;
            }
            else
            {
                result = false;
            }

            results.Add(result);
        }

        return config.Logic.Equals("AND", StringComparison.OrdinalIgnoreCase)
            ? results.TrueForAll(r => r)
            : results.Exists(r => r);
    }

    private static bool EvaluateTagCondition(double value, string op, double threshold)
    {
        return op.ToLowerInvariant() switch
        {
            "gt" => value > threshold,
            "gte" => value >= threshold,
            "lt" => value < threshold,
            "lte" => value <= threshold,
            "eq" => Math.Abs(value - threshold) < 0.0001,
            "ne" => Math.Abs(value - threshold) >= 0.0001,
            _ => false
        };
    }

    private static ConditionItem? GetDurationCondition(ConditionConfig config)
    {
        return config.Conditions.FirstOrDefault(c =>
            c.Type.Equals("duration", StringComparison.OrdinalIgnoreCase));
    }

    private async Task StartCollectionAsync(
        RuleState state,
        long now,
        ICollectionSegmentRepository segmentRepo,
        ICollectionRuleRepository ruleRepo,
        CancellationToken ct)
    {
        // 考虑前置缓冲
        var preBufferMs = state.CollectionConfig.PreBufferSeconds * 1000;
        var startTime = now - preBufferMs;

        var segment = new CollectionSegment
        {
            RuleId = state.RuleId,
            DeviceId = state.DeviceId,
            StartTimeUtc = startTime,
            EndTimeUtc = null,
            Status = SegmentStatus.Collecting,
            DataPointCount = 0,
            MetadataJson = null,
            CreatedUtc = now
        };

        var segmentId = await segmentRepo.CreateAsync(segment, ct);
        state.CurrentSegmentId = segmentId;
        state.Phase = CollectionPhase.Collecting;
        state.CollectionStartTime = now;
        state.StopConditionStartTime = null;

        // 更新触发计数
        await ruleRepo.IncrementTriggerCountAsync(state.RuleId, ct);

        Log.Information("Rule {RuleId} started collection, segment {SegmentId}", state.RuleId, segmentId);
    }

    private async Task CompleteCollectionAsync(
        RuleState state,
        long now,
        ICollectionSegmentRepository segmentRepo,
        CancellationToken ct)
    {
        if (state.CurrentSegmentId == null)
            return;

        await segmentRepo.SetEndTimeAsync(state.CurrentSegmentId.Value, now, ct);
        await segmentRepo.UpdateStatusAsync(state.CurrentSegmentId.Value, SegmentStatus.Completed, 0, ct);

        Log.Information("Rule {RuleId} completed collection, segment {SegmentId}", 
            state.RuleId, state.CurrentSegmentId.Value);

        // 重置状态
        state.Phase = CollectionPhase.Idle;
        state.CurrentSegmentId = null;
        state.CollectionStartTime = null;
        state.StopConditionStartTime = null;
        state.PostBufferStartTime = null;
    }

    // JSON 序列化选项（支持 PascalCase 和 camelCase）
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true  // 忽略大小写
    };

    private async Task LoadRulesAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ruleRepo = scope.ServiceProvider.GetRequiredService<ICollectionRuleRepository>();

            var rules = await ruleRepo.ListAsync(ct);

            // 更新或添加规则状态
            var currentRuleIds = new HashSet<string>();

            foreach (var rule in rules)
            {
                currentRuleIds.Add(rule.RuleId);

                ConditionConfig? startCondition = null;
                ConditionConfig? stopCondition = null;
                CollectionConfig? collectionConfig = null;

                try
                {
                    startCondition = JsonSerializer.Deserialize<ConditionConfig>(rule.StartConditionJson, JsonOptions);
                    stopCondition = JsonSerializer.Deserialize<ConditionConfig>(rule.StopConditionJson, JsonOptions);
                    collectionConfig = JsonSerializer.Deserialize<CollectionConfig>(rule.CollectionConfigJson, JsonOptions);
                }
                catch (JsonException ex)
                {
                    Log.Warning(ex, "Failed to parse JSON for rule {RuleId}, using defaults", rule.RuleId);
                }

                // 确保不为 null，创建新对象以确保 Conditions/TagIds 不为 null
                startCondition = new ConditionConfig 
                { 
                    Logic = startCondition?.Logic ?? "AND", 
                    Conditions = startCondition?.Conditions ?? new List<ConditionItem>() 
                };
                stopCondition = new ConditionConfig 
                { 
                    Logic = stopCondition?.Logic ?? "AND", 
                    Conditions = stopCondition?.Conditions ?? new List<ConditionItem>() 
                };
                collectionConfig = new CollectionConfig 
                { 
                    TagIds = collectionConfig?.TagIds ?? new List<string>(), 
                    PreBufferSeconds = collectionConfig?.PreBufferSeconds ?? 0, 
                    PostBufferSeconds = collectionConfig?.PostBufferSeconds ?? 0 
                };

                if (_ruleStates.TryGetValue(rule.RuleId, out var existingState))
                {
                    // 更新现有状态
                    existingState.Enabled = rule.Enabled;
                    existingState.DeviceId = rule.DeviceId;
                    existingState.StartCondition = startCondition;
                    existingState.StopCondition = stopCondition;
                    existingState.CollectionConfig = collectionConfig;
                }
                else
                {
                    // 添加新状态
                    _ruleStates[rule.RuleId] = new RuleState
                    {
                        RuleId = rule.RuleId,
                        DeviceId = rule.DeviceId,
                        Enabled = rule.Enabled,
                        StartCondition = startCondition,
                        StopCondition = stopCondition,
                        CollectionConfig = collectionConfig,
                        Phase = CollectionPhase.Idle
                    };
                }
            }

            // 移除已删除的规则
            var toRemove = _ruleStates.Keys.Where(k => !currentRuleIds.Contains(k)).ToList();
            foreach (var ruleId in toRemove)
            {
                _ruleStates.TryRemove(ruleId, out _);
            }

            Log.Information("CollectionRuleEngine loaded {Count} rules", rules.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load collection rules");
        }
    }

    private async Task FinalizeAllSegmentsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var segmentRepo = scope.ServiceProvider.GetRequiredService<ICollectionSegmentRepository>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (var state in _ruleStates.Values)
        {
            if (state.CurrentSegmentId != null)
            {
                try
                {
                    await segmentRepo.SetEndTimeAsync(state.CurrentSegmentId.Value, now, ct);
                    await segmentRepo.UpdateStatusAsync(state.CurrentSegmentId.Value, SegmentStatus.Completed, 0, ct);
                    Log.Information("Finalized segment {SegmentId} for rule {RuleId}", 
                        state.CurrentSegmentId.Value, state.RuleId);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to finalize segment {SegmentId}", state.CurrentSegmentId.Value);
                }
            }
        }
    }

    private static string MakeKey(string deviceId, string tagId) => $"{deviceId}|{tagId}";

    private static double ExtractNumericValue(TelemetryPoint p)
    {
        return p.ValueType switch
        {
            TagValueType.Bool => p.BoolValue == true ? 1.0 : 0.0,
            TagValueType.Int8 => p.Int8Value ?? 0,
            TagValueType.UInt8 => p.UInt8Value ?? 0,
            TagValueType.Int16 => p.Int16Value ?? 0,
            TagValueType.UInt16 => p.UInt16Value ?? 0,
            TagValueType.Int32 => p.Int32Value ?? 0,
            TagValueType.UInt32 => p.UInt32Value ?? 0,
            TagValueType.Int64 => p.Int64Value ?? 0,
            TagValueType.UInt64 => p.UInt64Value ?? 0,
            TagValueType.Float32 => p.Float32Value ?? 0,
            TagValueType.Float64 => p.Float64Value ?? 0,
            _ => 0
        };
    }

    #region 内部类型

    private sealed class RuleState
    {
        public required string RuleId { get; init; }
        public required string DeviceId { get; set; }
        public bool Enabled { get; set; }
        public required ConditionConfig StartCondition { get; set; }
        public required ConditionConfig StopCondition { get; set; }
        public required CollectionConfig CollectionConfig { get; set; }

        public CollectionPhase Phase { get; set; } = CollectionPhase.Idle;
        public long? CurrentSegmentId { get; set; }
        public long? CollectionStartTime { get; set; }
        public long? StopConditionStartTime { get; set; }
        public long? PostBufferStartTime { get; set; }
    }

    private enum CollectionPhase
    {
        Idle,
        Collecting,
        PostBuffer
    }

    private sealed class TagValue
    {
        public required string DeviceId { get; init; }
        public required string TagId { get; init; }
        public double Value { get; init; }
        public long Timestamp { get; init; }
    }

    #endregion
}
