using System.Text.Json;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Application.Services;

/// <summary>
/// v64: 电机故障检测服务
/// 将当前参数与基线比较，检测各类故障
/// </summary>
public sealed class MotorFaultDetectionService
{
    private readonly IMotorInstanceRepository _instanceRepo;
    private readonly IMotorModelRepository _modelRepo;
    private readonly IMotorParameterMappingRepository _mappingRepo;
    private readonly IOperationModeRepository _modeRepo;
    private readonly IBaselineProfileRepository _baselineRepo;
    private readonly ITelemetryRepository _telemetryRepo;
    private readonly MotorFftAnalyzer _fftAnalyzer;
    private readonly OperationModeDetector _modeDetector;
    private readonly ILogger<MotorFaultDetectionService> _logger;

    // 最新诊断结果缓存
    private readonly Dictionary<string, MotorDiagnosisResult> _latestResults = new();
    private readonly object _lock = new();

    public MotorFaultDetectionService(
        IMotorInstanceRepository instanceRepo,
        IMotorModelRepository modelRepo,
        IMotorParameterMappingRepository mappingRepo,
        IOperationModeRepository modeRepo,
        IBaselineProfileRepository baselineRepo,
        ITelemetryRepository telemetryRepo,
        MotorFftAnalyzer fftAnalyzer,
        OperationModeDetector modeDetector,
        ILogger<MotorFaultDetectionService> logger)
    {
        _instanceRepo = instanceRepo;
        _modelRepo = modelRepo;
        _mappingRepo = mappingRepo;
        _modeRepo = modeRepo;
        _baselineRepo = baselineRepo;
        _telemetryRepo = telemetryRepo;
        _fftAnalyzer = fftAnalyzer;
        _modeDetector = modeDetector;
        _logger = logger;
    }

    /// <summary>
    /// 对电机实例执行诊断
    /// </summary>
    public async Task<MotorDiagnosisResult?> DiagnoseAsync(
        string instanceId,
        FaultDetectionConfig? config = null,
        CancellationToken ct = default)
    {
        config ??= new FaultDetectionConfig();

        var instance = await _instanceRepo.GetAsync(instanceId, ct);
        if (instance == null || !instance.DiagnosisEnabled)
        {
            _logger.LogDebug("Instance {InstanceId} not found or diagnosis disabled", instanceId);
            return null;
        }

        var model = await _modelRepo.GetAsync(instance.ModelId, ct);
        var mappings = await _mappingRepo.ListByInstanceAsync(instanceId, ct);

        if (mappings.Count == 0)
        {
            _logger.LogDebug("No mappings for instance {InstanceId}", instanceId);
            return null;
        }

        // 检测当前操作模式
        var currentMode = await _modeDetector.DetectModeFromTelemetryAsync(
            instanceId, instance.DeviceId, mappings, ct);

        if (currentMode == null)
        {
            _logger.LogDebug("No active mode for instance {InstanceId}", instanceId);
            return null;
        }

        // 获取该模式的基线
        var baselines = await _baselineRepo.ListByModeAsync(currentMode.ModeId, ct);
        if (baselines.Count == 0)
        {
            _logger.LogDebug("No baselines for mode {ModeId}", currentMode.ModeId);
            return null;
        }

        // 获取当前参数值
        var currentValues = await GetCurrentValuesAsync(instance.DeviceId, mappings, ct);

        // 执行诊断
        var result = PerformDiagnosis(
            instanceId, currentMode.ModeId, model, mappings, baselines, currentValues, config);

        // 缓存结果
        lock (_lock)
        {
            _latestResults[instanceId] = result;
        }

        _logger.LogDebug(
            "Diagnosis for {InstanceId}: HealthScore={Score:F1}, Faults={FaultCount}",
            instanceId, result.HealthScore, result.Faults.Count);

        return result;
    }

    /// <summary>
    /// 获取最新诊断结果
    /// </summary>
    public MotorDiagnosisResult? GetLatestResult(string instanceId)
    {
        lock (_lock)
        {
            return _latestResults.TryGetValue(instanceId, out var result) ? result : null;
        }
    }

    /// <summary>
    /// 获取所有最新诊断结果
    /// </summary>
    public IReadOnlyList<MotorDiagnosisResult> GetAllLatestResults()
    {
        lock (_lock)
        {
            return _latestResults.Values.ToList();
        }
    }

    /// <summary>
    /// 执行诊断逻辑
    /// </summary>
    private MotorDiagnosisResult PerformDiagnosis(
        string instanceId,
        string modeId,
        MotorModel? model,
        IReadOnlyList<MotorParameterMapping> mappings,
        IReadOnlyList<BaselineProfile> baselines,
        Dictionary<MotorParameter, double> currentValues,
        FaultDetectionConfig config)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var faults = new List<DetectedFault>();
        var deviations = new List<ParameterDeviation>();

        // 1. 检查每个参数的偏离
        foreach (var baseline in baselines)
        {
            if (!currentValues.TryGetValue(baseline.Parameter, out var currentValue))
                continue;

            var deviation = CalculateDeviation(baseline, currentValue, config);
            deviations.Add(deviation);

            if (deviation.IsAbnormal)
            {
                var fault = CreateFaultFromDeviation(baseline.Parameter, deviation, config);
                if (fault != null && fault.Confidence >= config.MinConfidence)
                {
                    faults.Add(fault);
                }
            }
        }

        // 2. 检查相间不平衡（如果有三相电流）
        var phaseImbalanceFault = CheckPhaseImbalance(currentValues, baselines, config);
        if (phaseImbalanceFault != null)
            faults.Add(phaseImbalanceFault);

        // 3. 检查频域故障（如果有电流数据和 FFT 基线）
        var fftFaults = CheckFrequencyDomainFaults(baselines, model, config);
        faults.AddRange(fftFaults);

        // 4. 计算健康得分
        var healthScore = CalculateHealthScore(deviations, faults);

        // 5. 生成摘要和建议
        var (summary, recommendations) = GenerateSummaryAndRecommendations(faults, deviations);

        return new MotorDiagnosisResult
        {
            DiagnosisId = Guid.NewGuid().ToString("N")[..12],
            InstanceId = instanceId,
            ModeId = modeId,
            Timestamp = timestamp,
            HealthScore = healthScore,
            Faults = faults,
            Deviations = deviations,
            Summary = summary,
            Recommendations = recommendations
        };
    }

    /// <summary>
    /// 计算参数偏离
    /// </summary>
    private ParameterDeviation CalculateDeviation(
        BaselineProfile baseline,
        double currentValue,
        FaultDetectionConfig config)
    {
        var sigma = baseline.StdDev > 0
            ? (currentValue - baseline.Mean) / baseline.StdDev
            : 0;

        var direction = sigma > config.MinorThreshold ? 1
            : sigma < -config.MinorThreshold ? -1
            : 0;

        var isAbnormal = Math.Abs(sigma) >= config.MinorThreshold;

        return new ParameterDeviation
        {
            Parameter = baseline.Parameter,
            CurrentValue = currentValue,
            BaselineMean = baseline.Mean,
            BaselineStdDev = baseline.StdDev,
            DeviationSigma = sigma,
            Direction = direction,
            IsAbnormal = isAbnormal
        };
    }

    /// <summary>
    /// 从偏离创建故障
    /// </summary>
    private DetectedFault? CreateFaultFromDeviation(
        MotorParameter parameter,
        ParameterDeviation deviation,
        FaultDetectionConfig config)
    {
        var absSigma = Math.Abs(deviation.DeviationSigma);
        var severity = GetSeverityFromSigma(absSigma, config);

        if (severity == FaultSeverity.Normal)
            return null;

        var faultType = InferFaultType(parameter, deviation);
        var confidence = CalculateConfidence(absSigma, config);

        return new DetectedFault
        {
            FaultType = faultType,
            Severity = severity,
            Confidence = confidence,
            Description = GetFaultDescription(faultType, parameter, deviation),
            RelatedParameter = parameter,
            CurrentValue = deviation.CurrentValue,
            BaselineValue = deviation.BaselineMean,
            DeviationSigma = deviation.DeviationSigma
        };
    }

    /// <summary>
    /// 根据标准差倍数确定严重程度
    /// </summary>
    private static FaultSeverity GetSeverityFromSigma(double absSigma, FaultDetectionConfig config)
    {
        if (absSigma >= config.CriticalThreshold) return FaultSeverity.Critical;
        if (absSigma >= config.SevereThreshold) return FaultSeverity.Severe;
        if (absSigma >= config.ModerateThreshold) return FaultSeverity.Moderate;
        if (absSigma >= config.MinorThreshold) return FaultSeverity.Minor;
        return FaultSeverity.Normal;
    }

    /// <summary>
    /// 计算故障置信度
    /// </summary>
    private static double CalculateConfidence(double absSigma, FaultDetectionConfig config)
    {
        // 基于标准差倍数的置信度（2σ=60%, 3σ=75%, 4σ=85%, 5σ=95%）
        var baseConfidence = Math.Min(95, 50 + absSigma * 10);
        return baseConfidence;
    }

    /// <summary>
    /// 根据参数和偏离推断故障类型
    /// </summary>
    private static MotorFaultType InferFaultType(MotorParameter parameter, ParameterDeviation deviation)
    {
        return parameter switch
        {
            MotorParameter.CurrentPhaseA or MotorParameter.CurrentPhaseB or
            MotorParameter.CurrentPhaseC or MotorParameter.CurrentRMS =>
                deviation.Direction > 0 ? MotorFaultType.Overcurrent : MotorFaultType.Undercurrent,

            MotorParameter.VoltagePhaseA or MotorParameter.VoltagePhaseB or
            MotorParameter.VoltagePhaseC or MotorParameter.VoltageRMS =>
                deviation.Direction > 0 ? MotorFaultType.Overvoltage : MotorFaultType.Undervoltage,

            MotorParameter.PowerFactor => MotorFaultType.PowerFactorAbnormal,

            MotorParameter.Temperature => MotorFaultType.Overheating,

            MotorParameter.Vibration => MotorFaultType.MechanicalLooseness,

            MotorParameter.Speed => MotorFaultType.ParameterDrift,

            MotorParameter.Torque =>
                deviation.Direction > 0 ? MotorFaultType.Overload : MotorFaultType.ParameterDrift,

            _ => MotorFaultType.ParameterDrift
        };
    }

    /// <summary>
    /// 检查三相不平衡
    /// </summary>
    private DetectedFault? CheckPhaseImbalance(
        Dictionary<MotorParameter, double> currentValues,
        IReadOnlyList<BaselineProfile> baselines,
        FaultDetectionConfig config)
    {
        // 获取三相电流
        if (!currentValues.TryGetValue(MotorParameter.CurrentPhaseA, out var ia)) return null;
        if (!currentValues.TryGetValue(MotorParameter.CurrentPhaseB, out var ib)) return null;
        if (!currentValues.TryGetValue(MotorParameter.CurrentPhaseC, out var ic)) return null;

        var avg = (ia + ib + ic) / 3;
        if (avg <= 0) return null;

        var maxDev = Math.Max(Math.Abs(ia - avg), Math.Max(Math.Abs(ib - avg), Math.Abs(ic - avg)));
        var imbalancePercent = maxDev / avg * 100;

        if (imbalancePercent < config.PhaseImbalanceThreshold)
            return null;

        var severity = imbalancePercent >= config.PhaseImbalanceThreshold * 2.5
            ? FaultSeverity.Critical
            : imbalancePercent >= config.PhaseImbalanceThreshold * 2
                ? FaultSeverity.Severe
                : imbalancePercent >= config.PhaseImbalanceThreshold * 1.5
                    ? FaultSeverity.Moderate
                    : FaultSeverity.Minor;

        return new DetectedFault
        {
            FaultType = MotorFaultType.PhaseImbalance,
            Severity = severity,
            Confidence = Math.Min(95, 60 + imbalancePercent * 2),
            Description = $"三相电流不平衡度 {imbalancePercent:F1}%，超过阈值 {config.PhaseImbalanceThreshold}%",
            CurrentValue = imbalancePercent,
            BaselineValue = config.PhaseImbalanceThreshold
        };
    }

    /// <summary>
    /// 检查频域故障（轴承、转子等）
    /// </summary>
    private List<DetectedFault> CheckFrequencyDomainFaults(
        IReadOnlyList<BaselineProfile> baselines,
        MotorModel? model,
        FaultDetectionConfig config)
    {
        var faults = new List<DetectedFault>();

        // 获取电流基线的频谱数据
        var currentBaseline = baselines.FirstOrDefault(b =>
            b.Parameter is MotorParameter.CurrentPhaseA or MotorParameter.CurrentRMS);

        if (currentBaseline?.FrequencyProfileJson == null)
            return faults;

        try
        {
            var freqProfile = JsonSerializer.Deserialize<FrequencyProfile>(currentBaseline.FrequencyProfileJson);
            if (freqProfile?.BearingAmplitudes == null)
                return faults;

            // 检查轴承故障特征频率
            // 注意：这里简化处理，实际应该与实时 FFT 结果比较
            var bearing = freqProfile.BearingAmplitudes;

            if (bearing.BPFO > freqProfile.NoiseFloor * config.BearingFaultGainThreshold)
            {
                faults.Add(new DetectedFault
                {
                    FaultType = MotorFaultType.BearingOuterRace,
                    Severity = FaultSeverity.Moderate,
                    Confidence = 70,
                    Description = "轴承外圈故障特征频率 (BPFO) 异常升高"
                });
            }

            if (bearing.BPFI > freqProfile.NoiseFloor * config.BearingFaultGainThreshold)
            {
                faults.Add(new DetectedFault
                {
                    FaultType = MotorFaultType.BearingInnerRace,
                    Severity = FaultSeverity.Moderate,
                    Confidence = 70,
                    Description = "轴承内圈故障特征频率 (BPFI) 异常升高"
                });
            }

            if (bearing.BSF > freqProfile.NoiseFloor * config.BearingFaultGainThreshold)
            {
                faults.Add(new DetectedFault
                {
                    FaultType = MotorFaultType.BearingBall,
                    Severity = FaultSeverity.Moderate,
                    Confidence = 70,
                    Description = "轴承滚动体故障特征频率 (BSF) 异常升高"
                });
            }

            if (bearing.FTF > freqProfile.NoiseFloor * config.BearingFaultGainThreshold)
            {
                faults.Add(new DetectedFault
                {
                    FaultType = MotorFaultType.BearingCage,
                    Severity = FaultSeverity.Minor,
                    Confidence = 65,
                    Description = "轴承保持架故障特征频率 (FTF) 异常升高"
                });
            }

            // 检查谐波畸变
            if (freqProfile.TotalHarmonicDistortion > config.ThdThreshold)
            {
                faults.Add(new DetectedFault
                {
                    FaultType = MotorFaultType.HarmonicAbnormal,
                    Severity = FaultSeverity.Minor,
                    Confidence = 75,
                    Description = $"总谐波畸变 (THD) {freqProfile.TotalHarmonicDistortion:F1}% 超过阈值 {config.ThdThreshold}%",
                    CurrentValue = freqProfile.TotalHarmonicDistortion,
                    BaselineValue = config.ThdThreshold
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse frequency profile JSON");
        }

        return faults;
    }

    /// <summary>
    /// 计算健康得分
    /// </summary>
    private static double CalculateHealthScore(
        List<ParameterDeviation> deviations,
        List<DetectedFault> faults)
    {
        if (deviations.Count == 0)
            return 100;

        // 基础得分从参数偏离计算
        var avgAbsSigma = deviations.Average(d => Math.Abs(d.DeviationSigma));
        var baseScore = Math.Max(0, 100 - avgAbsSigma * 15);

        // 根据故障严重程度扣分
        foreach (var fault in faults)
        {
            var penalty = fault.Severity switch
            {
                FaultSeverity.Critical => 30,
                FaultSeverity.Severe => 20,
                FaultSeverity.Moderate => 10,
                FaultSeverity.Minor => 5,
                _ => 0
            };
            baseScore -= penalty * (fault.Confidence / 100);
        }

        return Math.Max(0, Math.Min(100, baseScore));
    }

    /// <summary>
    /// 生成诊断摘要和建议
    /// </summary>
    private static (string summary, List<string> recommendations) GenerateSummaryAndRecommendations(
        List<DetectedFault> faults,
        List<ParameterDeviation> deviations)
    {
        var recommendations = new List<string>();

        if (faults.Count == 0)
        {
            return ("电机运行正常，各项参数在基线范围内", recommendations);
        }

        var maxSeverity = faults.Max(f => f.Severity);
        var faultTypes = faults.Select(f => f.FaultType).Distinct().ToList();

        var summaryParts = new List<string>();

        // 按严重程度排序故障
        foreach (var fault in faults.OrderByDescending(f => f.Severity).Take(3))
        {
            summaryParts.Add(fault.Description ?? GetFaultTypeName(fault.FaultType));
        }

        var summary = $"检测到 {faults.Count} 个异常: " + string.Join("; ", summaryParts);

        // 生成建议
        if (faultTypes.Contains(MotorFaultType.BearingOuterRace) ||
            faultTypes.Contains(MotorFaultType.BearingInnerRace) ||
            faultTypes.Contains(MotorFaultType.BearingBall))
        {
            recommendations.Add("建议检查轴承状态，安排预防性更换");
        }

        if (faultTypes.Contains(MotorFaultType.PhaseImbalance))
        {
            recommendations.Add("检查电源质量和接线端子连接");
        }

        if (faultTypes.Contains(MotorFaultType.Overcurrent) ||
            faultTypes.Contains(MotorFaultType.Overload))
        {
            recommendations.Add("检查负载是否过重，确认机械传动无卡阻");
        }

        if (faultTypes.Contains(MotorFaultType.Overheating))
        {
            recommendations.Add("检查散热系统，清理风道，确认环境温度");
        }

        if (maxSeverity >= FaultSeverity.Severe)
        {
            recommendations.Add("建议尽快安排停机检查");
        }
        else if (maxSeverity >= FaultSeverity.Moderate)
        {
            recommendations.Add("建议在下次计划停机时进行检查");
        }

        return (summary, recommendations);
    }

    /// <summary>
    /// 获取故障类型名称
    /// </summary>
    private static string GetFaultTypeName(MotorFaultType faultType)
    {
        return faultType switch
        {
            MotorFaultType.PhaseImbalance => "三相不平衡",
            MotorFaultType.Overcurrent => "过电流",
            MotorFaultType.Undercurrent => "欠电流",
            MotorFaultType.Overvoltage => "过电压",
            MotorFaultType.Undervoltage => "欠电压",
            MotorFaultType.PowerFactorAbnormal => "功率因数异常",
            MotorFaultType.HarmonicAbnormal => "谐波异常",
            MotorFaultType.RotorEccentricity => "转子偏心",
            MotorFaultType.BrokenRotorBar => "转子断条",
            MotorFaultType.BearingOuterRace => "轴承外圈故障",
            MotorFaultType.BearingInnerRace => "轴承内圈故障",
            MotorFaultType.BearingBall => "轴承滚动体故障",
            MotorFaultType.BearingCage => "轴承保持架故障",
            MotorFaultType.Misalignment => "轴不对中",
            MotorFaultType.MechanicalLooseness => "机械松动",
            MotorFaultType.Overheating => "过热",
            MotorFaultType.InsulationDegradation => "绝缘老化",
            MotorFaultType.Overload => "过载",
            MotorFaultType.FrequentStartStop => "频繁启停",
            MotorFaultType.ParameterDrift => "参数偏移",
            _ => "未知故障"
        };
    }

    /// <summary>
    /// 获取故障描述
    /// </summary>
    private static string GetFaultDescription(
        MotorFaultType faultType,
        MotorParameter parameter,
        ParameterDeviation deviation)
    {
        var paramName = GetParameterName(parameter);
        var direction = deviation.Direction > 0 ? "高于" : "低于";

        return $"{paramName}{direction}基线 {Math.Abs(deviation.DeviationSigma):F1}σ " +
               $"(当前={deviation.CurrentValue:F2}, 基线={deviation.BaselineMean:F2}±{deviation.BaselineStdDev:F2})";
    }

    /// <summary>
    /// 获取参数名称
    /// </summary>
    private static string GetParameterName(MotorParameter parameter)
    {
        return parameter switch
        {
            MotorParameter.CurrentPhaseA => "A相电流",
            MotorParameter.CurrentPhaseB => "B相电流",
            MotorParameter.CurrentPhaseC => "C相电流",
            MotorParameter.CurrentRMS => "RMS电流",
            MotorParameter.VoltagePhaseA => "A相电压",
            MotorParameter.VoltagePhaseB => "B相电压",
            MotorParameter.VoltagePhaseC => "C相电压",
            MotorParameter.VoltageRMS => "RMS电压",
            MotorParameter.Power => "功率",
            MotorParameter.PowerFactor => "功率因数",
            MotorParameter.Frequency => "频率",
            MotorParameter.Torque => "扭矩",
            MotorParameter.Speed => "转速",
            MotorParameter.Temperature => "温度",
            MotorParameter.Vibration => "振动",
            _ => parameter.ToString()
        };
    }

    /// <summary>
    /// 获取当前参数值
    /// </summary>
    private async Task<Dictionary<MotorParameter, double>> GetCurrentValuesAsync(
        string deviceId,
        IReadOnlyList<MotorParameterMapping> mappings,
        CancellationToken ct)
    {
        var values = new Dictionary<MotorParameter, double>();

        foreach (var mapping in mappings.Where(m => m.UsedForDiagnosis))
        {
            var latestList = await _telemetryRepo.GetLatestAsync(deviceId, mapping.TagId, ct);
            var latest = latestList.FirstOrDefault();

            if (latest != null)
            {
                var rawValue = GetNumericValue(latest);
                if (rawValue.HasValue)
                {
                    values[mapping.Parameter] = rawValue.Value * mapping.ScaleFactor + mapping.Offset;
                }
            }
        }

        return values;
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
