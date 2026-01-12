using MediatR;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Application.Events;

/// <summary>
/// P2: 告警事件处理器 - 集中处理告警通知逻辑
/// </summary>
public sealed class AlarmCreatedEventHandler : INotificationHandler<AlarmCreatedEvent>
{
    private readonly ILogger<AlarmCreatedEventHandler> _logger;

    public AlarmCreatedEventHandler(ILogger<AlarmCreatedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(AlarmCreatedEvent notification, CancellationToken cancellationToken)
    {
        var alarm = notification.Alarm;
        var rule = notification.Rule;

        _logger.LogInformation(
            "[AlarmEvent] Alarm created: {AlarmId}, Severity: {Severity}, Rule: {RuleId}, Tag: {TagId}, Value: {Value}",
            alarm.AlarmId,
            alarm.Severity,
            rule.RuleId,
            alarm.TagId,
            notification.TriggerValue);

        // 未来扩展点：
        // 1. 发送 WebSocket 通知到前端
        // 2. 发送邮件/短信通知
        // 3. 写入通知队列
        // 4. 触发工作流

        return Task.CompletedTask;
    }
}

/// <summary>
/// 告警确认事件处理器
/// </summary>
public sealed class AlarmAcknowledgedEventHandler : INotificationHandler<AlarmAcknowledgedEvent>
{
    private readonly ILogger<AlarmAcknowledgedEventHandler> _logger;

    public AlarmAcknowledgedEventHandler(ILogger<AlarmAcknowledgedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(AlarmAcknowledgedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[AlarmEvent] Alarm acknowledged: {AlarmId} by {User}",
            notification.AlarmId,
            notification.AcknowledgedBy);

        return Task.CompletedTask;
    }
}

/// <summary>
/// 告警关闭事件处理器
/// </summary>
public sealed class AlarmClosedEventHandler : INotificationHandler<AlarmClosedEvent>
{
    private readonly ILogger<AlarmClosedEventHandler> _logger;

    public AlarmClosedEventHandler(ILogger<AlarmClosedEventHandler> logger)
    {
        _logger = logger;
    }

    public Task Handle(AlarmClosedEvent notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "[AlarmEvent] Alarm closed: {AlarmId} by {User}, Resolution: {Resolution}",
            notification.AlarmId,
            notification.ClosedBy,
            notification.Resolution ?? "N/A");

        return Task.CompletedTask;
    }
}
