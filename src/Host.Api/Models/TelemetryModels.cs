namespace IntelliMaint.Host.Api.Models;

/// <summary>
/// 查询请求参数
/// </summary>
public sealed record TelemetryQueryRequest
{
    public string? DeviceId { get; init; }
    public string? TagId { get; init; }
    public long? StartTs { get; init; }     // Unix 毫秒
    public long? EndTs { get; init; }       // Unix 毫秒
    public int Limit { get; init; } = 1000; // 默认 1000，最大 10000
    
    // v48: 游标分页支持
    public long? CursorTs { get; init; }    // 游标时间戳（上一页最后一条的 ts）
    public int? CursorSeq { get; init; }    // 游标序号（上一页最后一条的 seq）
}

/// <summary>
/// 聚合请求参数
/// </summary>
public sealed record AggregateQueryRequest
{
    public string? DeviceId { get; init; }
    public string? TagId { get; init; }
    public long StartTs { get; init; }
    public long EndTs { get; init; }
    public int IntervalMs { get; init; } = 60000;  // 默认 1 分钟
    public string Function { get; init; } = "avg"; // avg, min, max, sum, count
}

/// <summary>
/// 通用响应包装
/// </summary>
public sealed record ApiResponse<T>
{
    public bool Success { get; init; } = true;
    public T? Data { get; init; }
    public string? Error { get; init; }
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

/// <summary>
/// v48: 分页响应包装
/// </summary>
public sealed record PagedApiResponse<T>
{
    public bool Success { get; init; } = true;
    public T? Data { get; init; }
    public string? Error { get; init; }
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    
    // 分页信息
    public int Count { get; init; }
    public bool HasMore { get; init; }
    public string? NextCursor { get; init; }  // 格式: "ts:seq"
}

/// <summary>
/// 遥测数据响应（简化版，用于 API 返回）
/// </summary>
public sealed record TelemetryDataPoint
{
    public required string DeviceId { get; init; }
    public required string TagId { get; init; }
    public required long Ts { get; init; }
    public required long Seq { get; init; }  // v48: 添加序号用于游标分页
    public required object? Value { get; init; }    // 实际值（根据类型）
    public required string ValueType { get; init; } // 类型名称
    public required int Quality { get; init; }
    public string? Unit { get; init; }
}
