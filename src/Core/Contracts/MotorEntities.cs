namespace IntelliMaint.Core.Contracts;

// ========== 电机模型核心实体 ==========

/// <summary>
/// 电机类型枚举
/// </summary>
public enum MotorType
{
    InductionMotor = 0,       // 感应电机 (异步电机)
    SynchronousMotor = 1,     // 同步电机
    DCMotor = 2,              // 直流电机
    PMSyncMotor = 3,          // 永磁同步电机
    BrushlessDC = 4           // 无刷直流电机
}

/// <summary>
/// 电机标准参数类型枚举
/// </summary>
public enum MotorParameter
{
    // === 电气参数 ===
    CurrentPhaseA = 0,        // A相电流
    CurrentPhaseB = 1,        // B相电流
    CurrentPhaseC = 2,        // C相电流
    CurrentRMS = 3,           // RMS电流
    VoltagePhaseA = 4,        // A相电压
    VoltagePhaseB = 5,        // B相电压
    VoltagePhaseC = 6,        // C相电压
    VoltageRMS = 7,           // RMS电压
    Power = 8,                // 功率
    PowerFactor = 9,          // 功率因数
    Frequency = 10,           // 输出频率

    // === 机械参数 ===
    Torque = 20,              // 扭矩
    Speed = 21,               // 转速
    Position = 22,            // 位置

    // === 操作上下文参数 ===
    TiltAngle = 30,           // 翻转角度 (翻车机)
    TravelDistance = 31,      // 行程距离 (定位车)
    TravelSpeed = 32,         // 行程速度
    LoadWeight = 33,          // 负载重量
    OperationState = 34,      // 操作状态

    // === 环境参数 ===
    Temperature = 40,         // 温度
    Vibration = 41,           // 振动
    Humidity = 42             // 湿度
}

/// <summary>
/// 用户自定义电机模型
/// 用户可以创建不同类型电机的模型模板
/// </summary>
public sealed record MotorModel
{
    /// <summary>模型ID (GUID)</summary>
    public required string ModelId { get; init; }

    /// <summary>名称: "翻车机主电机", "定位车驱动电机"</summary>
    public required string Name { get; init; }

    /// <summary>描述</summary>
    public string? Description { get; init; }

    /// <summary>电机类型</summary>
    public MotorType Type { get; init; } = MotorType.InductionMotor;

    /// <summary>额定功率 (kW)</summary>
    public double? RatedPower { get; init; }

    /// <summary>额定电压 (V)</summary>
    public double? RatedVoltage { get; init; }

    /// <summary>额定电流 (A)</summary>
    public double? RatedCurrent { get; init; }

    /// <summary>额定转速 (RPM)</summary>
    public double? RatedSpeed { get; init; }

    /// <summary>额定频率 (Hz)</summary>
    public double? RatedFrequency { get; init; }

    /// <summary>极对数 (用于计算故障特征频率)</summary>
    public int? PolePairs { get; init; }

    /// <summary>变频器型号</summary>
    public string? VfdModel { get; init; }

    /// <summary>轴承型号 (用于计算轴承故障特征频率)</summary>
    public string? BearingModel { get; init; }

    /// <summary>滚动体数量 (轴承参数)</summary>
    public int? BearingRollingElements { get; init; }

    /// <summary>滚动体直径 (mm)</summary>
    public double? BearingBallDiameter { get; init; }

    /// <summary>节圆直径 (mm)</summary>
    public double? BearingPitchDiameter { get; init; }

    /// <summary>接触角 (度)</summary>
    public double? BearingContactAngle { get; init; }

    public long CreatedUtc { get; init; }
    public long? UpdatedUtc { get; init; }
    public string? CreatedBy { get; init; }
}

/// <summary>
/// 电机实例 - 绑定电机模型到具体设备
/// </summary>
public sealed record MotorInstance
{
    /// <summary>实例ID (GUID)</summary>
    public required string InstanceId { get; init; }

    /// <summary>关联电机模型</summary>
    public required string ModelId { get; init; }

    /// <summary>关联采集设备</summary>
    public required string DeviceId { get; init; }

    /// <summary>实例名称: "1#翻车机主电机"</summary>
    public required string Name { get; init; }

    /// <summary>安装位置</summary>
    public string? Location { get; init; }

    /// <summary>安装日期</summary>
    public string? InstallDate { get; init; }

    /// <summary>累计运行小时</summary>
    public double? OperatingHours { get; init; }

    /// <summary>资产编号</summary>
    public string? AssetNumber { get; init; }

    /// <summary>是否启用诊断</summary>
    public bool DiagnosisEnabled { get; init; } = true;

    public long CreatedUtc { get; init; }
    public long? UpdatedUtc { get; init; }
}

/// <summary>
/// 电机参数映射 - 将VFD采集的标签映射到标准参数
/// </summary>
public sealed record MotorParameterMapping
{
    /// <summary>映射ID (GUID)</summary>
    public required string MappingId { get; init; }

    /// <summary>关联电机实例</summary>
    public required string InstanceId { get; init; }

    /// <summary>标准参数类型</summary>
    public MotorParameter Parameter { get; init; }

    /// <summary>关联的采集标签</summary>
    public required string TagId { get; init; }

    /// <summary>缩放因子 (原始值 * ScaleFactor + Offset = 实际值)</summary>
    public double ScaleFactor { get; init; } = 1.0;

    /// <summary>偏移量</summary>
    public double Offset { get; init; } = 0.0;

    /// <summary>是否用于诊断</summary>
    public bool UsedForDiagnosis { get; init; } = true;
}

/// <summary>
/// 操作模式定义 - 不同操作对应不同的正常参数范围
/// </summary>
public sealed record OperationMode
{
    /// <summary>模式ID (GUID)</summary>
    public required string ModeId { get; init; }

    /// <summary>关联电机实例</summary>
    public required string InstanceId { get; init; }

    /// <summary>模式名称: "空载翻转", "满载翻转", "返回复位"</summary>
    public required string Name { get; init; }

    /// <summary>描述</summary>
    public string? Description { get; init; }

    /// <summary>触发标签ID (用于自动识别操作模式)</summary>
    public string? TriggerTagId { get; init; }

    /// <summary>触发范围最小值</summary>
    public double? TriggerMinValue { get; init; }

    /// <summary>触发范围最大值</summary>
    public double? TriggerMaxValue { get; init; }

    /// <summary>最小持续时间 (ms)</summary>
    public int MinDurationMs { get; init; }

    /// <summary>最大持续时间 (ms), 0=无限</summary>
    public int MaxDurationMs { get; init; }

    /// <summary>排序优先级 (用于触发条件重叠时)</summary>
    public int Priority { get; init; }

    /// <summary>是否启用</summary>
    public bool Enabled { get; init; } = true;

    public long CreatedUtc { get; init; }
    public long? UpdatedUtc { get; init; }
}

/// <summary>
/// 基线配置 - 每个操作模式下每个参数的正常范围
/// 通过学习历史数据自动生成
/// </summary>
public sealed record BaselineProfile
{
    /// <summary>基线ID (GUID)</summary>
    public required string BaselineId { get; init; }

    /// <summary>关联操作模式</summary>
    public required string ModeId { get; init; }

    /// <summary>参数类型</summary>
    public MotorParameter Parameter { get; init; }

    // === 统计基线 ===
    /// <summary>均值</summary>
    public double Mean { get; init; }

    /// <summary>标准差</summary>
    public double StdDev { get; init; }

    /// <summary>最小值</summary>
    public double MinValue { get; init; }

    /// <summary>最大值</summary>
    public double MaxValue { get; init; }

    /// <summary>5%分位数</summary>
    public double Percentile05 { get; init; }

    /// <summary>95%分位数</summary>
    public double Percentile95 { get; init; }

    /// <summary>中位数</summary>
    public double Median { get; init; }

    // === 频域基线 (FFT) ===
    /// <summary>频谱特征JSON (用于电流分析)</summary>
    public string? FrequencyProfileJson { get; init; }

    // === 学习信息 ===
    /// <summary>学习样本数</summary>
    public int SampleCount { get; init; }

    /// <summary>学习开始时间</summary>
    public long LearnedFromUtc { get; init; }

    /// <summary>学习结束时间</summary>
    public long LearnedToUtc { get; init; }

    /// <summary>置信度 0-1</summary>
    public double ConfidenceLevel { get; init; }

    /// <summary>基线版本 (每次重新学习递增)</summary>
    public int Version { get; init; } = 1;

    public long CreatedUtc { get; init; }
    public long? UpdatedUtc { get; init; }
}

/// <summary>
/// 频域特征配置 (存储为JSON)
/// </summary>
public sealed record FrequencyProfile
{
    /// <summary>基频 (Hz)</summary>
    public double FundamentalFreq { get; init; }

    /// <summary>基频幅值</summary>
    public double FundamentalAmplitude { get; init; }

    /// <summary>谐波幅值 [2次, 3次, ...10次]</summary>
    public double[] HarmonicAmplitudes { get; init; } = Array.Empty<double>();

    /// <summary>总谐波畸变 (THD%)</summary>
    public double TotalHarmonicDistortion { get; init; }

    /// <summary>轴承故障特征频率幅值</summary>
    public BearingFaultAmplitudes? BearingAmplitudes { get; init; }

    /// <summary>噪声底 (dB)</summary>
    public double NoiseFloor { get; init; }

    /// <summary>频谱能量</summary>
    public double SpectralEnergy { get; init; }
}

/// <summary>
/// 轴承故障特征频率幅值
/// </summary>
public sealed record BearingFaultAmplitudes
{
    /// <summary>外圈故障频率(BPFO)幅值</summary>
    public double BPFO { get; init; }

    /// <summary>内圈故障频率(BPFI)幅值</summary>
    public double BPFI { get; init; }

    /// <summary>滚动体故障频率(BSF)幅值</summary>
    public double BSF { get; init; }

    /// <summary>保持架故障频率(FTF)幅值</summary>
    public double FTF { get; init; }
}

// ========== 请求/响应 DTO ==========

/// <summary>
/// 创建电机模型请求
/// </summary>
public sealed record CreateMotorModelRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public MotorType Type { get; init; } = MotorType.InductionMotor;
    public double? RatedPower { get; init; }
    public double? RatedVoltage { get; init; }
    public double? RatedCurrent { get; init; }
    public double? RatedSpeed { get; init; }
    public double? RatedFrequency { get; init; }
    public int? PolePairs { get; init; }
    public string? VfdModel { get; init; }
    public string? BearingModel { get; init; }
    public int? BearingRollingElements { get; init; }
    public double? BearingBallDiameter { get; init; }
    public double? BearingPitchDiameter { get; init; }
    public double? BearingContactAngle { get; init; }
}

/// <summary>
/// 创建电机实例请求
/// </summary>
public sealed record CreateMotorInstanceRequest
{
    public required string ModelId { get; init; }
    public required string DeviceId { get; init; }
    public required string Name { get; init; }
    public string? Location { get; init; }
    public string? InstallDate { get; init; }
    public string? AssetNumber { get; init; }
}

/// <summary>
/// 创建参数映射请求
/// </summary>
public sealed record CreateParameterMappingRequest
{
    public MotorParameter Parameter { get; init; }
    public required string TagId { get; init; }
    public double ScaleFactor { get; init; } = 1.0;
    public double Offset { get; init; } = 0.0;
    public bool UsedForDiagnosis { get; init; } = true;
}

/// <summary>
/// 创建操作模式请求
/// </summary>
public sealed record CreateOperationModeRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? TriggerTagId { get; init; }
    public double? TriggerMinValue { get; init; }
    public double? TriggerMaxValue { get; init; }
    public int MinDurationMs { get; init; }
    public int MaxDurationMs { get; init; }
    public int Priority { get; init; }
}

/// <summary>
/// 基线学习请求
/// </summary>
public sealed record LearnBaselineRequest
{
    public required string ModeId { get; init; }
    public long StartTs { get; init; }
    public long EndTs { get; init; }
    public int MinSamples { get; init; } = 1000;
}

/// <summary>
/// 基线学习结果
/// </summary>
public sealed record BaselineLearningResult
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public int LearnedParameters { get; init; }
    public int TotalSamples { get; init; }
    public double AverageConfidence { get; init; }
    public IReadOnlyList<BaselineProfile> Baselines { get; init; } = Array.Empty<BaselineProfile>();
}

/// <summary>
/// 电机实例详情 (包含关联数据)
/// </summary>
public sealed record MotorInstanceDetail
{
    public required MotorInstance Instance { get; init; }
    public MotorModel? Model { get; init; }
    public IReadOnlyList<MotorParameterMapping> Mappings { get; init; } = Array.Empty<MotorParameterMapping>();
    public IReadOnlyList<OperationMode> Modes { get; init; } = Array.Empty<OperationMode>();
    public int BaselineCount { get; init; }
}

// ========== Phase 3: 故障检测实体 ==========

/// <summary>
/// 电机故障类型枚举
/// </summary>
public enum MotorFaultType
{
    // === 电气故障 ===
    /// <summary>相间不平衡</summary>
    PhaseImbalance = 0,
    /// <summary>过电流</summary>
    Overcurrent = 1,
    /// <summary>欠电流</summary>
    Undercurrent = 2,
    /// <summary>过电压</summary>
    Overvoltage = 3,
    /// <summary>欠电压</summary>
    Undervoltage = 4,
    /// <summary>功率因数异常</summary>
    PowerFactorAbnormal = 5,
    /// <summary>谐波异常</summary>
    HarmonicAbnormal = 6,

    // === 机械故障 ===
    /// <summary>转子偏心</summary>
    RotorEccentricity = 10,
    /// <summary>转子断条</summary>
    BrokenRotorBar = 11,
    /// <summary>轴承外圈故障</summary>
    BearingOuterRace = 12,
    /// <summary>轴承内圈故障</summary>
    BearingInnerRace = 13,
    /// <summary>轴承滚动体故障</summary>
    BearingBall = 14,
    /// <summary>轴承保持架故障</summary>
    BearingCage = 15,
    /// <summary>轴不对中</summary>
    Misalignment = 16,
    /// <summary>机械松动</summary>
    MechanicalLooseness = 17,

    // === 热故障 ===
    /// <summary>过热</summary>
    Overheating = 20,
    /// <summary>绝缘老化</summary>
    InsulationDegradation = 21,

    // === 操作异常 ===
    /// <summary>过载</summary>
    Overload = 30,
    /// <summary>频繁启停</summary>
    FrequentStartStop = 31,
    /// <summary>参数偏移</summary>
    ParameterDrift = 32,

    // === 未知/综合 ===
    /// <summary>未知故障</summary>
    Unknown = 99
}

/// <summary>
/// 故障严重程度
/// </summary>
public enum FaultSeverity
{
    /// <summary>正常</summary>
    Normal = 0,
    /// <summary>轻微偏离（关注）</summary>
    Minor = 1,
    /// <summary>中度偏离（警告）</summary>
    Moderate = 2,
    /// <summary>严重偏离（报警）</summary>
    Severe = 3,
    /// <summary>危急（立即处理）</summary>
    Critical = 4
}

/// <summary>
/// 诊断结果记录
/// </summary>
public sealed record MotorDiagnosisResult
{
    /// <summary>诊断ID</summary>
    public required string DiagnosisId { get; init; }

    /// <summary>电机实例ID</summary>
    public required string InstanceId { get; init; }

    /// <summary>操作模式ID</summary>
    public required string ModeId { get; init; }

    /// <summary>诊断时间戳</summary>
    public long Timestamp { get; init; }

    /// <summary>综合健康得分 0-100</summary>
    public double HealthScore { get; init; }

    /// <summary>检测到的故障列表</summary>
    public IReadOnlyList<DetectedFault> Faults { get; init; } = Array.Empty<DetectedFault>();

    /// <summary>参数偏离详情</summary>
    public IReadOnlyList<ParameterDeviation> Deviations { get; init; } = Array.Empty<ParameterDeviation>();

    /// <summary>诊断摘要</summary>
    public string? Summary { get; init; }

    /// <summary>建议措施</summary>
    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();

    /// <summary>是否已生成告警</summary>
    public bool AlarmGenerated { get; init; }

    /// <summary>关联告警ID</summary>
    public string? AlarmId { get; init; }
}

/// <summary>
/// 检测到的故障
/// </summary>
public sealed record DetectedFault
{
    /// <summary>故障类型</summary>
    public MotorFaultType FaultType { get; init; }

    /// <summary>严重程度</summary>
    public FaultSeverity Severity { get; init; }

    /// <summary>置信度 0-100</summary>
    public double Confidence { get; init; }

    /// <summary>故障描述</summary>
    public string? Description { get; init; }

    /// <summary>相关参数</summary>
    public MotorParameter? RelatedParameter { get; init; }

    /// <summary>当前值</summary>
    public double? CurrentValue { get; init; }

    /// <summary>基线值</summary>
    public double? BaselineValue { get; init; }

    /// <summary>偏离程度（标准差倍数）</summary>
    public double? DeviationSigma { get; init; }
}

/// <summary>
/// 参数偏离详情
/// </summary>
public sealed record ParameterDeviation
{
    /// <summary>参数类型</summary>
    public MotorParameter Parameter { get; init; }

    /// <summary>参数名称（用于前端显示）</summary>
    public string ParameterName => GetParameterName(Parameter);

    /// <summary>当前值</summary>
    public double CurrentValue { get; init; }

    private static string GetParameterName(MotorParameter p) => p switch
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
        _ => p.ToString()
    };

    /// <summary>基线均值</summary>
    public double BaselineMean { get; init; }

    /// <summary>基线标准差</summary>
    public double BaselineStdDev { get; init; }

    /// <summary>偏离度（标准差倍数）</summary>
    public double DeviationSigma { get; init; }

    /// <summary>偏离方向：1=高于，-1=低于，0=正常</summary>
    public int Direction { get; init; }

    /// <summary>是否超出阈值</summary>
    public bool IsAbnormal { get; init; }
}

/// <summary>
/// 诊断历史记录（用于存储）
/// </summary>
public sealed record MotorDiagnosisHistory
{
    /// <summary>记录ID</summary>
    public required string Id { get; init; }

    /// <summary>电机实例ID</summary>
    public required string InstanceId { get; init; }

    /// <summary>诊断时间戳</summary>
    public long Timestamp { get; init; }

    /// <summary>操作模式ID</summary>
    public string? ModeId { get; init; }

    /// <summary>综合健康得分</summary>
    public double HealthScore { get; init; }

    /// <summary>最高严重程度</summary>
    public FaultSeverity MaxSeverity { get; init; }

    /// <summary>故障数量</summary>
    public int FaultCount { get; init; }

    /// <summary>故障类型JSON</summary>
    public string? FaultsJson { get; init; }

    /// <summary>偏离详情JSON</summary>
    public string? DeviationsJson { get; init; }

    /// <summary>诊断摘要</summary>
    public string? Summary { get; init; }

    /// <summary>关联告警ID</summary>
    public string? AlarmId { get; init; }
}

/// <summary>
/// 故障检测配置
/// </summary>
public sealed record FaultDetectionConfig
{
    /// <summary>轻微偏离阈值（标准差倍数）</summary>
    public double MinorThreshold { get; init; } = 2.0;

    /// <summary>中度偏离阈值</summary>
    public double ModerateThreshold { get; init; } = 3.0;

    /// <summary>严重偏离阈值</summary>
    public double SevereThreshold { get; init; } = 4.0;

    /// <summary>危急偏离阈值</summary>
    public double CriticalThreshold { get; init; } = 5.0;

    /// <summary>相间不平衡阈值（%）</summary>
    public double PhaseImbalanceThreshold { get; init; } = 5.0;

    /// <summary>轴承故障频率幅值增益阈值（倍数）</summary>
    public double BearingFaultGainThreshold { get; init; } = 3.0;

    /// <summary>边带频率幅值增益阈值（倍数）</summary>
    public double SidebandGainThreshold { get; init; } = 2.0;

    /// <summary>谐波畸变阈值（%）</summary>
    public double ThdThreshold { get; init; } = 10.0;

    /// <summary>最小置信度（低于此值不报告故障）</summary>
    public double MinConfidence { get; init; } = 60.0;

    /// <summary>是否启用告警生成</summary>
    public bool EnableAlarmGeneration { get; init; } = true;

    /// <summary>告警生成的最低严重程度</summary>
    public FaultSeverity MinAlarmSeverity { get; init; } = FaultSeverity.Moderate;
}
