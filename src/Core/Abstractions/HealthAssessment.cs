namespace IntelliMaint.Core.Abstractions;

/// <summary>
/// v45: 特征提取器接口
/// 从遥测数据中提取统计特征
/// </summary>
public interface IFeatureExtractor
{
    /// <summary>
    /// 提取设备特征
    /// </summary>
    /// <param name="deviceId">设备 ID</param>
    /// <param name="windowMinutes">时间窗口（分钟）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>设备特征集合</returns>
    Task<DeviceFeatures?> ExtractAsync(string deviceId, int windowMinutes, CancellationToken ct);
    
    /// <summary>
    /// 批量提取所有设备特征
    /// </summary>
    Task<IReadOnlyList<DeviceFeatures>> ExtractAllAsync(int windowMinutes, CancellationToken ct);
}

/// <summary>
/// v45: 健康评分计算器接口
/// </summary>
public interface IHealthScoreCalculator
{
    /// <summary>
    /// 计算设备健康指数
    /// </summary>
    /// <param name="features">设备特征</param>
    /// <param name="baseline">设备基线（可选）</param>
    /// <returns>健康指数 0-100</returns>
    HealthScore Calculate(DeviceFeatures features, DeviceBaseline? baseline);
}

/// <summary>
/// v45: 健康基线仓储接口
/// </summary>
public interface IHealthBaselineRepository
{
    /// <summary>获取设备基线</summary>
    Task<DeviceBaseline?> GetAsync(string deviceId, CancellationToken ct);
    
    /// <summary>保存设备基线</summary>
    Task SaveAsync(DeviceBaseline baseline, CancellationToken ct);
    
    /// <summary>删除设备基线</summary>
    Task DeleteAsync(string deviceId, CancellationToken ct);
    
    /// <summary>获取所有基线</summary>
    Task<IReadOnlyList<DeviceBaseline>> ListAsync(CancellationToken ct);
}

/// <summary>
/// v45: 设备特征集合
/// </summary>
public sealed record DeviceFeatures
{
    /// <summary>设备 ID</summary>
    public required string DeviceId { get; init; }
    
    /// <summary>计算时间戳</summary>
    public long Timestamp { get; init; }
    
    /// <summary>窗口时长（分钟）</summary>
    public int WindowMinutes { get; init; }
    
    /// <summary>数据点数量</summary>
    public int SampleCount { get; init; }
    
    /// <summary>各标签的特征</summary>
    public IReadOnlyDictionary<string, TagFeatures> TagFeatures { get; init; } 
        = new Dictionary<string, TagFeatures>();
}

/// <summary>
/// v45: 单个标签的统计特征
/// </summary>
public sealed record TagFeatures
{
    /// <summary>标签 ID</summary>
    public required string TagId { get; init; }
    
    /// <summary>数据点数量</summary>
    public int Count { get; init; }
    
    // === 基础统计 ===
    
    /// <summary>均值</summary>
    public double Mean { get; init; }
    
    /// <summary>标准差</summary>
    public double StdDev { get; init; }
    
    /// <summary>最小值</summary>
    public double Min { get; init; }
    
    /// <summary>最大值</summary>
    public double Max { get; init; }
    
    /// <summary>最新值</summary>
    public double Latest { get; init; }
    
    // === 趋势特征 ===
    
    /// <summary>趋势斜率（线性回归）</summary>
    public double TrendSlope { get; init; }
    
    /// <summary>趋势方向: -1=下降, 0=平稳, 1=上升</summary>
    public int TrendDirection { get; init; }
    
    // === 稳定性特征 ===
    
    /// <summary>变异系数 (StdDev / Mean)</summary>
    public double CoefficientOfVariation { get; init; }
    
    /// <summary>极差 (Max - Min)</summary>
    public double Range { get; init; }
}

/// <summary>
/// v45: 设备基线数据
/// 记录设备正常运行时的特征统计
/// </summary>
public sealed record DeviceBaseline
{
    /// <summary>设备 ID</summary>
    public required string DeviceId { get; init; }
    
    /// <summary>基线创建时间</summary>
    public long CreatedUtc { get; init; }
    
    /// <summary>基线更新时间</summary>
    public long UpdatedUtc { get; init; }
    
    /// <summary>学习样本数量</summary>
    public int SampleCount { get; init; }
    
    /// <summary>学习时长（小时）</summary>
    public int LearningHours { get; init; }
    
    /// <summary>各标签的基线</summary>
    public IReadOnlyDictionary<string, TagBaseline> TagBaselines { get; init; }
        = new Dictionary<string, TagBaseline>();
}

/// <summary>
/// v45: 单个标签的基线数据
/// </summary>
public sealed record TagBaseline
{
    /// <summary>标签 ID</summary>
    public required string TagId { get; init; }
    
    /// <summary>正常均值</summary>
    public double NormalMean { get; init; }
    
    /// <summary>正常标准差</summary>
    public double NormalStdDev { get; init; }
    
    /// <summary>正常最小值</summary>
    public double NormalMin { get; init; }
    
    /// <summary>正常最大值</summary>
    public double NormalMax { get; init; }
    
    /// <summary>正常变异系数</summary>
    public double NormalCV { get; init; }
}

/// <summary>
/// v45: 健康评分结果
/// </summary>
public sealed record HealthScore
{
    /// <summary>设备 ID</summary>
    public required string DeviceId { get; init; }
    
    /// <summary>计算时间戳</summary>
    public long Timestamp { get; init; }
    
    /// <summary>总健康指数 0-100</summary>
    public int Index { get; init; }
    
    /// <summary>健康等级</summary>
    public HealthLevel Level { get; init; }
    
    /// <summary>偏差评分 (0-100)</summary>
    public int DeviationScore { get; init; }
    
    /// <summary>趋势评分 (0-100)</summary>
    public int TrendScore { get; init; }
    
    /// <summary>稳定性评分 (0-100)</summary>
    public int StabilityScore { get; init; }
    
    /// <summary>告警评分 (0-100)</summary>
    public int AlarmScore { get; init; }
    
    /// <summary>是否有有效基线</summary>
    public bool HasBaseline { get; init; }
    
    /// <summary>问题标签列表</summary>
    public IReadOnlyList<string> ProblemTags { get; init; } = Array.Empty<string>();
    
    /// <summary>诊断消息</summary>
    public string? DiagnosticMessage { get; init; }
}

/// <summary>
/// v45: 健康等级
/// </summary>
public enum HealthLevel
{
    /// <summary>健康 (80-100)</summary>
    Healthy = 0,
    
    /// <summary>注意 (60-79)</summary>
    Attention = 1,
    
    /// <summary>警告 (40-59)</summary>
    Warning = 2,
    
    /// <summary>危险 (0-39)</summary>
    Critical = 3
}
