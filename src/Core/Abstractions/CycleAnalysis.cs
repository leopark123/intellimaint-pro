using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Core.Abstractions;

/// <summary>
/// 周期分析服务接口
/// </summary>
public interface ICycleAnalysisService
{
    /// <summary>
    /// 分析指定时间范围内的工作周期
    /// </summary>
    Task<CycleAnalysisResult> AnalyzeCyclesAsync(
        CycleAnalysisRequest request, 
        CancellationToken ct);
    
    /// <summary>
    /// 分析单个采集片段中的周期
    /// </summary>
    Task<CycleAnalysisResult> AnalyzeSegmentAsync(
        long segmentId,
        CycleAnalysisRequest request,
        CancellationToken ct);
    
    /// <summary>
    /// 检测数据中的周期边界
    /// </summary>
    Task<IReadOnlyList<(long StartUtc, long EndUtc)>> DetectCycleBoundariesAsync(
        string deviceId,
        string angleTagId,
        long startTimeUtc,
        long endTimeUtc,
        double angleThreshold,
        CancellationToken ct);
}

/// <summary>
/// 基线学习服务接口
/// </summary>
public interface IBaselineLearningService
{
    /// <summary>
    /// 学习电流-角度基线模型
    /// </summary>
    Task<CurrentAngleModel> LearnCurrentAngleModelAsync(
        string deviceId,
        string angleTagId,
        string currentTagId,
        long startTimeUtc,
        long endTimeUtc,
        CancellationToken ct);
    
    /// <summary>
    /// 学习电机平衡基线
    /// </summary>
    Task<MotorBalanceModel> LearnMotorBalanceModelAsync(
        string deviceId,
        string motor1TagId,
        string motor2TagId,
        long startTimeUtc,
        long endTimeUtc,
        CancellationToken ct);
    
    /// <summary>
    /// 学习周期时长基线
    /// </summary>
    Task<CycleDurationModel> LearnCycleDurationModelAsync(
        string deviceId,
        IEnumerable<WorkCycle> cycles,
        CancellationToken ct);
    
    /// <summary>
    /// 从历史数据自动学习所有基线
    /// </summary>
    Task LearnAllBaselinesAsync(
        CycleAnalysisRequest config,
        long learningStartUtc,
        long learningEndUtc,
        CancellationToken ct);
}

/// <summary>
/// 异常检测服务接口
/// </summary>
public interface IAnomalyDetectionService
{
    /// <summary>
    /// 计算周期的异常分数
    /// </summary>
    Task<(double Score, bool IsAnomaly, string? AnomalyType)> EvaluateCycleAsync(
        WorkCycle cycle,
        string deviceId,
        CancellationToken ct);
    
    /// <summary>
    /// 检测实时数据中的异常
    /// </summary>
    Task<IReadOnlyList<AnomalyAlert>> DetectRealtimeAnomaliesAsync(
        string deviceId,
        IReadOnlyList<TelemetryPoint> recentData,
        CancellationToken ct);
}

/// <summary>
/// 实时异常告警
/// </summary>
public sealed record AnomalyAlert
{
    public required string DeviceId { get; init; }
    public required string AnomalyType { get; init; }
    public required double Severity { get; init; }  // 1-5
    public required string Message { get; init; }
    public required long DetectedUtc { get; init; }
    public Dictionary<string, double>? Details { get; init; }
}
