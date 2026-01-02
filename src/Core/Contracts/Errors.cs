namespace IntelliMaint.Core.Contracts;

/// <summary>
/// 统一错误码
/// </summary>
public static class ErrorCodes
{
    // ========== 认证错误 (1xxx) ==========
    public const string AuthInvalidApiKey = "E_AUTH_INVALID_APIKEY";
    public const string AuthExpired = "E_AUTH_EXPIRED";
    public const string AuthForbidden = "E_AUTH_FORBIDDEN";
    
    // ========== 数据库错误 (2xxx) ==========
    public const string DbUnavailable = "E_DB_UNAVAILABLE";
    public const string DbSlow = "E_DB_SLOW";
    public const string DbTransactionFailed = "E_DB_TRANSACTION_FAILED";
    public const string DbConstraintViolation = "E_DB_CONSTRAINT";
    
    // ========== 采集器错误 (3xxx) ==========
    public const string CollectorDisconnected = "E_COLLECTOR_DISCONNECTED";
    public const string CollectorTimeout = "E_COLLECTOR_TIMEOUT";
    public const string CollectorTypeMismatch = "E_COLLECTOR_TYPE_MISMATCH";
    public const string CollectorBadTag = "E_COLLECTOR_BAD_TAG";
    public const string CollectorTooManyConnections = "E_COLLECTOR_TOO_MANY_CONN";
    public const string CollectorNoRoute = "E_COLLECTOR_NO_ROUTE";
    
    // ========== 管道错误 (4xxx) ==========
    public const string PipelineFull = "E_PIPELINE_FULL";
    public const string PipelineDropped = "E_PIPELINE_DROPPED";
    public const string PipelineBackpressure = "E_PIPELINE_BACKPRESSURE";
    
    // ========== MQTT错误 (5xxx) ==========
    public const string MqttDisconnected = "E_MQTT_DISCONNECTED";
    public const string MqttPublishFailed = "E_MQTT_PUBLISH_FAILED";
    public const string MqttOutboxFull = "E_MQTT_OUTBOX_FULL";
    
    // ========== 验证错误 (6xxx) ==========
    public const string ValidationFailed = "E_VALIDATION_FAILED";
    public const string ValidationMissingField = "E_VALIDATION_MISSING";
    public const string ValidationInvalidFormat = "E_VALIDATION_FORMAT";
    
    // ========== 限流错误 (7xxx) ==========
    public const string RateLimited = "E_RATE_LIMITED";
    
    // ========== 资源错误 (8xxx) ==========
    public const string ResourceNotFound = "E_NOT_FOUND";
    public const string ResourceConflict = "E_CONFLICT";
}

/// <summary>
/// 操作结果
/// </summary>
public sealed record OperationResult
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    
    public static OperationResult Ok() => new() { Success = true };
    
    public static OperationResult Fail(string errorCode, string? message = null) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = message
    };
}

/// <summary>
/// 带数据的操作结果
/// </summary>
public sealed record OperationResult<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    
    public static OperationResult<T> Ok(T data) => new() { Success = true, Data = data };
    
    public static OperationResult<T> Fail(string errorCode, string? message = null) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = message
    };
}
