using System.Text.Json;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Application.Services;

/// <summary>
/// 基线学习服务实现
/// </summary>
public sealed class BaselineLearningService : IBaselineLearningService
{
    private readonly ITelemetryRepository _telemetryRepo;
    private readonly ICycleDeviceBaselineRepository _baselineRepo;
    private readonly IWorkCycleRepository _cycleRepo;
    private readonly ILogger<BaselineLearningService> _logger;

    public BaselineLearningService(
        ITelemetryRepository telemetryRepo,
        ICycleDeviceBaselineRepository baselineRepo,
        IWorkCycleRepository cycleRepo,
        ILogger<BaselineLearningService> logger)
    {
        _telemetryRepo = telemetryRepo;
        _baselineRepo = baselineRepo;
        _cycleRepo = cycleRepo;
        _logger = logger;
    }

    public async Task<CurrentAngleModel> LearnCurrentAngleModelAsync(
        string deviceId,
        string angleTagId,
        string currentTagId,
        long startTimeUtc,
        long endTimeUtc,
        CancellationToken ct)
    {
        _logger.LogInformation("Learning current-angle model for {DeviceId}, tag {CurrentTag}",
            deviceId, currentTagId);

        // 获取数据
        var angleResult = await _telemetryRepo.QueryAsync(new HistoryQuery
        {
            DeviceId = deviceId, TagId = angleTagId, StartTs = startTimeUtc, EndTs = endTimeUtc, Limit = 500000, Sort = SortDirection.Asc
        }, ct);
        var currentResult = await _telemetryRepo.QueryAsync(new HistoryQuery
        {
            DeviceId = deviceId, TagId = currentTagId, StartTs = startTimeUtc, EndTs = endTimeUtc, Limit = 500000, Sort = SortDirection.Asc
        }, ct);

        var angleData = angleResult.Items;
        var currentData = currentResult.Items;

        // 转换为时间戳索引的字典
        var angleByTs = angleData.ToDictionary(p => p.Ts, p => ExtractNumericValue(p));
        
        // 匹配角度和电流数据
        var pairs = new List<(double Angle, double Current)>();
        
        foreach (var cp in currentData)
        {
            var ts = cp.Ts;
            // 找最近的角度数据
            var closestAngle = angleByTs
                .Where(a => Math.Abs(a.Key - ts) < 1000) // 1秒内
                .OrderBy(a => Math.Abs(a.Key - ts))
                .FirstOrDefault();

            if (closestAngle.Key != 0)
            {
                var angle = closestAngle.Value;
                var current = ExtractNumericValue(cp);
                
                // 只取工作状态（角度 > 5）的数据
                if (angle > 5 && current > 100)
                {
                    pairs.Add((angle, current));
                }
            }
        }

        _logger.LogInformation("Collected {Count} angle-current pairs", pairs.Count);

        if (pairs.Count < 30)
        {
            throw new InvalidOperationException($"Not enough data points ({pairs.Count}) for baseline learning, need at least 30");
        }

        // 二次多项式拟合: current = a*angle² + b*angle + c
        var coefficients = PolynomialFit(
            pairs.Select(p => p.Angle).ToArray(),
            pairs.Select(p => p.Current).ToArray(),
            2);

        // 计算 R²
        var rSquared = CalculateRSquared(pairs, coefficients);

        // 计算各角度范围的电流统计
        var angleRanges = new Dictionary<int, CurrentRange>();
        for (int angle = 30; angle <= 150; angle += 30)
        {
            var inRange = pairs.Where(p => Math.Abs(p.Angle - angle) < 5).Select(p => p.Current).ToList();
            if (inRange.Count > 0)
            {
                angleRanges[angle] = new CurrentRange
                {
                    Mean = inRange.Average(),
                    Std = CalculateStd(inRange),
                    Min = inRange.Min(),
                    Max = inRange.Max()
                };
            }
        }

        var model = new CurrentAngleModel
        {
            MotorTagId = currentTagId,
            Coefficients = coefficients,
            RSquared = rSquared,
            AngleRanges = angleRanges
        };

        // 保存基线
        var baselineType = $"current_angle_{currentTagId.Replace("_ACTUAL_CURRENT", "").ToLower()}";
        var baseline = new CycleDeviceBaseline
        {
            DeviceId = deviceId,
            BaselineType = baselineType,
            SampleCount = pairs.Count,
            UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ModelJson = JsonSerializer.Serialize(model),
            StatsJson = null
        };

        await _baselineRepo.UpsertAsync(baseline, ct);

        _logger.LogInformation("Saved baseline {Type} with R²={RSquared:F4}", baselineType, rSquared);

        return model;
    }

    public async Task<MotorBalanceModel> LearnMotorBalanceModelAsync(
        string deviceId,
        string motor1TagId,
        string motor2TagId,
        long startTimeUtc,
        long endTimeUtc,
        CancellationToken ct)
    {
        _logger.LogInformation("Learning motor balance model for {DeviceId}", deviceId);

        var motor1Result = await _telemetryRepo.QueryAsync(new HistoryQuery
        {
            DeviceId = deviceId, TagId = motor1TagId, StartTs = startTimeUtc, EndTs = endTimeUtc, Limit = 500000, Sort = SortDirection.Asc
        }, ct);
        var motor2Result = await _telemetryRepo.QueryAsync(new HistoryQuery
        {
            DeviceId = deviceId, TagId = motor2TagId, StartTs = startTimeUtc, EndTs = endTimeUtc, Limit = 500000, Sort = SortDirection.Asc
        }, ct);

        var motor1Data = motor1Result.Items;
        var motor2Data = motor2Result.Items;

        var motor1ByTs = motor1Data.ToDictionary(p => p.Ts, p => ExtractNumericValue(p));
        
        var ratios = new List<double>();

        foreach (var m2 in motor2Data)
        {
            var ts = m2.Ts;
            var closestM1 = motor1ByTs
                .Where(m => Math.Abs(m.Key - ts) < 1000)
                .OrderBy(m => Math.Abs(m.Key - ts))
                .FirstOrDefault();

            if (closestM1.Key != 0)
            {
                var m1Val = closestM1.Value;
                var m2Val = ExtractNumericValue(m2);

                // 只在两者都在工作状态时计算
                if (m1Val > 500 && m2Val > 500)
                {
                    ratios.Add(m1Val / m2Val);
                }
            }
        }

        _logger.LogInformation("Collected {Count} balance ratios", ratios.Count);

        if (ratios.Count < 30)
        {
            throw new InvalidOperationException($"Not enough data points ({ratios.Count}) for balance baseline, need at least 30");
        }

        var mean = ratios.Average();
        var std = CalculateStd(ratios);

        var model = new MotorBalanceModel
        {
            MeanRatio = mean,
            StdRatio = std,
            LowerBound = mean - 2 * std,
            UpperBound = mean + 2 * std
        };

        var baseline = new CycleDeviceBaseline
        {
            DeviceId = deviceId,
            BaselineType = "motor_balance",
            SampleCount = ratios.Count,
            UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ModelJson = JsonSerializer.Serialize(model),
            StatsJson = null
        };

        await _baselineRepo.UpsertAsync(baseline, ct);

        _logger.LogInformation("Saved motor balance baseline: mean={Mean:F3}, range=[{Lower:F3}, {Upper:F3}]",
            mean, model.LowerBound, model.UpperBound);

        return model;
    }

    public async Task<CycleDurationModel> LearnCycleDurationModelAsync(
        string deviceId,
        IEnumerable<WorkCycle> cycles,
        CancellationToken ct)
    {
        var durations = cycles.Select(c => c.DurationSeconds).ToList();

        if (durations.Count < 5)
        {
            throw new InvalidOperationException($"Not enough cycles ({durations.Count}) for duration baseline, need at least 5");
        }

        var mean = durations.Average();
        var std = CalculateStd(durations);

        var model = new CycleDurationModel
        {
            MeanDuration = mean,
            StdDuration = std,
            LowerBound = mean - 2 * std,
            UpperBound = mean + 2 * std
        };

        var baseline = new CycleDeviceBaseline
        {
            DeviceId = deviceId,
            BaselineType = "cycle_duration",
            SampleCount = durations.Count,
            UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ModelJson = JsonSerializer.Serialize(model),
            StatsJson = null
        };

        await _baselineRepo.UpsertAsync(baseline, ct);

        _logger.LogInformation("Saved cycle duration baseline: mean={Mean:F1}s, range=[{Lower:F1}, {Upper:F1}]",
            mean, model.LowerBound, model.UpperBound);

        return model;
    }

    public async Task LearnAllBaselinesAsync(
        CycleAnalysisRequest config,
        long learningStartUtc,
        long learningEndUtc,
        CancellationToken ct)
    {
        _logger.LogInformation("Learning all baselines for {DeviceId}", config.DeviceId);

        // 1. 学习电流-角度基线
        await LearnCurrentAngleModelAsync(
            config.DeviceId,
            config.AngleTagId,
            config.Motor1CurrentTagId,
            learningStartUtc,
            learningEndUtc,
            ct);

        await LearnCurrentAngleModelAsync(
            config.DeviceId,
            config.AngleTagId,
            config.Motor2CurrentTagId,
            learningStartUtc,
            learningEndUtc,
            ct);

        // 2. 学习电机平衡基线
        await LearnMotorBalanceModelAsync(
            config.DeviceId,
            config.Motor1CurrentTagId,
            config.Motor2CurrentTagId,
            learningStartUtc,
            learningEndUtc,
            ct);

        _logger.LogInformation("All baselines learned successfully");
    }

    #region 数学工具方法

    /// <summary>
    /// 最小二乘多项式拟合
    /// </summary>
    private static double[] PolynomialFit(double[] x, double[] y, int degree)
    {
        int n = x.Length;
        int m = degree + 1;

        // 构建范德蒙矩阵
        var A = new double[n, m];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < m; j++)
            {
                A[i, j] = Math.Pow(x[i], m - 1 - j);
            }
        }

        // 使用正规方程: (A^T * A) * coeffs = A^T * y
        var ATA = new double[m, m];
        var ATy = new double[m];

        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < m; j++)
            {
                double sum = 0;
                for (int k = 0; k < n; k++)
                    sum += A[k, i] * A[k, j];
                ATA[i, j] = sum;
            }

            double sumY = 0;
            for (int k = 0; k < n; k++)
                sumY += A[k, i] * y[k];
            ATy[i] = sumY;
        }

        // 高斯消元求解
        return GaussianElimination(ATA, ATy);
    }

    private static double[] GaussianElimination(double[,] A, double[] b)
    {
        int n = b.Length;
        var augmented = new double[n, n + 1];

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
                augmented[i, j] = A[i, j];
            augmented[i, n] = b[i];
        }

        // 前向消元
        for (int col = 0; col < n; col++)
        {
            // 部分主元选取
            int maxRow = col;
            for (int row = col + 1; row < n; row++)
            {
                if (Math.Abs(augmented[row, col]) > Math.Abs(augmented[maxRow, col]))
                    maxRow = row;
            }

            // 交换行
            for (int j = 0; j <= n; j++)
            {
                (augmented[col, j], augmented[maxRow, j]) = (augmented[maxRow, j], augmented[col, j]);
            }

            // 消元
            for (int row = col + 1; row < n; row++)
            {
                if (Math.Abs(augmented[col, col]) < 1e-10) continue;
                double factor = augmented[row, col] / augmented[col, col];
                for (int j = col; j <= n; j++)
                {
                    augmented[row, j] -= factor * augmented[col, j];
                }
            }
        }

        // 回代
        var result = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double sum = augmented[i, n];
            for (int j = i + 1; j < n; j++)
            {
                sum -= augmented[i, j] * result[j];
            }
            result[i] = Math.Abs(augmented[i, i]) > 1e-10 ? sum / augmented[i, i] : 0;
        }

        return result;
    }

    private static double CalculateRSquared(List<(double Angle, double Current)> pairs, double[] coefficients)
    {
        var actual = pairs.Select(p => p.Current).ToList();
        var predicted = pairs.Select(p => EvaluatePolynomial(coefficients, p.Angle)).ToList();

        var mean = actual.Average();
        var ssTot = actual.Sum(y => Math.Pow(y - mean, 2));
        var ssRes = actual.Zip(predicted, (a, p) => Math.Pow(a - p, 2)).Sum();

        return ssTot > 0 ? 1 - ssRes / ssTot : 0;
    }

    private static double EvaluatePolynomial(double[] coefficients, double x)
    {
        if (coefficients.Length >= 3)
            return coefficients[0] * x * x + coefficients[1] * x + coefficients[2];
        if (coefficients.Length == 2)
            return coefficients[0] * x + coefficients[1];
        if (coefficients.Length == 1)
            return coefficients[0];
        return 0;
    }

    private static double CalculateStd(List<double> values)
    {
        if (values.Count < 2) return 0;
        var mean = values.Average();
        var sumSq = values.Sum(v => Math.Pow(v - mean, 2));
        return Math.Sqrt(sumSq / (values.Count - 1));
    }

    private static double ExtractNumericValue(TelemetryPoint p)
    {
        return p.ValueType switch
        {
            TagValueType.Float32 => p.Float32Value ?? 0,
            TagValueType.Float64 => p.Float64Value ?? 0,
            TagValueType.Int32 => p.Int32Value ?? 0,
            TagValueType.Int16 => p.Int16Value ?? 0,
            TagValueType.UInt16 => p.UInt16Value ?? 0,
            _ => 0
        };
    }

    #endregion
}
