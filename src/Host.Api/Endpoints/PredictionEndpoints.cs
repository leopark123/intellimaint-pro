using IntelliMaint.Application.Services;
using IntelliMaint.Core.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace IntelliMaint.Host.Api.Endpoints;

/// <summary>
/// v63: 预测与预警 API 端点
/// </summary>
public static class PredictionEndpoints
{
    public static void MapPredictionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/predictions")
            .WithTags("Predictions")
            .RequireAuthorization();

        // 趋势预测
        group.MapGet("/trend/{deviceId}", GetDeviceTrendAsync);
        group.MapGet("/trend", GetAllTrendsAsync);

        // 劣化检测
        group.MapGet("/degradation/{deviceId}", GetDeviceDegradationAsync);
        group.MapGet("/degradation", GetAllDegradationsAsync);

        // 综合预警汇总
        group.MapGet("/alerts", GetPredictionAlertsAsync);

        // RUL 预测
        group.MapGet("/rul/{deviceId}", GetDeviceRulAsync);
        group.MapGet("/rul", GetAllRulAsync);
    }

    /// <summary>
    /// 获取单个设备的趋势预测
    /// </summary>
    private static async Task<IResult> GetDeviceTrendAsync(
        string deviceId,
        [FromServices] ITrendPredictionService trendService,
        CancellationToken ct)
    {
        var summary = await trendService.PredictDeviceTrendAsync(deviceId, ct);

        if (summary == null)
        {
            return Results.NotFound(new
            {
                success = false,
                error = $"No trend data available for device {deviceId}"
            });
        }

        return Results.Ok(new
        {
            success = true,
            data = MapTrendSummaryToDto(summary)
        });
    }

    /// <summary>
    /// 获取所有设备的趋势预测
    /// </summary>
    private static async Task<IResult> GetAllTrendsAsync(
        [FromServices] ITrendPredictionService trendService,
        CancellationToken ct)
    {
        var summaries = await trendService.PredictAllDevicesTrendAsync(ct);

        return Results.Ok(new
        {
            success = true,
            data = summaries.Select(MapTrendSummaryToDto).ToList(),
            summary = new
            {
                totalDevices = summaries.Count,
                devicesWithAlerts = summaries.Count(s => s.MaxAlertLevel > PredictionAlertLevel.None),
                criticalAlerts = summaries.Count(s => s.MaxAlertLevel >= PredictionAlertLevel.Critical),
                highAlerts = summaries.Count(s => s.MaxAlertLevel == PredictionAlertLevel.High)
            }
        });
    }

    /// <summary>
    /// 获取单个设备的劣化检测
    /// </summary>
    private static async Task<IResult> GetDeviceDegradationAsync(
        string deviceId,
        [FromServices] IDegradationDetectionService degradationService,
        CancellationToken ct)
    {
        var results = await degradationService.DetectDeviceDegradationAsync(deviceId, ct);

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                deviceId,
                degradingTags = results.Count,
                results = results.Select(MapDegradationToDto).ToList()
            }
        });
    }

    /// <summary>
    /// 获取所有设备的劣化检测
    /// </summary>
    private static async Task<IResult> GetAllDegradationsAsync(
        [FromServices] IDegradationDetectionService degradationService,
        CancellationToken ct)
    {
        var results = await degradationService.DetectAllDevicesDegradationAsync(ct);

        // 按设备分组
        var byDevice = results.GroupBy(r => r.DeviceId)
            .Select(g => new
            {
                deviceId = g.Key,
                degradingTags = g.Count(),
                results = g.Select(MapDegradationToDto).ToList()
            })
            .ToList();

        return Results.Ok(new
        {
            success = true,
            data = byDevice,
            summary = new
            {
                totalDevices = byDevice.Count,
                totalDegradingTags = results.Count,
                byType = new
                {
                    gradualIncrease = results.Count(r => r.DegradationType == DegradationType.GradualIncrease),
                    gradualDecrease = results.Count(r => r.DegradationType == DegradationType.GradualDecrease),
                    increasingVariance = results.Count(r => r.DegradationType == DegradationType.IncreasingVariance)
                }
            }
        });
    }

    /// <summary>
    /// 获取所有预警汇总
    /// </summary>
    private static async Task<IResult> GetPredictionAlertsAsync(
        [FromServices] ITrendPredictionService trendService,
        [FromServices] IDegradationDetectionService degradationService,
        CancellationToken ct)
    {
        // 并行获取趋势预测和劣化检测
        var trendTask = trendService.PredictAllDevicesTrendAsync(ct);
        var degradationTask = degradationService.DetectAllDevicesDegradationAsync(ct);

        await Task.WhenAll(trendTask, degradationTask);

        var trends = trendTask.Result;
        var degradations = degradationTask.Result;

        // 汇总预警
        var trendAlerts = trends
            .SelectMany(t => t.TagPredictions)
            .Where(p => p.AlertLevel > PredictionAlertLevel.None)
            .OrderByDescending(p => p.AlertLevel)
            .ThenBy(p => p.HoursToAlarmThreshold)
            .Take(20)
            .Select(p => new
            {
                type = "trend",
                deviceId = p.DeviceId,
                tagId = p.TagId,
                level = p.AlertLevel.ToString().ToLower(),
                levelCode = (int)p.AlertLevel,
                message = p.AlertMessage,
                hoursToThreshold = p.HoursToAlarmThreshold,
                confidence = p.Confidence
            })
            .ToList();

        var degradationAlerts = degradations
            .OrderByDescending(d => Math.Abs(d.DegradationRate))
            .Take(20)
            .Select(d => new
            {
                type = "degradation",
                deviceId = d.DeviceId,
                tagId = d.TagId,
                degradationType = d.DegradationType.ToString(),
                rate = d.DegradationRate,
                changePercent = d.ChangePercent,
                description = d.Description
            })
            .ToList();

        return Results.Ok(new
        {
            success = true,
            data = new
            {
                trendAlerts,
                degradationAlerts,
                summary = new
                {
                    totalTrendAlerts = trendAlerts.Count,
                    totalDegradationAlerts = degradationAlerts.Count,
                    criticalCount = trendAlerts.Count(a => a.levelCode >= (int)PredictionAlertLevel.Critical),
                    highCount = trendAlerts.Count(a => a.levelCode == (int)PredictionAlertLevel.High)
                }
            }
        });
    }

    /// <summary>
    /// 映射趋势汇总到 DTO
    /// </summary>
    private static object MapTrendSummaryToDto(DeviceTrendSummary summary)
    {
        return new
        {
            deviceId = summary.DeviceId,
            timestamp = summary.Timestamp,
            maxAlertLevel = summary.MaxAlertLevel.ToString().ToLower(),
            maxAlertLevelCode = (int)summary.MaxAlertLevel,
            riskTagCount = summary.RiskTagCount,
            riskSummary = summary.RiskSummary,
            predictions = summary.TagPredictions.Select(p => new
            {
                tagId = p.TagId,
                currentValue = p.CurrentValue,
                predictedValue = p.PredictedValue,
                trendSlope = p.TrendSlope,
                trendDirection = p.TrendDirection,
                confidence = p.Confidence,
                alertLevel = p.AlertLevel.ToString().ToLower(),
                alertLevelCode = (int)p.AlertLevel,
                hoursToThreshold = p.HoursToAlarmThreshold,
                alertMessage = p.AlertMessage
            }).ToList()
        };
    }

    /// <summary>
    /// 映射劣化结果到 DTO
    /// </summary>
    private static object MapDegradationToDto(DegradationResult result)
    {
        return new
        {
            tagId = result.TagId,
            timestamp = result.Timestamp,
            isDegrading = result.IsDegrading,
            degradationType = result.DegradationType.ToString(),
            degradationRate = result.DegradationRate,
            startValue = result.StartValue,
            currentValue = result.CurrentValue,
            changePercent = result.ChangePercent,
            description = result.Description
        };
    }

    /// <summary>
    /// 获取单个设备的 RUL 预测
    /// </summary>
    private static async Task<IResult> GetDeviceRulAsync(
        string deviceId,
        [FromServices] IRulPredictionService rulService,
        CancellationToken ct)
    {
        var prediction = await rulService.PredictDeviceRulAsync(deviceId, ct);

        if (prediction == null)
        {
            return Results.NotFound(new
            {
                success = false,
                error = $"Cannot predict RUL for device {deviceId}"
            });
        }

        return Results.Ok(new
        {
            success = true,
            data = MapRulPredictionToDto(prediction)
        });
    }

    /// <summary>
    /// 获取所有设备的 RUL 预测
    /// </summary>
    private static async Task<IResult> GetAllRulAsync(
        [FromServices] IRulPredictionService rulService,
        CancellationToken ct)
    {
        var predictions = await rulService.PredictAllDevicesRulAsync(ct);

        // 按风险等级分组统计
        var criticalCount = predictions.Count(p => p.RiskLevel == RulRiskLevel.Critical);
        var highCount = predictions.Count(p => p.RiskLevel == RulRiskLevel.High);
        var mediumCount = predictions.Count(p => p.RiskLevel == RulRiskLevel.Medium);

        return Results.Ok(new
        {
            success = true,
            data = predictions.Select(MapRulPredictionToDto).ToList(),
            summary = new
            {
                totalDevices = predictions.Count,
                riskDistribution = new
                {
                    critical = criticalCount,
                    high = highCount,
                    medium = mediumCount,
                    low = predictions.Count - criticalCount - highCount - mediumCount
                },
                statusDistribution = new
                {
                    healthy = predictions.Count(p => p.Status == RulStatus.Healthy),
                    normalDegradation = predictions.Count(p => p.Status == RulStatus.NormalDegradation),
                    acceleratedDegradation = predictions.Count(p => p.Status == RulStatus.AcceleratedDegradation),
                    nearFailure = predictions.Count(p => p.Status == RulStatus.NearFailure),
                    insufficientData = predictions.Count(p => p.Status == RulStatus.InsufficientData)
                },
                averageRulDays = predictions
                    .Where(p => p.RemainingUsefulLifeDays.HasValue)
                    .Select(p => p.RemainingUsefulLifeDays!.Value)
                    .DefaultIfEmpty(0)
                    .Average()
            }
        });
    }

    /// <summary>
    /// 映射 RUL 预测到 DTO
    /// </summary>
    private static object MapRulPredictionToDto(RulPrediction prediction)
    {
        return new
        {
            deviceId = prediction.DeviceId,
            timestamp = prediction.PredictionTimestamp,
            currentHealthIndex = prediction.CurrentHealthIndex,
            remainingUsefulLifeHours = prediction.RemainingUsefulLifeHours,
            remainingUsefulLifeDays = prediction.RemainingUsefulLifeDays,
            predictedFailureTime = prediction.PredictedFailureTime,
            confidence = prediction.Confidence,
            degradationRate = prediction.DegradationRate,
            modelType = prediction.ModelType.ToString().ToLower(),
            status = prediction.Status.ToString(),
            statusCode = (int)prediction.Status,
            riskLevel = prediction.RiskLevel.ToString().ToLower(),
            riskLevelCode = (int)prediction.RiskLevel,
            recommendedMaintenanceTime = prediction.RecommendedMaintenanceTime,
            diagnosticMessage = prediction.DiagnosticMessage,
            factors = prediction.Factors.Select(f => new
            {
                name = f.Name,
                tagId = f.TagId,
                weight = f.Weight,
                currentStatus = f.CurrentStatus,
                contribution = f.Contribution
            }).ToList()
        };
    }
}
