using IntelliMaint.Core.Contracts;
using MediatR;

namespace IntelliMaint.Application.Events;

/// <summary>
/// P2: 告警创建事件 - 解耦告警通知
/// </summary>
public sealed record AlarmCreatedEvent : INotification
{
    public required AlarmRecord Alarm { get; init; }
    public required AlarmRule Rule { get; init; }
    public required TelemetryPoint TriggerPoint { get; init; }
    public required double TriggerValue { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// 告警确认事件
/// </summary>
public sealed record AlarmAcknowledgedEvent : INotification
{
    public required string AlarmId { get; init; }
    public required string AcknowledgedBy { get; init; }
    public required DateTimeOffset AcknowledgedAt { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// 告警关闭事件
/// </summary>
public sealed record AlarmClosedEvent : INotification
{
    public required string AlarmId { get; init; }
    public required string ClosedBy { get; init; }
    public required DateTimeOffset ClosedAt { get; init; }
    public string? Resolution { get; init; }
}
