using System.Text.Json;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Application.Services;

/// <summary>
/// v64: 电机基线学习服务
/// 收集数据并计算统计特征，建立各操作模式的正常基线
/// </summary>
public sealed class MotorBaselineLearningService
{
    private readonly IMotorInstanceRepository _instanceRepo;
    private readonly IMotorModelRepository _modelRepo;
    private readonly IMotorParameterMappingRepository _mappingRepo;
    private readonly IOperationModeRepository _modeRepo;
    private readonly IBaselineProfileRepository _baselineRepo;
    private readonly ITelemetryRepository _telemetryRepo;
    private readonly MotorFftAnalyzer _fftAnalyzer;
    private readonly ILogger<MotorBaselineLearningService> _logger;

    // 学习任务状态
    private readonly Dictionary<string, MotorLearningTaskState> _taskStates = new();
    private readonly object _lock = new();

    public MotorBaselineLearningService(
        IMotorInstanceRepository instanceRepo,
        IMotorModelRepository modelRepo,
        IMotorParameterMappingRepository mappingRepo,
        IOperationModeRepository modeRepo,
        IBaselineProfileRepository baselineRepo,
        ITelemetryRepository telemetryRepo,
        MotorFftAnalyzer fftAnalyzer,
        ILogger<MotorBaselineLearningService> logger)
    {
        _instanceRepo = instanceRepo;
        _modelRepo = modelRepo;
        _mappingRepo = mappingRepo;
        _modeRepo = modeRepo;
        _baselineRepo = baselineRepo;
        _telemetryRepo = telemetryRepo;
        _fftAnalyzer = fftAnalyzer;
        _logger = logger;
    }

    /// <summary>
    /// 启动基线学习任务
    /// </summary>
    public async Task<string> StartLearningAsync(MotorBaselineLearningRequest request, CancellationToken ct)
    {
        // 清理过期任务
        CleanupExpiredTasks();

        var instance = await _instanceRepo.GetAsync(request.InstanceId, ct)
            ?? throw new InvalidOperationException($"Motor instance not found: {request.InstanceId}");

        var mode = await _modeRepo.GetAsync(request.ModeId, ct)
            ?? throw new InvalidOperationException($"Operation mode not found: {request.ModeId}");

        var taskId = Guid.NewGuid().ToString("N")[..12];

        var state = new MotorLearningTaskState
        {
            TaskId = taskId,
            InstanceId = request.InstanceId,
            ModeId = request.ModeId,
            Status = MotorLearningStatus.Running,
            StartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            DataStartTs = request.StartTs ?? DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeMilliseconds(),
            DataEndTs = request.EndTs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MinSamples = request.MinSamples ?? 1000
        };

        lock (_lock)
        {
            _taskStates[taskId] = state;
        }

        _logger.LogInformation(
            "Starting motor baseline learning task {TaskId} for instance {InstanceId} mode {ModeId}",
            taskId, request.InstanceId, request.ModeId);

        // 异步执行学习（不阻塞）- 使用 CancellationToken.None 避免 HTTP 请求结束后取消后台任务
        _ = Task.Run(async () => await ExecuteLearningAsync(state, CancellationToken.None));

        return taskId;
    }

    /// <summary>
    /// 执行基线学习
    /// </summary>
    private async Task ExecuteLearningAsync(MotorLearningTaskState state, CancellationToken ct)
    {
        try
        {
            var instance = await _instanceRepo.GetAsync(state.InstanceId, ct);
            if (instance == null)
            {
                UpdateTaskState(state.TaskId, MotorLearningStatus.Failed, "Instance not found");
                return;
            }

            var model = await _modelRepo.GetAsync(instance.ModelId, ct);
            var mappings = await _mappingRepo.ListByInstanceAsync(state.InstanceId, ct);

            if (mappings.Count == 0)
            {
                UpdateTaskState(state.TaskId, MotorLearningStatus.Failed, "No parameter mappings configured");
                return;
            }

            // 为每个参数收集数据并计算基线
            var baselines = new List<BaselineProfile>();
            var diagnosisMappings = mappings.Where(m => m.UsedForDiagnosis).ToList();

            for (int i = 0; i < diagnosisMappings.Count; i++)
            {
                var mapping = diagnosisMappings[i];
                UpdateTaskProgress(state.TaskId, $"Processing {i + 1}/{diagnosisMappings.Count}: {mapping.Parameter}");

                var baseline = await LearnParameterBaselineAsync(
                    instance, model, mapping, state.ModeId,
                    state.DataStartTs, state.DataEndTs, state.MinSamples, ct);

                if (baseline != null)
                {
                    baselines.Add(baseline);
                }
            }

            if (baselines.Count == 0)
            {
                UpdateTaskState(state.TaskId, MotorLearningStatus.Failed, "No valid baselines could be created");
                return;
            }

            // 保存基线
            await _baselineRepo.SaveBatchAsync(baselines, ct);

            UpdateTaskState(state.TaskId, MotorLearningStatus.Completed,
                $"Created {baselines.Count} baseline profiles");

            _logger.LogInformation(
                "Motor baseline learning completed for task {TaskId}: {Count} profiles created",
                state.TaskId, baselines.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Motor baseline learning failed for task {TaskId}", state.TaskId);
            UpdateTaskState(state.TaskId, MotorLearningStatus.Failed, ex.Message);
        }
    }

    /// <summary>
    /// 为单个参数学习基线
    /// </summary>
    private async Task<BaselineProfile?> LearnParameterBaselineAsync(
        MotorInstance instance,
        MotorModel? model,
        MotorParameterMapping mapping,
        string modeId,
        long startTs,
        long endTs,
        int minSamples,
        CancellationToken ct)
    {
        // 查询历史数据
        var query = new HistoryQuery
        {
            DeviceId = instance.DeviceId,
            TagId = mapping.TagId,
            StartTs = startTs,
            EndTs = endTs,
            Limit = 100000 // 最多 10 万点
        };

        var result = await _telemetryRepo.QueryAsync(query, ct);

        if (result.Items.Count < minSamples)
        {
            _logger.LogWarning(
                "Insufficient samples for parameter {Parameter}: {Count} < {MinSamples}",
                mapping.Parameter, result.Items.Count, minSamples);
            return null;
        }

        // 提取数值并应用缩放
        var values = result.Items
            .Select(p => GetNumericValue(p))
            .Where(v => v.HasValue)
            .Select(v => v!.Value * mapping.ScaleFactor + mapping.Offset)
            .ToList();

        if (values.Count < minSamples)
        {
            _logger.LogWarning("Insufficient valid values for parameter {Parameter}", mapping.Parameter);
            return null;
        }

        // 计算统计特征
        var stats = CalculateStatistics(values);

        // 计算置信度（基于样本量和标准差）
        var confidenceLevel = CalculateConfidenceLevel(values.Count, stats.StdDev, stats.Mean);

        // 如果是电流参数，进行 FFT 分析
        string? frequencyProfileJson = null;
        if (IsCurrentParameter(mapping.Parameter) && model != null)
        {
            var fftParams = MotorFftParams.FromModel(model, model.RatedSpeed ?? 1500);
            // 估算采样率（基于数据时间跨度）
            var timeSpanMs = endTs - startTs;
            var estimatedSampleRate = values.Count * 1000.0 / timeSpanMs;

            var fftResult = _fftAnalyzer.Analyze(values.ToArray(), Math.Max(100, estimatedSampleRate), fftParams);
            var freqProfile = _fftAnalyzer.CreateFrequencyProfile(fftResult);
            frequencyProfileJson = JsonSerializer.Serialize(freqProfile);
        }

        return new BaselineProfile
        {
            BaselineId = Guid.NewGuid().ToString("N")[..12],
            ModeId = modeId,
            Parameter = mapping.Parameter,
            Mean = stats.Mean,
            StdDev = stats.StdDev,
            MinValue = stats.Min,
            MaxValue = stats.Max,
            Percentile05 = stats.Percentile05,
            Percentile95 = stats.Percentile95,
            Median = stats.Median,
            FrequencyProfileJson = frequencyProfileJson,
            SampleCount = values.Count,
            LearnedFromUtc = startTs,
            LearnedToUtc = endTs,
            ConfidenceLevel = confidenceLevel,
            Version = 1,
            CreatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    /// <summary>
    /// 为实例的所有模式学习基线
    /// </summary>
    public async Task<string> LearnAllModesAsync(string instanceId, long? startTs, long? endTs, CancellationToken ct)
    {
        // 清理过期任务
        CleanupExpiredTasks();

        var modes = await _modeRepo.ListEnabledByInstanceAsync(instanceId, ct);
        if (modes.Count == 0)
        {
            throw new InvalidOperationException($"No enabled operation modes for instance: {instanceId}");
        }

        var taskId = Guid.NewGuid().ToString("N")[..12];
        var state = new MotorLearningTaskState
        {
            TaskId = taskId,
            InstanceId = instanceId,
            ModeId = "ALL",
            Status = MotorLearningStatus.Running,
            StartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            DataStartTs = startTs ?? DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeMilliseconds(),
            DataEndTs = endTs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MinSamples = 500
        };

        lock (_lock)
        {
            _taskStates[taskId] = state;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var totalBaselines = 0;
                for (int i = 0; i < modes.Count; i++)
                {
                    var mode = modes[i];
                    UpdateTaskProgress(state.TaskId, $"Learning mode {i + 1}/{modes.Count}: {mode.Name}");

                    var request = new MotorBaselineLearningRequest
                    {
                        InstanceId = instanceId,
                        ModeId = mode.ModeId,
                        StartTs = state.DataStartTs,
                        EndTs = state.DataEndTs,
                        MinSamples = state.MinSamples
                    };

                    // 使用内部方法直接执行（不创建新任务）
                    var modeTaskId = $"{taskId}_{mode.ModeId}";
                    var modeState = new MotorLearningTaskState
                    {
                        TaskId = modeTaskId,
                        InstanceId = instanceId,
                        ModeId = mode.ModeId,
                        Status = MotorLearningStatus.Running,
                        StartTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        DataStartTs = state.DataStartTs,
                        DataEndTs = state.DataEndTs,
                        MinSamples = state.MinSamples
                    };

                    // 将子任务添加到状态字典以便 UpdateTaskState 能找到它
                    lock (_lock)
                    {
                        _taskStates[modeTaskId] = modeState;
                    }

                    await ExecuteLearningAsync(modeState, CancellationToken.None);

                    // 从字典获取更新后的状态
                    var updatedState = GetTaskState(modeTaskId);
                    if (updatedState?.Status == MotorLearningStatus.Completed)
                    {
                        totalBaselines++;
                    }

                    // 清理子任务状态（可选：保留用于调试）
                    lock (_lock)
                    {
                        _taskStates.Remove(modeTaskId);
                    }
                }

                UpdateTaskState(state.TaskId, MotorLearningStatus.Completed,
                    $"Learned baselines for {totalBaselines}/{modes.Count} modes");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Learn all modes failed for task {TaskId}", state.TaskId);
                UpdateTaskState(state.TaskId, MotorLearningStatus.Failed, ex.Message);
            }
        });  // 不传递 ct，避免 HTTP 请求结束后取消后台任务

        return taskId;
    }

    /// <summary>
    /// 增量更新基线（在线学习）
    /// </summary>
    public async Task<bool> UpdateBaselineIncrementalAsync(
        string instanceId,
        string modeId,
        MotorParameter parameter,
        IReadOnlyList<double> newValues,
        CancellationToken ct)
    {
        if (newValues.Count == 0) return false;

        var existing = await _baselineRepo.GetByModeAndParameterAsync(modeId, parameter, ct);
        if (existing == null)
        {
            _logger.LogWarning("No existing baseline for mode {ModeId} parameter {Parameter}", modeId, parameter);
            return false;
        }

        // Welford's 在线算法更新均值和方差
        var newMean = existing.Mean;
        var newM2 = existing.StdDev * existing.StdDev * existing.SampleCount;
        var newCount = existing.SampleCount;
        var newMin = existing.MinValue;
        var newMax = existing.MaxValue;

        foreach (var value in newValues)
        {
            newCount++;
            var delta = value - newMean;
            newMean += delta / newCount;
            var delta2 = value - newMean;
            newM2 += delta * delta2;

            newMin = Math.Min(newMin, value);
            newMax = Math.Max(newMax, value);
        }

        var newStdDev = Math.Sqrt(newM2 / newCount);

        // 使用 with 模式创建更新后的基线
        var updated = existing with
        {
            Mean = newMean,
            StdDev = newStdDev,
            MinValue = newMin,
            MaxValue = newMax,
            SampleCount = newCount,
            ConfidenceLevel = CalculateConfidenceLevel(newCount, newStdDev, newMean),
            UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _baselineRepo.UpdateAsync(updated, ct);

        _logger.LogDebug(
            "Updated baseline for mode {ModeId} parameter {Parameter}: n={Count}, mean={Mean:F3}",
            modeId, parameter, newCount, newMean);

        return true;
    }

    /// <summary>
    /// 获取学习任务状态
    /// </summary>
    public MotorLearningTaskState? GetTaskState(string taskId)
    {
        lock (_lock)
        {
            return _taskStates.TryGetValue(taskId, out var state) ? state : null;
        }
    }

    /// <summary>
    /// 获取实例的所有学习任务
    /// </summary>
    public IReadOnlyList<MotorLearningTaskState> GetTasksByInstance(string instanceId)
    {
        lock (_lock)
        {
            return _taskStates.Values
                .Where(s => s.InstanceId == instanceId)
                .OrderByDescending(s => s.StartTime)
                .ToList();
        }
    }

    #region Private Methods

    /// <summary>
    /// 清理过期的任务状态（完成超过5分钟的任务）
    /// </summary>
    private void CleanupExpiredTasks()
    {
        var expirationMs = 5 * 60 * 1000; // 5分钟
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        lock (_lock)
        {
            var expiredTasks = _taskStates.Values
                .Where(t => t.EndTime.HasValue && (now - t.EndTime.Value) > expirationMs)
                .Select(t => t.TaskId)
                .ToList();

            foreach (var taskId in expiredTasks)
            {
                _taskStates.Remove(taskId);
            }

            if (expiredTasks.Count > 0)
            {
                _logger.LogDebug("Cleaned up {Count} expired learning tasks", expiredTasks.Count);
            }
        }
    }

    private void UpdateTaskState(string taskId, MotorLearningStatus status, string? message = null)
    {
        lock (_lock)
        {
            if (_taskStates.TryGetValue(taskId, out var state))
            {
                state.Status = status;
                state.Message = message;
                if (status is MotorLearningStatus.Completed or MotorLearningStatus.Failed)
                {
                    state.EndTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }
            }
        }
    }

    private void UpdateTaskProgress(string taskId, string progress)
    {
        lock (_lock)
        {
            if (_taskStates.TryGetValue(taskId, out var state))
            {
                state.Progress = progress;
            }
        }
    }

    private static MotorStatisticsResult CalculateStatistics(List<double> values)
    {
        if (values.Count == 0)
            return new MotorStatisticsResult();

        var sorted = values.OrderBy(v => v).ToList();
        var n = sorted.Count;

        var mean = values.Average();
        var variance = values.Sum(v => Math.Pow(v - mean, 2)) / n;
        var stdDev = Math.Sqrt(variance);

        return new MotorStatisticsResult
        {
            Mean = mean,
            StdDev = stdDev,
            Min = sorted[0],
            Max = sorted[^1],
            Median = sorted[n / 2],
            Percentile05 = sorted[(int)(n * 0.05)],
            Percentile95 = sorted[(int)(n * 0.95)]
        };
    }

    private static double CalculateConfidenceLevel(int sampleCount, double stdDev, double mean)
    {
        // 基于样本量和变异系数计算置信度
        var cvFactor = mean != 0 ? 1 - Math.Min(1, stdDev / Math.Abs(mean)) : 0.5;
        var sampleFactor = Math.Min(1, sampleCount / 10000.0);
        return (cvFactor * 0.6 + sampleFactor * 0.4) * 100;
    }

    private static bool IsCurrentParameter(MotorParameter param)
    {
        return param is MotorParameter.CurrentPhaseA or MotorParameter.CurrentPhaseB
            or MotorParameter.CurrentPhaseC or MotorParameter.CurrentRMS;
    }

    private static double? GetNumericValue(TelemetryPoint point)
    {
        return point.ValueType switch
        {
            TagValueType.Float32 => point.Float32Value,
            TagValueType.Float64 => point.Float64Value,
            TagValueType.Int32 => point.Int32Value,
            TagValueType.Int64 => point.Int64Value,
            _ => null
        };
    }

    #endregion
}

/// <summary>
/// 电机基线学习请求
/// </summary>
public sealed class MotorBaselineLearningRequest
{
    /// <summary>电机实例ID</summary>
    public required string InstanceId { get; init; }

    /// <summary>操作模式ID</summary>
    public required string ModeId { get; init; }

    /// <summary>数据开始时间戳（默认7天前）</summary>
    public long? StartTs { get; init; }

    /// <summary>数据结束时间戳（默认当前）</summary>
    public long? EndTs { get; init; }

    /// <summary>最小样本数（默认1000）</summary>
    public int? MinSamples { get; init; }
}

/// <summary>
/// 学习任务状态
/// </summary>
public sealed class MotorLearningTaskState
{
    public string TaskId { get; init; } = "";
    public string InstanceId { get; init; } = "";
    public string ModeId { get; init; } = "";
    public MotorLearningStatus Status { get; set; }
    public string? Progress { get; set; }
    public string? Message { get; set; }
    public long StartTime { get; init; }
    public long? EndTime { get; set; }
    public long DataStartTs { get; init; }
    public long DataEndTs { get; init; }
    public int MinSamples { get; init; }
}

/// <summary>
/// 学习状态枚举
/// </summary>
public enum MotorLearningStatus
{
    Pending,
    Running,
    Completed,
    Failed
}

/// <summary>
/// 统计结果
/// </summary>
internal sealed class MotorStatisticsResult
{
    public double Mean { get; init; }
    public double StdDev { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public double Median { get; init; }
    public double Percentile05 { get; init; }
    public double Percentile95 { get; init; }
}
