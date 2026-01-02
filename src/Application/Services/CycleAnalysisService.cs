using System.Text.Json;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Application.Services;

/// <summary>
/// 周期分析服务实现
/// </summary>
public sealed class CycleAnalysisService : ICycleAnalysisService
{
    private readonly ITelemetryRepository _telemetryRepo;
    private readonly IWorkCycleRepository _cycleRepo;
    private readonly ICycleDeviceBaselineRepository _baselineRepo;
    private readonly ILogger<CycleAnalysisService> _logger;

    public CycleAnalysisService(
        ITelemetryRepository telemetryRepo,
        IWorkCycleRepository cycleRepo,
        ICycleDeviceBaselineRepository baselineRepo,
        ILogger<CycleAnalysisService> logger)
    {
        _telemetryRepo = telemetryRepo;
        _cycleRepo = cycleRepo;
        _baselineRepo = baselineRepo;
        _logger = logger;
    }

    public async Task<CycleAnalysisResult> AnalyzeCyclesAsync(
        CycleAnalysisRequest request,
        CancellationToken ct)
    {
        _logger.LogInformation("Starting cycle analysis for device {DeviceId}, time range: {Start} - {End}",
            request.DeviceId,
            DateTimeOffset.FromUnixTimeMilliseconds(request.StartTimeUtc),
            DateTimeOffset.FromUnixTimeMilliseconds(request.EndTimeUtc));

        // 1. 检测周期边界
        var boundaries = await DetectCycleBoundariesAsync(
            request.DeviceId,
            request.AngleTagId,
            request.StartTimeUtc,
            request.EndTimeUtc,
            request.AngleThreshold,
            ct);

        _logger.LogInformation("Detected {Count} cycle boundaries", boundaries.Count);

        if (boundaries.Count == 0)
        {
            return new CycleAnalysisResult
            {
                CycleCount = 0,
                AnomalyCycleCount = 0,
                Cycles = new List<WorkCycle>(),
                Summary = null
            };
        }

        // 2. 获取基线模型
        var motor1Baseline = await _baselineRepo.GetAsync(request.DeviceId, "current_angle_motor1", ct);
        var motor2Baseline = await _baselineRepo.GetAsync(request.DeviceId, "current_angle_motor2", ct);
        var balanceBaseline = await _baselineRepo.GetAsync(request.DeviceId, "motor_balance", ct);

        // 3. 分析每个周期
        var cycles = new List<WorkCycle>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (var (startUtc, endUtc) in boundaries)
        {
            var duration = (endUtc - startUtc) / 1000.0;
            
            // 过滤异常周期
            if (duration < request.MinCycleDuration || duration > request.MaxCycleDuration)
            {
                _logger.LogDebug("Skipping cycle with duration {Duration}s (out of range)", duration);
                continue;
            }

            var cycle = await AnalyzeSingleCycleAsync(
                request.DeviceId,
                startUtc,
                endUtc,
                request.AngleTagId,
                request.Motor1CurrentTagId,
                request.Motor2CurrentTagId,
                motor1Baseline,
                motor2Baseline,
                balanceBaseline,
                ct);

            if (cycle != null)
            {
                cycles.Add(cycle with { CreatedUtc = now });
            }
        }

        // 4. 计算统计摘要
        var summary = cycles.Count > 0 ? new CycleStatsSummary
        {
            AvgDuration = cycles.Average(c => c.DurationSeconds),
            AvgMotor1PeakCurrent = cycles.Average(c => c.Motor1PeakCurrent),
            AvgMotor2PeakCurrent = cycles.Average(c => c.Motor2PeakCurrent),
            AvgMotorBalanceRatio = cycles.Average(c => c.MotorBalanceRatio),
            AvgAnomalyScore = cycles.Average(c => c.AnomalyScore)
        } : null;

        _logger.LogInformation("Cycle analysis completed: {Total} cycles, {Anomalies} anomalies",
            cycles.Count, cycles.Count(c => c.IsAnomaly));

        return new CycleAnalysisResult
        {
            CycleCount = cycles.Count,
            AnomalyCycleCount = cycles.Count(c => c.IsAnomaly),
            Cycles = cycles,
            Summary = summary
        };
    }

    public async Task<CycleAnalysisResult> AnalyzeSegmentAsync(
        long segmentId,
        CycleAnalysisRequest request,
        CancellationToken ct)
    {
        // 先分析，然后给每个周期关联 segmentId
        var result = await AnalyzeCyclesAsync(request, ct);

        var cyclesWithSegment = result.Cycles
            .Select(c => c with { SegmentId = segmentId })
            .ToList();

        return result with { Cycles = cyclesWithSegment };
    }

    public async Task<IReadOnlyList<(long StartUtc, long EndUtc)>> DetectCycleBoundariesAsync(
        string deviceId,
        string angleTagId,
        long startTimeUtc,
        long endTimeUtc,
        double angleThreshold,
        CancellationToken ct)
    {
        // 获取角度数据
        var angleQuery = new HistoryQuery
        {
            DeviceId = deviceId,
            TagId = angleTagId,
            StartTs = startTimeUtc,
            EndTs = endTimeUtc,
            Limit = 1_000_000,
            Sort = SortDirection.Asc
        };
        var angleResult = await _telemetryRepo.QueryAsync(angleQuery, ct);
        var angleData = angleResult.Items;

        if (angleData.Count < 10)
        {
            return Array.Empty<(long, long)>();
        }

        // 提取时间戳和角度值
        var points = angleData
            .OrderBy(p => p.Ts)
            .Select(p => (Ts: p.Ts, Angle: ExtractNumericValue(p)))
            .ToList();

        // 检测周期边界：角度从低→高→低的过程
        var boundaries = new List<(long StartUtc, long EndUtc)>();
        
        bool inCycle = false;
        long cycleStart = 0;
        double maxAngleInCycle = 0;

        for (int i = 1; i < points.Count; i++)
        {
            var prev = points[i - 1];
            var curr = points[i];

            if (!inCycle)
            {
                // 检测周期开始：角度从低值开始上升
                if (prev.Angle < angleThreshold && curr.Angle >= angleThreshold)
                {
                    inCycle = true;
                    cycleStart = prev.Ts;
                    maxAngleInCycle = curr.Angle;
                }
            }
            else
            {
                maxAngleInCycle = Math.Max(maxAngleInCycle, curr.Angle);

                // 检测周期结束：角度回到低值
                if (curr.Angle < angleThreshold && maxAngleInCycle > 30) // 确保经历了有效翻转
                {
                    boundaries.Add((cycleStart, curr.Ts));
                    inCycle = false;
                    maxAngleInCycle = 0;
                }
            }
        }

        return boundaries;
    }

    private async Task<WorkCycle?> AnalyzeSingleCycleAsync(
        string deviceId,
        long startUtc,
        long endUtc,
        string angleTagId,
        string motor1TagId,
        string motor2TagId,
        CycleDeviceBaseline? motor1Baseline,
        CycleDeviceBaseline? motor2Baseline,
        CycleDeviceBaseline? balanceBaseline,
        CancellationToken ct)
    {
        // 获取周期内的数据
        var angleResult = await _telemetryRepo.QueryAsync(new HistoryQuery
        {
            DeviceId = deviceId, TagId = angleTagId, StartTs = startUtc, EndTs = endUtc, Limit = 10000, Sort = SortDirection.Asc
        }, ct);
        var motor1Result = await _telemetryRepo.QueryAsync(new HistoryQuery
        {
            DeviceId = deviceId, TagId = motor1TagId, StartTs = startUtc, EndTs = endUtc, Limit = 10000, Sort = SortDirection.Asc
        }, ct);
        var motor2Result = await _telemetryRepo.QueryAsync(new HistoryQuery
        {
            DeviceId = deviceId, TagId = motor2TagId, StartTs = startUtc, EndTs = endUtc, Limit = 10000, Sort = SortDirection.Asc
        }, ct);

        var angleData = angleResult.Items;
        var motor1Data = motor1Result.Items;
        var motor2Data = motor2Result.Items;

        if (angleData.Count < 5 || motor1Data.Count < 5 || motor2Data.Count < 5)
        {
            return null;
        }

        var angles = angleData.Select(p => ExtractNumericValue(p)).ToList();
        var motor1Currents = motor1Data.Select(p => ExtractNumericValue(p)).ToList();
        var motor2Currents = motor2Data.Select(p => ExtractNumericValue(p)).ToList();

        // 计算特征
        var duration = (endUtc - startUtc) / 1000.0;
        var maxAngle = angles.Max();
        var motor1Peak = motor1Currents.Max();
        var motor2Peak = motor2Currents.Max();
        var motor1Avg = motor1Currents.Average();
        var motor2Avg = motor2Currents.Average();
        
        // 计算能耗（电流积分近似）
        var motor1Energy = motor1Currents.Sum() * (duration / motor1Currents.Count);
        var motor2Energy = motor2Currents.Sum() * (duration / motor2Currents.Count);

        // 计算电机平衡比
        var balanceRatio = motor2Avg > 0 ? motor1Avg / motor2Avg : 1.0;

        // 计算基线偏差
        var baselineDeviation = CalculateBaselineDeviation(
            angles, motor1Currents, motor2Currents,
            motor1Baseline, motor2Baseline);

        // 计算异常分数
        var (anomalyScore, isAnomaly, anomalyType) = CalculateAnomalyScore(
            duration, maxAngle, motor1Peak, motor2Peak,
            balanceRatio, baselineDeviation, balanceBaseline);

        return new WorkCycle
        {
            DeviceId = deviceId,
            SegmentId = null,
            StartTimeUtc = startUtc,
            EndTimeUtc = endUtc,
            DurationSeconds = duration,
            MaxAngle = maxAngle,
            Motor1PeakCurrent = motor1Peak,
            Motor2PeakCurrent = motor2Peak,
            Motor1AvgCurrent = motor1Avg,
            Motor2AvgCurrent = motor2Avg,
            Motor1Energy = motor1Energy,
            Motor2Energy = motor2Energy,
            MotorBalanceRatio = balanceRatio,
            BaselineDeviationPercent = baselineDeviation,
            AnomalyScore = anomalyScore,
            IsAnomaly = isAnomaly,
            AnomalyType = anomalyType,
            DetailsJson = null,
            CreatedUtc = 0
        };
    }

    private double CalculateBaselineDeviation(
        List<double> angles,
        List<double> motor1Currents,
        List<double> motor2Currents,
        CycleDeviceBaseline? motor1Baseline,
        CycleDeviceBaseline? motor2Baseline)
    {
        if (motor1Baseline == null || motor2Baseline == null)
            return 0;

        try
        {
            var model1 = JsonSerializer.Deserialize<CurrentAngleModel>(motor1Baseline.ModelJson);
            var model2 = JsonSerializer.Deserialize<CurrentAngleModel>(motor2Baseline.ModelJson);

            if (model1?.Coefficients == null || model2?.Coefficients == null)
                return 0;

            // 计算每个点与基线的偏差
            var deviations = new List<double>();
            var minCount = Math.Min(angles.Count, Math.Min(motor1Currents.Count, motor2Currents.Count));

            for (int i = 0; i < minCount; i++)
            {
                var angle = angles[i];
                if (angle < 5) continue; // 忽略静止状态

                var expected1 = EvaluatePolynomial(model1.Coefficients, angle);
                var expected2 = EvaluatePolynomial(model2.Coefficients, angle);

                var actual1 = motor1Currents[i];
                var actual2 = motor2Currents[i];

                if (expected1 > 100)
                    deviations.Add(Math.Abs(actual1 - expected1) / expected1 * 100);
                if (expected2 > 100)
                    deviations.Add(Math.Abs(actual2 - expected2) / expected2 * 100);
            }

            return deviations.Count > 0 ? deviations.Average() : 0;
        }
        catch
        {
            return 0;
        }
    }

    private (double Score, bool IsAnomaly, string? Type) CalculateAnomalyScore(
        double duration,
        double maxAngle,
        double motor1Peak,
        double motor2Peak,
        double balanceRatio,
        double baselineDeviation,
        CycleDeviceBaseline? balanceBaseline)
    {
        var scores = new List<(double Score, string Type)>();

        // 1. 周期时长异常
        if (duration > 120)
            scores.Add((30 + (duration - 120) / 10, AnomalyTypes.CycleTimeout));
        else if (duration < 30)
            scores.Add((30 + (30 - duration), AnomalyTypes.CycleTooShort));

        // 2. 过电流 (假设正常峰值 < 12000)
        if (motor1Peak > 12000 || motor2Peak > 12000)
        {
            var overPercent = Math.Max(motor1Peak, motor2Peak) / 12000.0 * 100 - 100;
            scores.Add((20 + overPercent, AnomalyTypes.OverCurrent));
        }

        // 3. 电机不平衡
        var balanceScore = 0.0;
        if (balanceBaseline != null)
        {
            try
            {
                var model = JsonSerializer.Deserialize<MotorBalanceModel>(balanceBaseline.ModelJson);
                if (model != null && (balanceRatio < model.LowerBound || balanceRatio > model.UpperBound))
                {
                    balanceScore = Math.Abs(balanceRatio - model.MeanRatio) / model.StdRatio * 10;
                    scores.Add((balanceScore, AnomalyTypes.MotorImbalance));
                }
            }
            catch { }
        }
        else
        {
            // 默认判断：偏离 1.0 超过 30%
            if (balanceRatio < 0.7 || balanceRatio > 1.3)
            {
                balanceScore = Math.Abs(balanceRatio - 1.0) * 50;
                scores.Add((balanceScore, AnomalyTypes.MotorImbalance));
            }
        }

        // 4. 基线偏离
        if (baselineDeviation > 20)
        {
            scores.Add((baselineDeviation, AnomalyTypes.BaselineDeviation));
        }

        // 5. 角度不足 (翻转不完整)
        if (maxAngle < 100)
        {
            scores.Add((20 + (100 - maxAngle) / 2, AnomalyTypes.AngleStall));
        }

        if (scores.Count == 0)
            return (0, false, null);

        var totalScore = Math.Min(100, scores.Sum(s => s.Score));
        var isAnomaly = totalScore >= 30;
        var primaryType = scores.OrderByDescending(s => s.Score).First().Type;

        return (totalScore, isAnomaly, isAnomaly ? primaryType : null);
    }

    private static double EvaluatePolynomial(double[] coefficients, double x)
    {
        // coefficients = [a, b, c] for ax² + bx + c
        if (coefficients.Length >= 3)
            return coefficients[0] * x * x + coefficients[1] * x + coefficients[2];
        if (coefficients.Length == 2)
            return coefficients[0] * x + coefficients[1];
        if (coefficients.Length == 1)
            return coefficients[0];
        return 0;
    }

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
}
