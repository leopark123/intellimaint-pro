namespace IntelliMaint.Core.Contracts;

// ========================================
// 数据分析相关实体 (v47)
// ========================================

/// <summary>
/// 工作周期记录
/// </summary>
public sealed record WorkCycle
{
    /// <summary>周期ID (自增)</summary>
    public long Id { get; init; }
    
    /// <summary>设备ID</summary>
    public required string DeviceId { get; init; }
    
    /// <summary>采集片段ID (可选关联)</summary>
    public long? SegmentId { get; init; }
    
    /// <summary>周期开始时间 (Unix毫秒)</summary>
    public required long StartTimeUtc { get; init; }
    
    /// <summary>周期结束时间 (Unix毫秒)</summary>
    public required long EndTimeUtc { get; init; }
    
    /// <summary>周期时长 (秒)</summary>
    public double DurationSeconds { get; init; }
    
    /// <summary>最大角度</summary>
    public double MaxAngle { get; init; }
    
    /// <summary>电机1峰值电流</summary>
    public double Motor1PeakCurrent { get; init; }
    
    /// <summary>电机2峰值电流</summary>
    public double Motor2PeakCurrent { get; init; }
    
    /// <summary>电机1平均电流</summary>
    public double Motor1AvgCurrent { get; init; }
    
    /// <summary>电机2平均电流</summary>
    public double Motor2AvgCurrent { get; init; }
    
    /// <summary>电机1能耗 (电流积分)</summary>
    public double Motor1Energy { get; init; }
    
    /// <summary>电机2能耗</summary>
    public double Motor2Energy { get; init; }
    
    /// <summary>左右电机电流比 (Motor1/Motor2)</summary>
    public double MotorBalanceRatio { get; init; }
    
    /// <summary>与基线偏差百分比</summary>
    public double BaselineDeviationPercent { get; init; }
    
    /// <summary>异常分数 (0-100, 越高越异常)</summary>
    public double AnomalyScore { get; init; }
    
    /// <summary>是否标记为异常</summary>
    public bool IsAnomaly { get; init; }
    
    /// <summary>异常类型 (如果是异常)</summary>
    public string? AnomalyType { get; init; }
    
    /// <summary>详细数据 (JSON)</summary>
    public string? DetailsJson { get; init; }
    
    /// <summary>创建时间</summary>
    public long CreatedUtc { get; init; }
}

/// <summary>
/// 周期分析基线模型
/// </summary>
public sealed record CycleDeviceBaseline
{
    /// <summary>设备ID</summary>
    public required string DeviceId { get; init; }
    
    /// <summary>基线类型 (current_angle, motor_balance, cycle_duration)</summary>
    public required string BaselineType { get; init; }
    
    /// <summary>学习样本数</summary>
    public int SampleCount { get; init; }
    
    /// <summary>最后更新时间</summary>
    public long UpdatedUtc { get; init; }
    
    /// <summary>模型参数 (JSON)</summary>
    public required string ModelJson { get; init; }
    
    /// <summary>统计信息 (JSON)</summary>
    public string? StatsJson { get; init; }
}

/// <summary>
/// 电流-角度基线模型参数
/// </summary>
public sealed record CurrentAngleModel
{
    /// <summary>电机标签ID</summary>
    public required string MotorTagId { get; init; }
    
    /// <summary>多项式系数 [a, b, c] for ax² + bx + c</summary>
    public required double[] Coefficients { get; init; }
    
    /// <summary>拟合R²</summary>
    public double RSquared { get; init; }
    
    /// <summary>各角度的正常电流范围</summary>
    public Dictionary<int, CurrentRange>? AngleRanges { get; init; }
}

/// <summary>
/// 电流范围
/// </summary>
public sealed record CurrentRange
{
    public double Mean { get; init; }
    public double Std { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
}

/// <summary>
/// 电机平衡基线
/// </summary>
public sealed record MotorBalanceModel
{
    /// <summary>平均电流比</summary>
    public double MeanRatio { get; init; }
    
    /// <summary>标准差</summary>
    public double StdRatio { get; init; }
    
    /// <summary>正常范围下限</summary>
    public double LowerBound { get; init; }
    
    /// <summary>正常范围上限</summary>
    public double UpperBound { get; init; }
}

/// <summary>
/// 周期时长基线
/// </summary>
public sealed record CycleDurationModel
{
    /// <summary>平均时长 (秒)</summary>
    public double MeanDuration { get; init; }
    
    /// <summary>标准差</summary>
    public double StdDuration { get; init; }
    
    /// <summary>正常范围下限</summary>
    public double LowerBound { get; init; }
    
    /// <summary>正常范围上限</summary>
    public double UpperBound { get; init; }
}

/// <summary>
/// 周期分析请求
/// </summary>
public sealed record CycleAnalysisRequest
{
    /// <summary>设备ID</summary>
    public required string DeviceId { get; init; }
    
    /// <summary>角度标签ID</summary>
    public required string AngleTagId { get; init; }
    
    /// <summary>电机1电流标签ID</summary>
    public required string Motor1CurrentTagId { get; init; }
    
    /// <summary>电机2电流标签ID</summary>
    public required string Motor2CurrentTagId { get; init; }
    
    /// <summary>分析时间范围开始</summary>
    public long StartTimeUtc { get; init; }
    
    /// <summary>分析时间范围结束</summary>
    public long EndTimeUtc { get; init; }
    
    /// <summary>周期检测角度阈值 (默认5°)</summary>
    public double AngleThreshold { get; init; } = 5.0;
    
    /// <summary>最小周期时长 (秒)</summary>
    public double MinCycleDuration { get; init; } = 20.0;
    
    /// <summary>最大周期时长 (秒)</summary>
    public double MaxCycleDuration { get; init; } = 300.0;
}

/// <summary>
/// 周期分析结果
/// </summary>
public sealed record CycleAnalysisResult
{
    /// <summary>检测到的周期数</summary>
    public int CycleCount { get; init; }
    
    /// <summary>异常周期数</summary>
    public int AnomalyCycleCount { get; init; }
    
    /// <summary>周期列表</summary>
    public required List<WorkCycle> Cycles { get; init; }
    
    /// <summary>统计摘要</summary>
    public CycleStatsSummary? Summary { get; init; }
}

/// <summary>
/// 周期统计摘要
/// </summary>
public sealed record CycleStatsSummary
{
    /// <summary>平均周期时长</summary>
    public double AvgDuration { get; init; }
    
    /// <summary>平均电机1峰值电流</summary>
    public double AvgMotor1PeakCurrent { get; init; }
    
    /// <summary>平均电机2峰值电流</summary>
    public double AvgMotor2PeakCurrent { get; init; }
    
    /// <summary>平均电机平衡比</summary>
    public double AvgMotorBalanceRatio { get; init; }
    
    /// <summary>平均异常分数</summary>
    public double AvgAnomalyScore { get; init; }
}

/// <summary>
/// 异常类型枚举
/// </summary>
public static class AnomalyTypes
{
    public const string OverCurrent = "over_current";           // 过电流
    public const string MotorImbalance = "motor_imbalance";     // 电机不平衡
    public const string CycleTimeout = "cycle_timeout";         // 周期超时
    public const string CycleTooShort = "cycle_too_short";      // 周期过短
    public const string BaselineDeviation = "baseline_deviation"; // 基线偏离
    public const string AngleStall = "angle_stall";             // 角度停滞
}

/// <summary>
/// 周期查询参数
/// </summary>
public sealed record WorkCycleQuery
{
    public string? DeviceId { get; init; }
    public long? StartTimeUtc { get; init; }
    public long? EndTimeUtc { get; init; }
    public bool? IsAnomaly { get; init; }
    public string? AnomalyType { get; init; }
    public int Limit { get; init; } = 100;
}
