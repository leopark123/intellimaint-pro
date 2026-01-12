using IntelliMaint.Core.Abstractions;

namespace IntelliMaint.Core.Contracts;

/// <summary>
/// v61: 标签重要性级别
/// 用于健康评估时的加权计算
/// </summary>
public enum TagImportance
{
    /// <summary>关键指标：故障直接相关（权重100）</summary>
    Critical = 100,

    /// <summary>重要指标：性能相关（权重70）</summary>
    Major = 70,

    /// <summary>次要指标：参考信息（权重40）</summary>
    Minor = 40,

    /// <summary>辅助指标：环境监测（权重20）</summary>
    Auxiliary = 20
}

/// <summary>
/// v61: 标签重要性配置
/// </summary>
public sealed record TagImportanceConfig
{
    /// <summary>配置ID（自增）</summary>
    public int Id { get; init; }

    /// <summary>匹配模式（支持通配符 *）</summary>
    public required string Pattern { get; init; }

    /// <summary>重要性级别</summary>
    public TagImportance Importance { get; init; } = TagImportance.Minor;

    /// <summary>描述</summary>
    public string? Description { get; init; }

    /// <summary>优先级（数字越大优先级越高）</summary>
    public int Priority { get; init; }

    /// <summary>是否启用</summary>
    public bool Enabled { get; init; } = true;

    public long CreatedUtc { get; init; }
    public long UpdatedUtc { get; init; }
}

/// <summary>
/// v61: 健康评估配置选项
/// </summary>
public sealed class HealthAssessmentOptions
{
    /// <summary>默认评估窗口（分钟）</summary>
    public int DefaultWindowMinutes { get; set; } = 30;

    /// <summary>基线学习时长（小时）</summary>
    public int BaselineLearningHours { get; set; } = 24;

    /// <summary>评分权重配置</summary>
    public ScoreWeights Weights { get; set; } = new();

    /// <summary>告警评分配置</summary>
    public AlarmScoreConfig AlarmScore { get; set; } = new();

    /// <summary>健康等级阈值</summary>
    public HealthLevelThresholds LevelThresholds { get; set; } = new();

    /// <summary>默认标签重要性</summary>
    public TagImportance DefaultTagImportance { get; set; } = TagImportance.Minor;

    /// <summary>v62: 动态基线配置</summary>
    public DynamicBaselineConfig DynamicBaseline { get; set; } = new();

    /// <summary>v62: 多尺度评估配置</summary>
    public MultiScaleConfig MultiScale { get; set; } = new();

    /// <summary>v63: 趋势预测配置</summary>
    public TrendPredictionConfig TrendPrediction { get; set; } = new();

    /// <summary>v63: 劣化检测配置</summary>
    public DegradationConfig Degradation { get; set; } = new();

    /// <summary>v63: RUL 预测配置</summary>
    public RulPredictionConfig RulPrediction { get; set; } = new();
}

/// <summary>
/// v61: 评分权重配置
/// </summary>
public sealed class ScoreWeights
{
    /// <summary>偏差评分权重（默认35%）</summary>
    public double Deviation { get; set; } = 0.35;

    /// <summary>趋势评分权重（默认25%）</summary>
    public double Trend { get; set; } = 0.25;

    /// <summary>稳定性评分权重（默认20%）</summary>
    public double Stability { get; set; } = 0.20;

    /// <summary>告警评分权重（默认20%）</summary>
    public double Alarm { get; set; } = 0.20;

    /// <summary>验证权重和是否为1</summary>
    public bool IsValid() => Math.Abs(Deviation + Trend + Stability + Alarm - 1.0) < 0.01;
}

/// <summary>
/// v61: 告警评分配置
/// </summary>
public sealed class AlarmScoreConfig
{
    /// <summary>Critical 告警基础扣分</summary>
    public int CriticalPenalty { get; set; } = 40;

    /// <summary>Error 告警基础扣分</summary>
    public int ErrorPenalty { get; set; } = 25;

    /// <summary>Warning 告警基础扣分</summary>
    public int WarningPenalty { get; set; } = 15;

    /// <summary>Info 告警基础扣分</summary>
    public int InfoPenalty { get; set; } = 5;

    /// <summary>是否考虑告警持续时间</summary>
    public bool ConsiderDuration { get; set; } = true;

    /// <summary>持续时间加权因子（每小时增加的百分比）</summary>
    public double DurationFactorPerHour { get; set; } = 0.1;

    /// <summary>最大持续时间加权倍数</summary>
    public double MaxDurationMultiplier { get; set; } = 1.5;

    /// <summary>最低分数（避免归零）</summary>
    public int MinScore { get; set; } = 20;
}

/// <summary>
/// v61: 健康等级阈值配置
/// </summary>
public sealed class HealthLevelThresholds
{
    /// <summary>健康状态最低分数（>=此值为Healthy）</summary>
    public int HealthyMin { get; set; } = 80;

    /// <summary>注意状态最低分数（>=此值为Attention）</summary>
    public int AttentionMin { get; set; } = 60;

    /// <summary>警告状态最低分数（>=此值为Warning）</summary>
    public int WarningMin { get; set; } = 40;

    // 低于 WarningMin 为 Critical
}

/// <summary>
/// v61: 增强的健康评分结果
/// </summary>
public sealed record EnhancedHealthScore
{
    /// <summary>设备ID</summary>
    public required string DeviceId { get; init; }

    /// <summary>计算时间戳</summary>
    public long Timestamp { get; init; }

    /// <summary>总健康指数 0-100</summary>
    public int Index { get; init; }

    /// <summary>健康等级</summary>
    public HealthLevel Level { get; init; }

    // === 分项评分 ===

    /// <summary>偏差评分 (0-100)</summary>
    public int DeviationScore { get; init; }

    /// <summary>趋势评分 (0-100)</summary>
    public int TrendScore { get; init; }

    /// <summary>稳定性评分 (0-100)</summary>
    public int StabilityScore { get; init; }

    /// <summary>告警评分 (0-100)</summary>
    public int AlarmScore { get; init; }

    // === 元数据 ===

    /// <summary>是否有有效基线</summary>
    public bool HasBaseline { get; init; }

    /// <summary>评估可信度 (0-1)</summary>
    public double Confidence { get; init; } = 1.0;

    /// <summary>可信度说明</summary>
    public string? ConfidenceReason { get; init; }

    /// <summary>样本数量</summary>
    public int SampleCount { get; init; }

    // === 问题诊断 ===

    /// <summary>问题标签列表（含权重信息）</summary>
    public IReadOnlyList<ProblemTagInfo> ProblemTags { get; init; } = Array.Empty<ProblemTagInfo>();

    /// <summary>诊断消息</summary>
    public string? DiagnosticMessage { get; init; }

    // === 告警详情 ===

    /// <summary>未关闭告警数量</summary>
    public int OpenAlarmCount { get; init; }

    /// <summary>Critical 告警数量</summary>
    public int CriticalAlarmCount { get; init; }
}

/// <summary>
/// v61: 问题标签信息
/// </summary>
public sealed record ProblemTagInfo
{
    /// <summary>标签ID</summary>
    public required string TagId { get; init; }

    /// <summary>标签重要性</summary>
    public TagImportance Importance { get; init; }

    /// <summary>问题类型</summary>
    public string? ProblemType { get; init; }

    /// <summary>问题描述</summary>
    public string? Description { get; init; }

    /// <summary>Z-Score 偏离度</summary>
    public double? ZScore { get; init; }
}

// ==================== P1: 多标签关联分析 ====================

/// <summary>
/// v62: 标签关联规则类型
/// </summary>
public enum CorrelationType
{
    /// <summary>同向变化（如温度升高+电流升高=过载）</summary>
    SameDirection,

    /// <summary>反向变化（如压力升高+流量降低=堵塞）</summary>
    OppositeDirection,

    /// <summary>阈值组合（如温度>80且振动>5=过热振动）</summary>
    ThresholdCombination
}

/// <summary>
/// v62: 标签关联规则配置
/// </summary>
public sealed record TagCorrelationRule
{
    /// <summary>规则ID</summary>
    public int Id { get; init; }

    /// <summary>规则名称</summary>
    public required string Name { get; init; }

    /// <summary>设备ID模式（支持通配符，null表示所有设备）</summary>
    public string? DevicePattern { get; init; }

    /// <summary>标签1模式</summary>
    public required string Tag1Pattern { get; init; }

    /// <summary>标签2模式</summary>
    public required string Tag2Pattern { get; init; }

    /// <summary>关联类型</summary>
    public CorrelationType CorrelationType { get; init; }

    /// <summary>关联阈值（相关系数或变化率阈值）</summary>
    public double Threshold { get; init; } = 0.7;

    /// <summary>风险描述</summary>
    public string? RiskDescription { get; init; }

    /// <summary>扣分值</summary>
    public int PenaltyScore { get; init; } = 15;

    /// <summary>是否启用</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>优先级</summary>
    public int Priority { get; init; }

    public long CreatedUtc { get; init; }
    public long UpdatedUtc { get; init; }
}

/// <summary>
/// v62: 关联异常检测结果
/// </summary>
public sealed record CorrelationAnomaly
{
    /// <summary>规则ID</summary>
    public int RuleId { get; init; }

    /// <summary>规则名称</summary>
    public required string RuleName { get; init; }

    /// <summary>标签1 ID</summary>
    public required string Tag1Id { get; init; }

    /// <summary>标签2 ID</summary>
    public required string Tag2Id { get; init; }

    /// <summary>检测到的相关系数</summary>
    public double CorrelationValue { get; init; }

    /// <summary>风险描述</summary>
    public string? RiskDescription { get; init; }

    /// <summary>扣分值</summary>
    public int PenaltyScore { get; init; }
}

// ==================== P1: 多尺度时间窗口 ====================

/// <summary>
/// v62: 多尺度评估配置
/// </summary>
public sealed class MultiScaleConfig
{
    /// <summary>是否启用多尺度评估</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>短期窗口（分钟）- 检测突发异常</summary>
    public int ShortTermMinutes { get; set; } = 5;

    /// <summary>中期窗口（分钟）- 检测渐变趋势</summary>
    public int MediumTermMinutes { get; set; } = 60;

    /// <summary>长期窗口（分钟）- 检测整体状态</summary>
    public int LongTermMinutes { get; set; } = 1440; // 24小时

    /// <summary>短期权重</summary>
    public double ShortTermWeight { get; set; } = 0.4;

    /// <summary>中期权重</summary>
    public double MediumTermWeight { get; set; } = 0.35;

    /// <summary>长期权重</summary>
    public double LongTermWeight { get; set; } = 0.25;
}

/// <summary>
/// v62: 多尺度评估结果
/// </summary>
public sealed record MultiScaleScore
{
    /// <summary>短期评分</summary>
    public int ShortTermScore { get; init; }

    /// <summary>中期评分</summary>
    public int MediumTermScore { get; init; }

    /// <summary>长期评分</summary>
    public int LongTermScore { get; init; }

    /// <summary>综合评分</summary>
    public int CompositeScore { get; init; }

    /// <summary>趋势方向（-1下降, 0稳定, 1上升）</summary>
    public int TrendDirection { get; init; }

    /// <summary>趋势描述</summary>
    public string? TrendDescription { get; init; }
}

// ==================== P1: 动态基线增量更新 ====================

/// <summary>
/// v62: 动态基线配置
/// </summary>
public sealed class DynamicBaselineConfig
{
    /// <summary>是否启用动态基线更新</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>更新间隔（小时）</summary>
    public int UpdateIntervalHours { get; set; } = 24;

    /// <summary>增量学习权重（新数据占比）</summary>
    public double IncrementalWeight { get; set; } = 0.1;

    /// <summary>异常数据过滤阈值（Z-Score）</summary>
    public double AnomalyFilterThreshold { get; set; } = 3.0;

    /// <summary>最小样本数（低于此值不更新）</summary>
    public int MinSampleCount { get; set; } = 100;

    /// <summary>基线老化系数（每天衰减比例）</summary>
    public double AgingFactor { get; set; } = 0.01;
}

// ==================== P2: 趋势预测与故障预警 ====================

/// <summary>
/// v63: 趋势预测配置
/// </summary>
public sealed class TrendPredictionConfig
{
    /// <summary>是否启用趋势预测</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>历史数据窗口（小时）- 用于训练模型</summary>
    public int HistoryWindowHours { get; set; } = 168; // 7天

    /// <summary>预测时间范围（小时）</summary>
    public int PredictionHorizonHours { get; set; } = 72; // 3天

    /// <summary>最小数据点数量（低于此值不预测）</summary>
    public int MinDataPoints { get; set; } = 100;

    /// <summary>预测更新间隔（分钟）</summary>
    public int UpdateIntervalMinutes { get; set; } = 60;

    /// <summary>趋势显著性阈值（斜率绝对值）</summary>
    public double TrendSignificanceThreshold { get; set; } = 0.01;

    /// <summary>预测置信度阈值（低于此值不生成预警）</summary>
    public double ConfidenceThreshold { get; set; } = 0.7;

    /// <summary>指数平滑系数 Alpha（0-1，越大越重视近期数据）</summary>
    public double SmoothingAlpha { get; set; } = 0.3;
}

/// <summary>
/// v63: 趋势预测结果
/// </summary>
public sealed record TrendPrediction
{
    /// <summary>设备ID</summary>
    public required string DeviceId { get; init; }

    /// <summary>标签ID</summary>
    public required string TagId { get; init; }

    /// <summary>预测时间戳</summary>
    public long PredictionTimestamp { get; init; }

    /// <summary>当前值</summary>
    public double CurrentValue { get; init; }

    /// <summary>趋势斜率（每小时变化率）</summary>
    public double TrendSlope { get; init; }

    /// <summary>趋势方向: -1=下降, 0=稳定, 1=上升</summary>
    public int TrendDirection { get; init; }

    /// <summary>预测值（在预测时间范围末端）</summary>
    public double PredictedValue { get; init; }

    /// <summary>预测置信度 (0-1)</summary>
    public double Confidence { get; init; }

    /// <summary>预计到达告警阈值的小时数（null表示不会到达）</summary>
    public double? HoursToAlarmThreshold { get; init; }

    /// <summary>相关告警规则ID（如果预计触发告警）</summary>
    public int? RelatedAlarmRuleId { get; init; }

    /// <summary>预警级别</summary>
    public PredictionAlertLevel AlertLevel { get; init; }

    /// <summary>预警消息</summary>
    public string? AlertMessage { get; init; }
}

/// <summary>
/// v63: 预警级别
/// </summary>
public enum PredictionAlertLevel
{
    /// <summary>无风险</summary>
    None = 0,

    /// <summary>低风险 - 趋势异常但距离阈值较远</summary>
    Low = 1,

    /// <summary>中风险 - 预计48-72小时内触发告警</summary>
    Medium = 2,

    /// <summary>高风险 - 预计24-48小时内触发告警</summary>
    High = 3,

    /// <summary>紧急 - 预计24小时内触发告警</summary>
    Critical = 4
}

/// <summary>
/// v63: 设备趋势预测汇总
/// </summary>
public sealed record DeviceTrendSummary
{
    /// <summary>设备ID</summary>
    public required string DeviceId { get; init; }

    /// <summary>评估时间戳</summary>
    public long Timestamp { get; init; }

    /// <summary>各标签的趋势预测</summary>
    public IReadOnlyList<TrendPrediction> TagPredictions { get; init; } = Array.Empty<TrendPrediction>();

    /// <summary>最高风险级别</summary>
    public PredictionAlertLevel MaxAlertLevel { get; init; }

    /// <summary>风险标签数量</summary>
    public int RiskTagCount { get; init; }

    /// <summary>综合风险评估</summary>
    public string? RiskSummary { get; init; }
}

/// <summary>
/// v63: 劣化检测配置
/// </summary>
public sealed class DegradationConfig
{
    /// <summary>是否启用劣化检测</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>劣化检测窗口（天）</summary>
    public int DetectionWindowDays { get; set; } = 7;

    /// <summary>劣化速率阈值（每天变化百分比）</summary>
    public double DegradationRateThreshold { get; set; } = 0.5;

    /// <summary>噪声过滤窗口（小时）- 移动平均</summary>
    public int NoiseFilterWindowHours { get; set; } = 4;

    /// <summary>连续劣化确认次数</summary>
    public int ConfirmationCount { get; set; } = 3;
}

/// <summary>
/// v63: 劣化检测结果
/// </summary>
public sealed record DegradationResult
{
    /// <summary>设备ID</summary>
    public required string DeviceId { get; init; }

    /// <summary>标签ID</summary>
    public required string TagId { get; init; }

    /// <summary>检测时间戳</summary>
    public long Timestamp { get; init; }

    /// <summary>是否检测到劣化</summary>
    public bool IsDegrading { get; init; }

    /// <summary>劣化速率（每天变化百分比）</summary>
    public double DegradationRate { get; init; }

    /// <summary>劣化类型</summary>
    public DegradationType DegradationType { get; init; }

    /// <summary>起始值</summary>
    public double StartValue { get; init; }

    /// <summary>当前值</summary>
    public double CurrentValue { get; init; }

    /// <summary>变化百分比</summary>
    public double ChangePercent { get; init; }

    /// <summary>劣化描述</summary>
    public string? Description { get; init; }
}

/// <summary>
/// v63: 劣化类型
/// </summary>
public enum DegradationType
{
    /// <summary>无劣化</summary>
    None = 0,

    /// <summary>渐进上升（如温度逐渐升高）</summary>
    GradualIncrease = 1,

    /// <summary>渐进下降（如压力逐渐降低）</summary>
    GradualDecrease = 2,

    /// <summary>波动增大（如振动越来越不稳定）</summary>
    IncreasingVariance = 3,

    /// <summary>周期异常（如周期变长/变短）</summary>
    CycleAnomaly = 4
}

// ==================== P2.4: RUL 剩余寿命预测 ====================

/// <summary>
/// v63: RUL 预测配置
/// </summary>
public sealed class RulPredictionConfig
{
    /// <summary>是否启用 RUL 预测</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>历史数据窗口（天）- 用于分析劣化趋势</summary>
    public int HistoryWindowDays { get; set; } = 30;

    /// <summary>最小数据点数量</summary>
    public int MinDataPoints { get; set; } = 500;

    /// <summary>健康指数失效阈值（低于此值认为失效）</summary>
    public int FailureThreshold { get; set; } = 30;

    /// <summary>预测置信度阈值</summary>
    public double ConfidenceThreshold { get; set; } = 0.6;

    /// <summary>劣化模型类型</summary>
    public DegradationModelType ModelType { get; set; } = DegradationModelType.Linear;

    /// <summary>最大预测天数（超过此值显示为 N/A）</summary>
    public int MaxPredictionDays { get; set; } = 365;
}

/// <summary>
/// v63: 劣化模型类型
/// </summary>
public enum DegradationModelType
{
    /// <summary>线性劣化模型</summary>
    Linear = 0,

    /// <summary>指数劣化模型</summary>
    Exponential = 1,

    /// <summary>威布尔劣化模型</summary>
    Weibull = 2
}

/// <summary>
/// v63: RUL 预测结果
/// </summary>
public sealed record RulPrediction
{
    /// <summary>设备ID</summary>
    public required string DeviceId { get; init; }

    /// <summary>预测时间戳</summary>
    public long PredictionTimestamp { get; init; }

    /// <summary>当前健康指数</summary>
    public int CurrentHealthIndex { get; init; }

    /// <summary>预测剩余寿命（小时）</summary>
    public double? RemainingUsefulLifeHours { get; init; }

    /// <summary>预测剩余寿命（天）</summary>
    public double? RemainingUsefulLifeDays { get; init; }

    /// <summary>预测失效时间（UTC 毫秒）</summary>
    public long? PredictedFailureTime { get; init; }

    /// <summary>预测置信度 (0-1)</summary>
    public double Confidence { get; init; }

    /// <summary>劣化速率（每天健康指数下降点数）</summary>
    public double DegradationRate { get; init; }

    /// <summary>使用的劣化模型</summary>
    public DegradationModelType ModelType { get; init; }

    /// <summary>RUL 状态</summary>
    public RulStatus Status { get; init; }

    /// <summary>风险等级</summary>
    public RulRiskLevel RiskLevel { get; init; }

    /// <summary>建议维护时间（UTC 毫秒）</summary>
    public long? RecommendedMaintenanceTime { get; init; }

    /// <summary>诊断消息</summary>
    public string? DiagnosticMessage { get; init; }

    /// <summary>影响因素</summary>
    public IReadOnlyList<RulFactor> Factors { get; init; } = Array.Empty<RulFactor>();
}

/// <summary>
/// v63: RUL 状态
/// </summary>
public enum RulStatus
{
    /// <summary>健康 - 无明显劣化</summary>
    Healthy = 0,

    /// <summary>正常劣化 - 按预期老化</summary>
    NormalDegradation = 1,

    /// <summary>加速劣化 - 劣化速度高于预期</summary>
    AcceleratedDegradation = 2,

    /// <summary>临近失效 - 剩余寿命不足</summary>
    NearFailure = 3,

    /// <summary>数据不足 - 无法预测</summary>
    InsufficientData = 4
}

/// <summary>
/// v63: RUL 风险等级
/// </summary>
public enum RulRiskLevel
{
    /// <summary>低风险 - RUL > 30 天</summary>
    Low = 0,

    /// <summary>中风险 - RUL 7-30 天</summary>
    Medium = 1,

    /// <summary>高风险 - RUL 1-7 天</summary>
    High = 2,

    /// <summary>紧急 - RUL < 1 天</summary>
    Critical = 3
}

/// <summary>
/// v63: RUL 影响因素
/// </summary>
public sealed record RulFactor
{
    /// <summary>因素名称（如：温度、振动）</summary>
    public required string Name { get; init; }

    /// <summary>相关标签ID</summary>
    public required string TagId { get; init; }

    /// <summary>影响权重 (0-1)</summary>
    public double Weight { get; init; }

    /// <summary>当前状态</summary>
    public string? CurrentStatus { get; init; }

    /// <summary>对 RUL 的贡献（正=延长，负=缩短）</summary>
    public double Contribution { get; init; }
}
