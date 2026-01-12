using System.Text.Json;
using IntelliMaint.Application.Services;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Host.Api.Services;

/// <summary>
/// v64: 电机诊断后台服务
/// 定期对所有电机实例执行故障诊断
/// </summary>
public sealed class MotorDiagnosisBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MotorDiagnosisBackgroundService> _logger;
    private readonly MotorDiagnosisOptions _options;

    public MotorDiagnosisBackgroundService(
        IServiceProvider serviceProvider,
        IOptions<MotorDiagnosisOptions> options,
        ILogger<MotorDiagnosisBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("[MotorDiagnosis] Background service is disabled");
            return;
        }

        _logger.LogInformation(
            "[MotorDiagnosis] Background service started, interval: {Interval}s",
            _options.IntervalSeconds);

        // 启动延迟
        await Task.Delay(TimeSpan.FromSeconds(_options.StartupDelaySeconds), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DiagnoseAllInstancesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MotorDiagnosis] Error in diagnosis cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.IntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("[MotorDiagnosis] Background service stopped");
    }

    private async Task DiagnoseAllInstancesAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var instanceRepo = scope.ServiceProvider.GetRequiredService<IMotorInstanceRepository>();
        var faultService = scope.ServiceProvider.GetRequiredService<MotorFaultDetectionService>();
        var alarmRepo = scope.ServiceProvider.GetRequiredService<IAlarmRepository>();

        // 获取所有启用诊断的电机实例
        var instances = await instanceRepo.ListAsync(ct);
        var enabledInstances = instances.Where(i => i.DiagnosisEnabled).ToList();

        if (enabledInstances.Count == 0)
        {
            _logger.LogDebug("[MotorDiagnosis] No enabled motor instances");
            return;
        }

        _logger.LogDebug("[MotorDiagnosis] Diagnosing {Count} motor instances", enabledInstances.Count);

        var config = new FaultDetectionConfig
        {
            MinorThreshold = _options.MinorThreshold,
            ModerateThreshold = _options.ModerateThreshold,
            SevereThreshold = _options.SevereThreshold,
            CriticalThreshold = _options.CriticalThreshold,
            PhaseImbalanceThreshold = _options.PhaseImbalanceThreshold,
            EnableAlarmGeneration = _options.EnableAlarmGeneration,
            MinAlarmSeverity = _options.MinAlarmSeverity
        };

        var diagnosisCount = 0;
        var faultCount = 0;
        var alarmCount = 0;

        foreach (var instance in enabledInstances)
        {
            try
            {
                var result = await faultService.DiagnoseAsync(instance.InstanceId, config, ct);

                if (result != null)
                {
                    diagnosisCount++;
                    faultCount += result.Faults.Count;

                    // 生成告警（如果配置启用且满足严重程度）
                    if (config.EnableAlarmGeneration && result.Faults.Count > 0)
                    {
                        var maxSeverity = result.Faults.Max(f => f.Severity);
                        if (maxSeverity >= config.MinAlarmSeverity)
                        {
                            var alarmGenerated = await GenerateAlarmAsync(
                                instance, result, maxSeverity, alarmRepo, ct);

                            if (alarmGenerated)
                                alarmCount++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MotorDiagnosis] Error diagnosing instance {InstanceId}",
                    instance.InstanceId);
            }
        }

        _logger.LogInformation(
            "[MotorDiagnosis] Cycle complete: {DiagnosisCount} diagnoses, {FaultCount} faults, {AlarmCount} alarms",
            diagnosisCount, faultCount, alarmCount);
    }

    private async Task<bool> GenerateAlarmAsync(
        MotorInstance instance,
        MotorDiagnosisResult result,
        FaultSeverity maxSeverity,
        IAlarmRepository alarmRepo,
        CancellationToken ct)
    {
        try
        {
            // Severity: 1=Info, 2=Warning, 3=Alarm, 4=Critical
            var severity = maxSeverity switch
            {
                FaultSeverity.Critical => 4,
                FaultSeverity.Severe => 3,
                FaultSeverity.Moderate => 2,
                _ => 1
            };

            // 构建告警消息
            var topFaults = result.Faults
                .OrderByDescending(f => f.Severity)
                .Take(3)
                .Select(f => f.Description ?? GetFaultTypeName(f.FaultType));

            var message = $"电机诊断异常: {string.Join("; ", topFaults)}";

            var alarm = new AlarmRecord
            {
                AlarmId = Guid.NewGuid().ToString("N")[..12],
                DeviceId = instance.DeviceId,
                TagId = $"motor_{instance.InstanceId}",
                Ts = result.Timestamp,
                Severity = severity,
                Code = $"MOTOR_{maxSeverity.ToString().ToUpper()}",
                Message = message,
                Status = AlarmStatus.Open,
                CreatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            await alarmRepo.CreateAsync(alarm, ct);

            _logger.LogInformation(
                "[MotorDiagnosis] Alarm generated for instance {InstanceId}: Severity={Severity} - {Message}",
                instance.InstanceId, severity, message);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MotorDiagnosis] Failed to generate alarm for instance {InstanceId}",
                instance.InstanceId);
            return false;
        }
    }

    private static string GetFaultTypeName(MotorFaultType faultType)
    {
        return faultType switch
        {
            MotorFaultType.PhaseImbalance => "三相不平衡",
            MotorFaultType.Overcurrent => "过电流",
            MotorFaultType.Undercurrent => "欠电流",
            MotorFaultType.Overvoltage => "过电压",
            MotorFaultType.Undervoltage => "欠电压",
            MotorFaultType.BearingOuterRace => "轴承外圈故障",
            MotorFaultType.BearingInnerRace => "轴承内圈故障",
            MotorFaultType.Overheating => "过热",
            MotorFaultType.Overload => "过载",
            _ => faultType.ToString()
        };
    }
}

/// <summary>
/// 电机诊断配置选项
/// </summary>
public sealed class MotorDiagnosisOptions
{
    /// <summary>是否启用后台诊断</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>启动延迟（秒）</summary>
    public int StartupDelaySeconds { get; set; } = 30;

    /// <summary>诊断周期（秒）</summary>
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>轻微偏离阈值</summary>
    public double MinorThreshold { get; set; } = 2.0;

    /// <summary>中度偏离阈值</summary>
    public double ModerateThreshold { get; set; } = 3.0;

    /// <summary>严重偏离阈值</summary>
    public double SevereThreshold { get; set; } = 4.0;

    /// <summary>危急偏离阈值</summary>
    public double CriticalThreshold { get; set; } = 5.0;

    /// <summary>相间不平衡阈值（%）</summary>
    public double PhaseImbalanceThreshold { get; set; } = 5.0;

    /// <summary>是否启用告警生成</summary>
    public bool EnableAlarmGeneration { get; set; } = true;

    /// <summary>告警生成的最低严重程度</summary>
    public FaultSeverity MinAlarmSeverity { get; set; } = FaultSeverity.Moderate;
}
