using IntelliMaint.Application.Services;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace IntelliMaint.Host.Api.Endpoints;

/// <summary>
/// v45: 健康评估 API 端点
/// </summary>
public static class HealthAssessmentEndpoints
{
    public static void MapHealthAssessmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/health-assessment")
            .WithTags("Health Assessment")
            .RequireAuthorization();

        // 获取单个设备的健康评分
        group.MapGet("/devices/{deviceId}", GetDeviceHealthAsync);
        
        // 获取所有设备的健康评分
        group.MapGet("/devices", GetAllDevicesHealthAsync);
        
        // 获取设备基线
        group.MapGet("/baselines/{deviceId}", GetBaselineAsync);
        
        // 学习设备基线
        group.MapPost("/baselines/{deviceId}/learn", LearnBaselineAsync)
            .RequireAuthorization(AuthPolicies.OperatorOrAbove);
        
        // 删除设备基线
        group.MapDelete("/baselines/{deviceId}", DeleteBaselineAsync)
            .RequireAuthorization(AuthPolicies.OperatorOrAbove);
        
        // 获取所有基线列表
        group.MapGet("/baselines", ListBaselinesAsync);

        // v60: 获取设备健康历史趋势
        group.MapGet("/devices/{deviceId}/history", GetDeviceHealthHistoryAsync);

        // v60: 获取健康汇总统计
        group.MapGet("/summary", GetHealthSummaryAsync);

        // v62: 获取设备多尺度健康评估
        group.MapGet("/devices/{deviceId}/multi-scale", GetMultiScaleAssessmentAsync);
    }

    /// <summary>
    /// 获取单个设备的健康评分
    /// </summary>
    private static async Task<IResult> GetDeviceHealthAsync(
        string deviceId,
        [FromQuery] int? windowMinutes,
        [FromServices] HealthAssessmentService service,
        CancellationToken ct)
    {
        var score = await service.AssessDeviceAsync(deviceId, windowMinutes, ct);
        
        if (score == null)
        {
            return Results.NotFound(new 
            { 
                success = false, 
                error = $"No data available for device {deviceId}" 
            });
        }

        return Results.Ok(new { success = true, data = MapToDto(score) });
    }

    /// <summary>
    /// 获取所有设备的健康评分
    /// </summary>
    private static async Task<IResult> GetAllDevicesHealthAsync(
        [FromQuery] int? windowMinutes,
        [FromServices] HealthAssessmentService service,
        CancellationToken ct)
    {
        var scores = await service.AssessAllDevicesAsync(windowMinutes, ct);
        
        return Results.Ok(new 
        { 
            success = true, 
            data = scores.Select(MapToDto).ToList() 
        });
    }

    /// <summary>
    /// 获取设备基线
    /// </summary>
    private static async Task<IResult> GetBaselineAsync(
        string deviceId,
        [FromServices] HealthAssessmentService service,
        CancellationToken ct)
    {
        var baseline = await service.GetBaselineAsync(deviceId, ct);
        
        if (baseline == null)
        {
            return Results.NotFound(new 
            { 
                success = false, 
                error = $"No baseline for device {deviceId}" 
            });
        }

        return Results.Ok(new { success = true, data = MapBaselineToDto(baseline) });
    }

    /// <summary>
    /// 学习设备基线
    /// </summary>
    private static async Task<IResult> LearnBaselineAsync(
        string deviceId,
        [FromBody] LearnBaselineRequest? request,
        [FromServices] HealthAssessmentService service,
        [FromServices] AuditService auditService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        int learningHours = request?.LearningHours ?? 24;
        
        var baseline = await service.LearnBaselineAsync(deviceId, learningHours, ct);
        
        if (baseline == null)
        {
            return Results.BadRequest(new 
            { 
                success = false, 
                error = "Not enough data to learn baseline. Need at least 100 samples per tag." 
            });
        }

        // 审计日志
        await auditService.LogAsync(
            "BaselineLearn", 
            "HealthBaseline", 
            deviceId, 
            $"Learned baseline with {learningHours}h data, {baseline.TagBaselines.Count} tags",
            ct);

        return Results.Ok(new 
        { 
            success = true, 
            data = MapBaselineToDto(baseline),
            message = $"Baseline learned successfully with {baseline.TagBaselines.Count} tags"
        });
    }

    /// <summary>
    /// 删除设备基线
    /// </summary>
    private static async Task<IResult> DeleteBaselineAsync(
        string deviceId,
        [FromServices] HealthAssessmentService service,
        [FromServices] AuditService auditService,
        CancellationToken ct)
    {
        await service.DeleteBaselineAsync(deviceId, ct);

        await auditService.LogAsync(
            "BaselineDelete", 
            "HealthBaseline", 
            deviceId, 
            null,
            ct);

        return Results.Ok(new { success = true, message = "Baseline deleted" });
    }

    /// <summary>
    /// 获取所有基线列表
    /// </summary>
    private static async Task<IResult> ListBaselinesAsync(
        [FromServices] IHealthBaselineRepository baselineRepo,
        CancellationToken ct)
    {
        var baselines = await baselineRepo.ListAsync(ct);
        
        return Results.Ok(new 
        { 
            success = true, 
            data = baselines.Select(MapBaselineToDto).ToList() 
        });
    }

    /// <summary>
    /// v60: 获取设备健康历史趋势
    /// </summary>
    private static async Task<IResult> GetDeviceHealthHistoryAsync(
        string deviceId,
        [FromQuery] long? startTs,
        [FromQuery] long? endTs,
        [FromServices] IDeviceHealthSnapshotRepository snapshotRepo,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var start = startTs ?? (now - 24 * 60 * 60 * 1000); // 默认24小时
        var end = endTs ?? now;

        var history = await snapshotRepo.GetHistoryAsync(deviceId, start, end, ct);

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                deviceId,
                startTs = start,
                endTs = end,
                count = history.Count,
                snapshots = history.Select(MapSnapshotToDto).ToList()
            }
        });
    }

    /// <summary>
    /// v60: 获取健康汇总统计
    /// </summary>
    private static async Task<IResult> GetHealthSummaryAsync(
        [FromServices] IDeviceHealthSnapshotRepository snapshotRepo,
        CancellationToken ct)
    {
        // 获取所有设备最新快照
        var latestSnapshots = await snapshotRepo.GetLatestAllAsync(ct);

        // 统计各级别设备数量
        int healthyCount = 0, attentionCount = 0, warningCount = 0, criticalCount = 0;
        double totalIndex = 0;

        foreach (var snapshot in latestSnapshots)
        {
            totalIndex += snapshot.Index;
            switch (snapshot.Level)
            {
                case HealthLevel.Healthy: healthyCount++; break;
                case HealthLevel.Attention: attentionCount++; break;
                case HealthLevel.Warning: warningCount++; break;
                case HealthLevel.Critical: criticalCount++; break;
            }
        }

        var deviceCount = latestSnapshots.Count;
        var avgIndex = deviceCount > 0 ? (int)Math.Round(totalIndex / deviceCount) : 0;

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                assessedDevices = deviceCount,
                avgHealthIndex = avgIndex,
                distribution = new
                {
                    healthy = healthyCount,
                    attention = attentionCount,
                    warning = warningCount,
                    critical = criticalCount
                },
                devices = latestSnapshots.Select(s => new
                {
                    deviceId = s.DeviceId,
                    index = s.Index,
                    level = s.Level.ToString().ToLower(),
                    timestamp = s.Timestamp
                }).ToList()
            }
        });
    }

    /// <summary>
    /// v62: 获取设备多尺度健康评估
    /// </summary>
    private static async Task<IResult> GetMultiScaleAssessmentAsync(
        string deviceId,
        [FromServices] IMultiScaleAssessmentService multiScaleService,
        CancellationToken ct)
    {
        var score = await multiScaleService.AssessAsync(deviceId, ct);

        if (score == null)
        {
            return Results.NotFound(new
            {
                success = false,
                error = $"No data available for multi-scale assessment of device {deviceId}"
            });
        }

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                deviceId,
                shortTermScore = score.ShortTermScore,
                mediumTermScore = score.MediumTermScore,
                longTermScore = score.LongTermScore,
                compositeScore = score.CompositeScore,
                trendDirection = score.TrendDirection,
                trendDescription = score.TrendDescription,
                summary = multiScaleService.GetSummary(score)
            }
        });
    }

    /// <summary>
    /// 映射快照到 DTO
    /// </summary>
    private static object MapSnapshotToDto(DeviceHealthSnapshot snapshot)
    {
        return new
        {
            deviceId = snapshot.DeviceId,
            timestamp = snapshot.Timestamp,
            index = snapshot.Index,
            level = snapshot.Level.ToString().ToLower(),
            deviationScore = snapshot.DeviationScore,
            trendScore = snapshot.TrendScore,
            stabilityScore = snapshot.StabilityScore,
            alarmScore = snapshot.AlarmScore
        };
    }

    /// <summary>
    /// 映射健康评分到 DTO
    /// </summary>
    private static object MapToDto(HealthScore score)
    {
        return new
        {
            deviceId = score.DeviceId,
            timestamp = score.Timestamp,
            index = score.Index,
            level = score.Level.ToString().ToLower(),
            levelCode = (int)score.Level,
            deviationScore = score.DeviationScore,
            trendScore = score.TrendScore,
            stabilityScore = score.StabilityScore,
            alarmScore = score.AlarmScore,
            hasBaseline = score.HasBaseline,
            problemTags = score.ProblemTags,
            diagnosticMessage = score.DiagnosticMessage
        };
    }

    /// <summary>
    /// 映射基线到 DTO
    /// </summary>
    private static object MapBaselineToDto(DeviceBaseline baseline)
    {
        return new
        {
            deviceId = baseline.DeviceId,
            createdUtc = baseline.CreatedUtc,
            updatedUtc = baseline.UpdatedUtc,
            sampleCount = baseline.SampleCount,
            learningHours = baseline.LearningHours,
            tagCount = baseline.TagBaselines.Count,
            tags = baseline.TagBaselines.Select(kv => new
            {
                tagId = kv.Key,
                normalMean = kv.Value.NormalMean,
                normalStdDev = kv.Value.NormalStdDev,
                normalMin = kv.Value.NormalMin,
                normalMax = kv.Value.NormalMax,
                normalCV = kv.Value.NormalCV
            })
        };
    }
}

/// <summary>
/// 学习基线请求
/// </summary>
public sealed record LearnBaselineRequest
{
    /// <summary>学习时长（小时），默认 24</summary>
    public int LearningHours { get; init; } = 24;
}
