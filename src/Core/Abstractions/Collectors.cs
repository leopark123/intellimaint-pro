using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Core.Abstractions;

/// <summary>
/// 采集器生命周期接口
/// </summary>
public interface ICollector
{
    /// <summary>协议标识 ("libplctag" | "opcua" | "opcda" | "modbus")</summary>
    string Protocol { get; }
    
    /// <summary>启动采集器</summary>
    Task StartAsync(CancellationToken ct);
    
    /// <summary>停止采集器（优雅关闭，等待当前读取完成）</summary>
    Task StopAsync(CancellationToken ct);
    
    /// <summary>获取健康状态（快速读取，避免await）</summary>
    CollectorHealth GetHealth();
}

/// <summary>
/// 遥测数据源接口 - 统一输出 TelemetryPoint
/// </summary>
public interface ITelemetrySource
{
    /// <summary>协议标识</summary>
    string Protocol { get; }
    
    /// <summary>
    /// 持续读取数据点
    /// 由上层 Channel 控制背压
    /// </summary>
    IAsyncEnumerable<TelemetryPoint> ReadAsync(CancellationToken ct);
}

/// <summary>
/// 类型映射器接口 - 协议类型到 TagValueType 的严格映射
/// </summary>
public interface ITagTypeMapper
{
    /// <summary>
    /// 映射协议原始类型到 TagValueType
    /// </summary>
    /// <param name="protocol">协议标识</param>
    /// <param name="tagId">标签ID</param>
    /// <param name="tagTypeHint">类型提示（如 CIP 类型、OPC 类型）</param>
    /// <param name="rawValue">原始值（用于推断）</param>
    /// <returns>对应的 TagValueType</returns>
    TagValueType MapType(string protocol, string tagId, string? tagTypeHint, object? rawValue);
    
    /// <summary>
    /// 将原始值映射到 TelemetryPoint
    /// 类型不匹配时抛出异常（Fail Fast）
    /// </summary>
    TelemetryPoint MapValue(
        string deviceId,
        string tagId,
        TagValueType expectedType,
        object rawValue,
        int quality = 192,
        string? protocol = null);
}

/// <summary>
/// 系统时钟接口 - 便于测试
/// </summary>
public interface ISystemClock
{
    /// <summary>当前UTC时间戳（毫秒）</summary>
    long UtcNowMs { get; }
    
    /// <summary>当前UTC时间</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// 默认系统时钟实现
/// </summary>
public sealed class SystemClock : ISystemClock
{
    public long UtcNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
