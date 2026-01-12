using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Application.Services;

/// <summary>
/// v64: 操作模式检测服务
/// 根据触发条件识别电机当前的工况模式
/// </summary>
public sealed class OperationModeDetector
{
    private readonly IOperationModeRepository _modeRepo;
    private readonly ITelemetryRepository _telemetryRepo;
    private readonly ILogger<OperationModeDetector> _logger;

    // 模式状态缓存：instanceId -> (modeId, enterTime)
    private readonly Dictionary<string, ModeState> _modeStates = new();
    private readonly object _lock = new();

    public OperationModeDetector(
        IOperationModeRepository modeRepo,
        ITelemetryRepository telemetryRepo,
        ILogger<OperationModeDetector> logger)
    {
        _modeRepo = modeRepo;
        _telemetryRepo = telemetryRepo;
        _logger = logger;
    }

    /// <summary>
    /// 检测当前操作模式
    /// </summary>
    /// <param name="instanceId">电机实例ID</param>
    /// <param name="tagValues">当前标签值字典 (tagId -> value)</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>当前激活的操作模式，如果没有匹配则返回 null</returns>
    public async Task<OperationMode?> DetectCurrentModeAsync(
        string instanceId,
        Dictionary<string, double> tagValues,
        CancellationToken ct)
    {
        // 获取该实例的所有启用模式（按优先级排序）
        var modes = await _modeRepo.ListEnabledByInstanceAsync(instanceId, ct);
        if (modes.Count == 0)
            return null;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        lock (_lock)
        {
            // 检查每个模式的触发条件
            foreach (var mode in modes)
            {
                var isTriggered = CheckTriggerCondition(mode, tagValues);

                if (isTriggered)
                {
                    // 检查是否已经在此模式中
                    if (_modeStates.TryGetValue(instanceId, out var state) && state.ModeId == mode.ModeId)
                    {
                        // 检查是否满足最小持续时间
                        var duration = now - state.EnterTime;
                        if (duration >= mode.MinDurationMs)
                        {
                            // 检查是否超过最大持续时间
                            if (mode.MaxDurationMs > 0 && duration > mode.MaxDurationMs)
                            {
                                _logger.LogDebug(
                                    "Mode {ModeId} exceeded max duration for instance {InstanceId}",
                                    mode.ModeId, instanceId);
                                continue; // 尝试下一个模式
                            }

                            return mode;
                        }
                        else
                        {
                            // 还未满足最小持续时间，继续等待
                            return null;
                        }
                    }
                    else
                    {
                        // 进入新模式
                        _modeStates[instanceId] = new ModeState
                        {
                            ModeId = mode.ModeId,
                            EnterTime = now
                        };

                        _logger.LogDebug(
                            "Instance {InstanceId} entered mode {ModeId} ({ModeName})",
                            instanceId, mode.ModeId, mode.Name);

                        // 如果最小持续时间为0，立即返回
                        if (mode.MinDurationMs <= 0)
                            return mode;

                        return null; // 等待最小持续时间
                    }
                }
            }

            // 没有任何模式匹配，清除状态
            if (_modeStates.ContainsKey(instanceId))
            {
                _modeStates.Remove(instanceId);
                _logger.LogDebug("Instance {InstanceId} exited all modes", instanceId);
            }
        }

        return null;
    }

    /// <summary>
    /// 使用最新遥测数据检测模式
    /// </summary>
    public async Task<OperationMode?> DetectModeFromTelemetryAsync(
        string instanceId,
        string deviceId,
        IReadOnlyList<MotorParameterMapping> mappings,
        CancellationToken ct)
    {
        // 获取该实例的所有运行模式，收集所有需要的触发标签
        var modes = await _modeRepo.ListEnabledByInstanceAsync(instanceId, ct);
        var triggerTagIds = modes
            .Where(m => !string.IsNullOrEmpty(m.TriggerTagId))
            .Select(m => m.TriggerTagId!)
            .Distinct()
            .ToList();

        // 获取所有映射标签的ID
        var mappingTagIds = mappings.Select(m => m.TagId).ToList();

        // 合并所有需要获取的标签ID（去重）
        var allTagIds = mappingTagIds.Union(triggerTagIds).Distinct().ToList();

        // 获取所有标签的最新值
        var tagValues = new Dictionary<string, double>();
        foreach (var tagId in allTagIds)
        {
            var latestList = await _telemetryRepo.GetLatestAsync(deviceId, tagId, ct);
            var latest = latestList.FirstOrDefault();
            if (latest != null)
            {
                var value = GetNumericValue(latest);
                if (value.HasValue)
                {
                    tagValues[tagId] = value.Value;
                }
            }
        }

        // 调试日志：记录获取到的标签值
        if (triggerTagIds.Count > 0)
        {
            foreach (var triggerId in triggerTagIds)
            {
                var hasValue = tagValues.TryGetValue(triggerId, out var val);
                _logger.LogDebug(
                    "Instance {InstanceId} trigger tag {TagId}: HasValue={HasValue}, Value={Value}",
                    instanceId, triggerId, hasValue, hasValue ? val : 0);
            }
        }

        return await DetectCurrentModeAsync(instanceId, tagValues, ct);
    }

    /// <summary>
    /// 批量检测模式变化事件
    /// </summary>
    public async Task<List<ModeChangeEvent>> DetectModeChangesAsync(
        string instanceId,
        IReadOnlyList<TelemetryPoint> telemetryBatch,
        CancellationToken ct)
    {
        var modes = await _modeRepo.ListEnabledByInstanceAsync(instanceId, ct);
        if (modes.Count == 0)
            return new List<ModeChangeEvent>();

        var events = new List<ModeChangeEvent>();
        string? currentModeId = null;
        long? modeEnterTime = null;

        // 按时间排序
        var sortedData = telemetryBatch.OrderBy(t => t.Ts).ToList();

        foreach (var point in sortedData)
        {
            var value = GetNumericValue(point);
            if (!value.HasValue) continue;

            // 检查每个模式
            foreach (var mode in modes)
            {
                if (string.IsNullOrEmpty(mode.TriggerTagId) || mode.TriggerTagId != point.TagId)
                    continue;

                var isInRange = IsValueInRange(value.Value, mode.TriggerMinValue, mode.TriggerMaxValue);

                if (isInRange && currentModeId != mode.ModeId)
                {
                    // 模式变化
                    if (currentModeId != null && modeEnterTime.HasValue)
                    {
                        events.Add(new ModeChangeEvent
                        {
                            InstanceId = instanceId,
                            PreviousModeId = currentModeId,
                            NewModeId = mode.ModeId,
                            Timestamp = point.Ts,
                            Duration = point.Ts - modeEnterTime.Value
                        });
                    }

                    currentModeId = mode.ModeId;
                    modeEnterTime = point.Ts;
                    break;
                }
                else if (!isInRange && currentModeId == mode.ModeId)
                {
                    // 退出当前模式
                    events.Add(new ModeChangeEvent
                    {
                        InstanceId = instanceId,
                        PreviousModeId = currentModeId,
                        NewModeId = null,
                        Timestamp = point.Ts,
                        Duration = point.Ts - (modeEnterTime ?? point.Ts)
                    });

                    currentModeId = null;
                    modeEnterTime = null;
                }
            }
        }

        return events;
    }

    /// <summary>
    /// 获取实例当前的模式状态
    /// </summary>
    public ModeState? GetCurrentState(string instanceId)
    {
        lock (_lock)
        {
            return _modeStates.TryGetValue(instanceId, out var state) ? state : null;
        }
    }

    /// <summary>
    /// 清除实例的模式状态
    /// </summary>
    public void ClearState(string instanceId)
    {
        lock (_lock)
        {
            _modeStates.Remove(instanceId);
        }
    }

    #region Private Methods

    private bool CheckTriggerCondition(OperationMode mode, Dictionary<string, double> tagValues)
    {
        // 如果没有触发标签，默认总是触发（如"默认模式"）
        if (string.IsNullOrEmpty(mode.TriggerTagId))
            return true;

        // 检查触发标签的值是否在范围内
        if (!tagValues.TryGetValue(mode.TriggerTagId, out var value))
            return false;

        return IsValueInRange(value, mode.TriggerMinValue, mode.TriggerMaxValue);
    }

    private static bool IsValueInRange(double value, double? minValue, double? maxValue)
    {
        if (minValue.HasValue && value < minValue.Value)
            return false;
        if (maxValue.HasValue && value > maxValue.Value)
            return false;
        return true;
    }

    private static double? GetNumericValue(TelemetryPoint point)
    {
        return point.ValueType switch
        {
            TagValueType.Float32 => point.Float32Value,
            TagValueType.Float64 => point.Float64Value,
            TagValueType.Int32 => point.Int32Value,
            TagValueType.Int64 => point.Int64Value,
            TagValueType.Bool => point.BoolValue == true ? 1.0 : 0.0,
            _ => null
        };
    }

    #endregion
}

/// <summary>
/// 模式状态
/// </summary>
public sealed class ModeState
{
    public string ModeId { get; init; } = "";
    public long EnterTime { get; init; }
}

/// <summary>
/// 模式变化事件
/// </summary>
public sealed class ModeChangeEvent
{
    public string InstanceId { get; init; } = "";
    public string? PreviousModeId { get; init; }
    public string? NewModeId { get; init; }
    public long Timestamp { get; init; }
    public long Duration { get; init; }
}
