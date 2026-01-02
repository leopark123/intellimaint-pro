namespace IntelliMaint.Core.Contracts;

/// <summary>
/// 遥测数据点 - 统一数据契约
/// 所有协议（libplctag/OPC UA/OPC DA/Modbus）输出此结构
/// </summary>
public sealed record TelemetryPoint
{
    /// <summary>设备ID</summary>
    public required string DeviceId { get; init; }
    
    /// <summary>标签ID</summary>
    public required string TagId { get; init; }
    
    /// <summary>UTC时间戳 (epoch毫秒)</summary>
    public required long Ts { get; init; }
    
    /// <summary>同毫秒内序号 (用于排序/幂等)</summary>
    public required long Seq { get; init; }
    
    /// <summary>值类型</summary>
    public required TagValueType ValueType { get; init; }
    
    // ========== 值字段（每次只有一个有值） ==========
    
    public bool? BoolValue { get; init; }
    public sbyte? Int8Value { get; init; }
    public byte? UInt8Value { get; init; }
    public short? Int16Value { get; init; }
    public ushort? UInt16Value { get; init; }
    public int? Int32Value { get; init; }
    public uint? UInt32Value { get; init; }
    public long? Int64Value { get; init; }
    public ulong? UInt64Value { get; init; }
    public float? Float32Value { get; init; }
    public double? Float64Value { get; init; }
    public string? StringValue { get; init; }
    public byte[]? ByteArrayValue { get; init; }
    
    /// <summary>质量码 (OPC Quality, 192=Good)</summary>
    public int Quality { get; init; } = 192;
    
    /// <summary>单位</summary>
    public string? Unit { get; init; }
    
    /// <summary>数据来源 ("edge" | "cloud")</summary>
    public string Source { get; init; } = "edge";
    
    /// <summary>协议来源 ("libplctag" | "opcua" | "opcda" | "modbus")</summary>
    public string? Protocol { get; init; }
    
    // ========== 工厂方法 ==========
    
    private static long _seqCounter = 0;
    
    /// <summary>生成序号（线程安全）</summary>
    public static long GenerateSeq() => Interlocked.Increment(ref _seqCounter);
    
    /// <summary>获取当前UTC时间戳</summary>
    public static long NowTs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    
    /// <summary>创建布尔值数据点</summary>
    public static TelemetryPoint FromBool(string deviceId, string tagId, bool value, int quality = 192, string? protocol = null)
        => new()
        {
            DeviceId = deviceId,
            TagId = tagId,
            Ts = NowTs(),
            Seq = GenerateSeq(),
            ValueType = TagValueType.Bool,
            BoolValue = value,
            Quality = quality,
            Protocol = protocol
        };
    
    /// <summary>创建Int32数据点</summary>
    public static TelemetryPoint FromInt32(string deviceId, string tagId, int value, int quality = 192, string? protocol = null)
        => new()
        {
            DeviceId = deviceId,
            TagId = tagId,
            Ts = NowTs(),
            Seq = GenerateSeq(),
            ValueType = TagValueType.Int32,
            Int32Value = value,
            Quality = quality,
            Protocol = protocol
        };
    
    /// <summary>创建Float32数据点</summary>
    public static TelemetryPoint FromFloat32(string deviceId, string tagId, float value, int quality = 192, string? protocol = null)
        => new()
        {
            DeviceId = deviceId,
            TagId = tagId,
            Ts = NowTs(),
            Seq = GenerateSeq(),
            ValueType = TagValueType.Float32,
            Float32Value = value,
            Quality = quality,
            Protocol = protocol
        };
    
    /// <summary>创建Float64数据点</summary>
    public static TelemetryPoint FromFloat64(string deviceId, string tagId, double value, int quality = 192, string? protocol = null)
        => new()
        {
            DeviceId = deviceId,
            TagId = tagId,
            Ts = NowTs(),
            Seq = GenerateSeq(),
            ValueType = TagValueType.Float64,
            Float64Value = value,
            Quality = quality,
            Protocol = protocol
        };
    
    /// <summary>创建字符串数据点</summary>
    public static TelemetryPoint FromString(string deviceId, string tagId, string value, int quality = 192, string? protocol = null)
        => new()
        {
            DeviceId = deviceId,
            TagId = tagId,
            Ts = NowTs(),
            Seq = GenerateSeq(),
            ValueType = TagValueType.String,
            StringValue = value,
            Quality = quality,
            Protocol = protocol
        };
    
    /// <summary>验证类型与值是否匹配</summary>
    public bool IsValid()
    {
        return ValueType switch
        {
            TagValueType.Bool => BoolValue.HasValue,
            TagValueType.Int8 => Int8Value.HasValue,
            TagValueType.UInt8 => UInt8Value.HasValue,
            TagValueType.Int16 => Int16Value.HasValue,
            TagValueType.UInt16 => UInt16Value.HasValue,
            TagValueType.Int32 => Int32Value.HasValue,
            TagValueType.UInt32 => UInt32Value.HasValue,
            TagValueType.Int64 => Int64Value.HasValue,
            TagValueType.UInt64 => UInt64Value.HasValue,
            TagValueType.Float32 => Float32Value.HasValue,
            TagValueType.Float64 => Float64Value.HasValue,
            TagValueType.String => StringValue != null,
            TagValueType.ByteArray => ByteArrayValue != null,
            TagValueType.DateTime => Int64Value.HasValue, // DateTime 存为 epoch
            _ => false
        };
    }
    
    /// <summary>获取值的通用表示</summary>
    public object? GetValue()
    {
        return ValueType switch
        {
            TagValueType.Bool => BoolValue,
            TagValueType.Int8 => Int8Value,
            TagValueType.UInt8 => UInt8Value,
            TagValueType.Int16 => Int16Value,
            TagValueType.UInt16 => UInt16Value,
            TagValueType.Int32 => Int32Value,
            TagValueType.UInt32 => UInt32Value,
            TagValueType.Int64 => Int64Value,
            TagValueType.UInt64 => UInt64Value,
            TagValueType.Float32 => Float32Value,
            TagValueType.Float64 => Float64Value,
            TagValueType.String => StringValue,
            TagValueType.ByteArray => ByteArrayValue,
            TagValueType.DateTime => Int64Value,
            _ => null
        };
    }
}
