namespace IntelliMaint.Core.Contracts;

/// <summary>
/// 审计日志查询参数
/// </summary>
public sealed record AuditLogQuery
{
    public string? Action { get; init; }
    public string? ResourceType { get; init; }
    public string? ResourceId { get; init; }
    public string? UserId { get; init; }
    public long? StartTs { get; init; }
    public long? EndTs { get; init; }
    public int Limit { get; init; } = 50;
    public int Offset { get; init; } = 0;
}
