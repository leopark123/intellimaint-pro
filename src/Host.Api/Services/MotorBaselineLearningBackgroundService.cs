using IntelliMaint.Application.Services;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Host.Api.Services;

/// <summary>
/// v64: 电机基线学习后台服务
/// 定期检查电机实例，执行增量基线学习
/// </summary>
public sealed class MotorBaselineLearningBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MotorBaselineLearningBackgroundService> _logger;
    private readonly MotorBaselineLearningOptions _options;

    public MotorBaselineLearningBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<MotorBaselineLearningOptions> options,
        ILogger<MotorBaselineLearningBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("[MotorBaseline] Background learning is disabled");
            return;
        }

        _logger.LogInformation("[MotorBaseline] Background learning service started, interval: {Interval}s",
            _options.IntervalSeconds);

        // 启动延迟
        await Task.Delay(TimeSpan.FromSeconds(_options.StartupDelaySeconds), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAllInstancesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MotorBaseline] Error in background learning cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("[MotorBaseline] Background learning service stopped");
    }

    private async Task ProcessAllInstancesAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var instanceRepo = scope.ServiceProvider.GetRequiredService<IMotorInstanceRepository>();
        var mappingRepo = scope.ServiceProvider.GetRequiredService<IMotorParameterMappingRepository>();
        var modeRepo = scope.ServiceProvider.GetRequiredService<IOperationModeRepository>();
        var baselineRepo = scope.ServiceProvider.GetRequiredService<IBaselineProfileRepository>();
        var telemetryRepo = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
        var learningService = scope.ServiceProvider.GetRequiredService<MotorBaselineLearningService>();
        var modeDetector = scope.ServiceProvider.GetRequiredService<OperationModeDetector>();

        // 获取所有启用诊断的电机实例
        var instances = await instanceRepo.ListAsync(ct);
        var enabledInstances = instances.Where(i => i.DiagnosisEnabled).ToList();

        if (enabledInstances.Count == 0)
        {
            _logger.LogDebug("[MotorBaseline] No enabled motor instances");
            return;
        }

        _logger.LogDebug("[MotorBaseline] Processing {Count} enabled instances", enabledInstances.Count);

        foreach (var instance in enabledInstances)
        {
            try
            {
                await ProcessInstanceAsync(
                    instance, mappingRepo, modeRepo, baselineRepo,
                    telemetryRepo, learningService, modeDetector, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MotorBaseline] Error processing instance {InstanceId}", instance.InstanceId);
            }
        }
    }

    private async Task ProcessInstanceAsync(
        MotorInstance instance,
        IMotorParameterMappingRepository mappingRepo,
        IOperationModeRepository modeRepo,
        IBaselineProfileRepository baselineRepo,
        ITelemetryRepository telemetryRepo,
        MotorBaselineLearningService learningService,
        OperationModeDetector modeDetector,
        CancellationToken ct)
    {
        var mappings = await mappingRepo.ListByInstanceAsync(instance.InstanceId, ct);
        if (mappings.Count == 0) return;

        // 获取当前操作模式
        var currentMode = await modeDetector.DetectModeFromTelemetryAsync(
            instance.InstanceId, instance.DeviceId, mappings, ct);

        if (currentMode == null)
        {
            _logger.LogDebug("[MotorBaseline] No active mode for instance {InstanceId}", instance.InstanceId);
            return;
        }

        // 检查是否有足够的新数据进行增量学习
        var existingBaselines = await baselineRepo.ListByModeAsync(currentMode.ModeId, ct);
        if (existingBaselines.Count == 0)
        {
            // 没有基线，跳过增量学习（需要手动触发全量学习）
            _logger.LogDebug("[MotorBaseline] No baseline for mode {ModeId}, skipping incremental learning",
                currentMode.ModeId);
            return;
        }

        // 获取最近的数据进行增量更新
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var lookbackMs = _options.IncrementalLookbackMinutes * 60 * 1000L;

        foreach (var mapping in mappings.Where(m => m.UsedForDiagnosis))
        {
            var baseline = existingBaselines.FirstOrDefault(b => b.Parameter == mapping.Parameter);
            if (baseline == null) continue;

            // 从上次学习时间或回溯时间开始查询
            var startTs = Math.Max(baseline.LearnedToUtc, now - lookbackMs);

            var query = new HistoryQuery
            {
                DeviceId = instance.DeviceId,
                TagId = mapping.TagId,
                StartTs = startTs,
                EndTs = now,
                Limit = _options.MaxIncrementalSamples
            };

            var result = await telemetryRepo.QueryAsync(query, ct);

            if (result.Items.Count < _options.MinIncrementalSamples)
            {
                continue; // 数据不足
            }

            // 提取数值
            var values = result.Items
                .Select(p => GetNumericValue(p))
                .Where(v => v.HasValue)
                .Select(v => v!.Value * mapping.ScaleFactor + mapping.Offset)
                .ToList();

            if (values.Count >= _options.MinIncrementalSamples)
            {
                await learningService.UpdateBaselineIncrementalAsync(
                    instance.InstanceId, currentMode.ModeId, mapping.Parameter, values, ct);

                _logger.LogDebug(
                    "[MotorBaseline] Incremental update for {InstanceId}/{ModeId}/{Parameter}: {Count} samples",
                    instance.InstanceId, currentMode.ModeId, mapping.Parameter, values.Count);
            }
        }
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
}

/// <summary>
/// 电机基线学习配置选项
/// </summary>
public sealed class MotorBaselineLearningOptions
{
    /// <summary>是否启用后台学习</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>启动延迟（秒）</summary>
    public int StartupDelaySeconds { get; set; } = 60;

    /// <summary>学习周期（秒）</summary>
    public int IntervalSeconds { get; set; } = 3600; // 每小时

    /// <summary>增量学习回溯时间（分钟）</summary>
    public int IncrementalLookbackMinutes { get; set; } = 60;

    /// <summary>增量学习最小样本数</summary>
    public int MinIncrementalSamples { get; set; } = 100;

    /// <summary>增量学习最大样本数</summary>
    public int MaxIncrementalSamples { get; set; } = 10000;
}
