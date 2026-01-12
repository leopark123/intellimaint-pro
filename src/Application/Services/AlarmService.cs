using IntelliMaint.Application.Events;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using MediatR;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Application.Services;

/// <summary>
/// P2: 告警业务服务 - 集中告警相关业务逻辑
/// 将散落在 Endpoints 和 Infrastructure 的业务逻辑移至 Application 层
/// </summary>
public interface IAlarmService
{
    /// <summary>查询告警列表</summary>
    Task<PagedResult<AlarmRecord>> QueryAsync(AlarmQuery query, CancellationToken ct);

    /// <summary>获取单个告警</summary>
    Task<AlarmRecord?> GetByIdAsync(string alarmId, CancellationToken ct);

    /// <summary>确认告警</summary>
    Task<AlarmRecord?> AcknowledgeAsync(string alarmId, string userId, string? notes, CancellationToken ct);

    /// <summary>关闭告警</summary>
    Task<AlarmRecord?> CloseAsync(string alarmId, string userId, string? resolution, CancellationToken ct);

    /// <summary>获取告警统计</summary>
    Task<AlarmStatistics> GetStatisticsAsync(CancellationToken ct);
}

/// <summary>
/// 告警统计
/// </summary>
public sealed record AlarmStatistics
{
    public int TotalOpen { get; init; }
    public int TotalAcknowledged { get; init; }
    public int TotalClosed { get; init; }
    public int Critical { get; init; }
    public int High { get; init; }
    public int Medium { get; init; }
    public int Low { get; init; }
    public int Info { get; init; }
}

public sealed class AlarmService : IAlarmService
{
    private readonly IAlarmRepository _alarmRepo;
    private readonly IMediator _mediator;
    private readonly ILogger<AlarmService> _logger;

    public AlarmService(
        IAlarmRepository alarmRepo,
        IMediator mediator,
        ILogger<AlarmService> logger)
    {
        _alarmRepo = alarmRepo;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<PagedResult<AlarmRecord>> QueryAsync(AlarmQuery query, CancellationToken ct)
    {
        return await _alarmRepo.QueryAsync(query, ct);
    }

    public async Task<AlarmRecord?> GetByIdAsync(string alarmId, CancellationToken ct)
    {
        return await _alarmRepo.GetAsync(alarmId, ct);
    }

    public async Task<AlarmRecord?> AcknowledgeAsync(string alarmId, string userId, string? notes, CancellationToken ct)
    {
        var alarm = await _alarmRepo.GetAsync(alarmId, ct);
        if (alarm == null)
        {
            _logger.LogWarning("Alarm not found for acknowledge: {AlarmId}", alarmId);
            return null;
        }

        if (alarm.Status == AlarmStatus.Closed)
        {
            _logger.LogWarning("Cannot acknowledge closed alarm: {AlarmId}", alarmId);
            return null;
        }

        // 使用 Repository 的 AckAsync 方法
        await _alarmRepo.AckAsync(new AlarmAckRequest
        {
            AlarmId = alarmId,
            AckedBy = userId,
            AckNote = notes
        }, ct);

        // 重新获取更新后的记录
        var updated = await _alarmRepo.GetAsync(alarmId, ct);

        // 发布领域事件
        await _mediator.Publish(new AlarmAcknowledgedEvent
        {
            AlarmId = alarmId,
            AcknowledgedBy = userId,
            AcknowledgedAt = DateTimeOffset.UtcNow,
            Notes = notes
        }, ct);

        _logger.LogInformation("Alarm acknowledged: {AlarmId} by {UserId}", alarmId, userId);

        return updated;
    }

    public async Task<AlarmRecord?> CloseAsync(string alarmId, string userId, string? resolution, CancellationToken ct)
    {
        var alarm = await _alarmRepo.GetAsync(alarmId, ct);
        if (alarm == null)
        {
            _logger.LogWarning("Alarm not found for close: {AlarmId}", alarmId);
            return null;
        }

        if (alarm.Status == AlarmStatus.Closed)
        {
            _logger.LogWarning("Alarm already closed: {AlarmId}", alarmId);
            return alarm;
        }

        // 使用 Repository 的 CloseAsync 方法
        await _alarmRepo.CloseAsync(alarmId, ct);

        // 重新获取更新后的记录
        var updated = await _alarmRepo.GetAsync(alarmId, ct);

        // 发布领域事件
        await _mediator.Publish(new AlarmClosedEvent
        {
            AlarmId = alarmId,
            ClosedBy = userId,
            ClosedAt = DateTimeOffset.UtcNow,
            Resolution = resolution
        }, ct);

        _logger.LogInformation("Alarm closed: {AlarmId} by {UserId}", alarmId, userId);

        return updated;
    }

    public async Task<AlarmStatistics> GetStatisticsAsync(CancellationToken ct)
    {
        // 获取各状态的告警数量
        var openQuery = new AlarmQuery { Status = AlarmStatus.Open, Limit = 1 };
        var ackQuery = new AlarmQuery { Status = AlarmStatus.Acknowledged, Limit = 1 };
        var closedQuery = new AlarmQuery { Status = AlarmStatus.Closed, Limit = 1 };

        var openResult = await _alarmRepo.QueryAsync(openQuery, ct);
        var ackResult = await _alarmRepo.QueryAsync(ackQuery, ct);
        var closedResult = await _alarmRepo.QueryAsync(closedQuery, ct);

        // 获取各严重级别的未关闭告警数量
        var openAlarms = await _alarmRepo.QueryAsync(new AlarmQuery { Status = AlarmStatus.Open, Limit = 10000 }, ct);

        var severityCounts = openAlarms.Items
            .GroupBy(a => a.Severity)
            .ToDictionary(g => g.Key, g => g.Count());

        return new AlarmStatistics
        {
            TotalOpen = openResult.TotalCount,
            TotalAcknowledged = ackResult.TotalCount,
            TotalClosed = closedResult.TotalCount,
            Critical = severityCounts.GetValueOrDefault(5, 0),
            High = severityCounts.GetValueOrDefault(4, 0),
            Medium = severityCounts.GetValueOrDefault(3, 0),
            Low = severityCounts.GetValueOrDefault(2, 0),
            Info = severityCounts.GetValueOrDefault(1, 0)
        };
    }
}
