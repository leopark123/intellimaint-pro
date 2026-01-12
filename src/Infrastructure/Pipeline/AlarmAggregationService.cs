using System;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Pipeline;

/// <summary>
/// v59: 告警聚合服务
/// 将同设备同规则触发的多个告警合并为一个聚合组
/// </summary>
public sealed class AlarmAggregationService
{
    private readonly IAlarmGroupRepository _groupRepo;
    private readonly ILogger<AlarmAggregationService> _logger;

    public AlarmAggregationService(
        IAlarmGroupRepository groupRepo,
        ILogger<AlarmAggregationService> logger)
    {
        _groupRepo = groupRepo;
        _logger = logger;
    }

    /// <summary>
    /// 处理新告警，加入或创建聚合组
    /// </summary>
    /// <param name="alarm">新创建的告警</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>关联的聚合组</returns>
    public async Task<AlarmGroup> AggregateAlarmAsync(AlarmRecord alarm, CancellationToken ct)
    {
        // 1. 从 alarm.Code 提取 ruleId (格式: "RULE:ruleId" 或其他格式)
        var ruleId = ExtractRuleId(alarm.Code);

        // 2. 查找活跃的聚合组
        var existingGroup = await _groupRepo.FindActiveGroupAsync(alarm.DeviceId, ruleId, ct);

        if (existingGroup != null)
        {
            // 3a. 更新现有聚合组
            var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var updated = existingGroup with
            {
                AlarmCount = existingGroup.AlarmCount + 1,
                LastOccurredUtc = alarm.Ts,
                Severity = Math.Max(existingGroup.Severity, alarm.Severity),
                Message = alarm.Message,
                UpdatedUtc = nowUtc
            };

            await _groupRepo.UpdateAsync(updated, ct);
            await _groupRepo.AddAlarmToGroupAsync(alarm.AlarmId, existingGroup.GroupId, ct);

            _logger.LogDebug("Alarm {AlarmId} aggregated to existing group {GroupId}, count={Count}",
                alarm.AlarmId, existingGroup.GroupId, updated.AlarmCount);

            return updated;
        }

        // 3b. 创建新聚合组
        var groupId = GenerateGroupId(alarm.DeviceId, ruleId, alarm.Ts);
        var newGroup = new AlarmGroup
        {
            GroupId = groupId,
            DeviceId = alarm.DeviceId,
            TagId = alarm.TagId,
            RuleId = ruleId,
            Severity = alarm.Severity,
            Code = alarm.Code,
            Message = alarm.Message,
            AlarmCount = 1,
            FirstOccurredUtc = alarm.Ts,
            LastOccurredUtc = alarm.Ts,
            AggregateStatus = AlarmStatus.Open,
            CreatedUtc = alarm.Ts,
            UpdatedUtc = alarm.Ts
        };

        await _groupRepo.CreateAsync(newGroup, ct);
        await _groupRepo.AddAlarmToGroupAsync(alarm.AlarmId, groupId, ct);

        _logger.LogInformation("New alarm group created: {GroupId} for device={DeviceId} rule={RuleId}",
            groupId, alarm.DeviceId, ruleId);

        return newGroup;
    }

    /// <summary>
    /// 从告警 Code 提取规则 ID
    /// 支持格式:
    /// - "RULE:ruleId" -> "ruleId"
    /// - "OFFLINE:ruleId" -> "ruleId"
    /// - "ROC:ruleId" -> "ruleId"
    /// - "VOLATILITY:ruleId" -> "ruleId"
    /// - 其他格式直接使用原始 Code
    /// </summary>
    private static string ExtractRuleId(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "unknown";

        // 查找冒号分隔符
        var colonIndex = code.IndexOf(':');
        if (colonIndex > 0 && colonIndex < code.Length - 1)
        {
            return code[(colonIndex + 1)..];
        }

        // 没有冒号，使用完整的 code 作为规则标识
        return code;
    }

    /// <summary>
    /// 生成聚合组 ID
    /// 格式: grp-{deviceId}-{ruleId}-{timestamp}
    /// </summary>
    private static string GenerateGroupId(string deviceId, string ruleId, long ts)
    {
        // 使用安全字符替换可能的特殊字符
        var safeDeviceId = SanitizeId(deviceId);
        var safeRuleId = SanitizeId(ruleId);
        return $"grp-{safeDeviceId}-{safeRuleId}-{ts}";
    }

    /// <summary>
    /// 清理 ID 中的特殊字符
    /// </summary>
    private static string SanitizeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "unknown";

        // 保留字母数字和下划线，其他替换为下划线
        var chars = id.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-')
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }
}
