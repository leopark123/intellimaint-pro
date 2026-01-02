using System.Text.Json;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace IntelliMaint.Host.Api.Endpoints;

/// <summary>
/// 数据分析 API 端点
/// </summary>
public static class CycleAnalysisEndpoints
{
    public static void MapCycleAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        // 周期分析端点
        var cycleGroup = app.MapGroup("/api/cycle-analysis")
            .WithTags("CycleAnalysis");

        cycleGroup.MapPost("/analyze", AnalyzeCyclesAsync)
            .RequireAuthorization(AuthPolicies.AllAuthenticated);
        
        cycleGroup.MapGet("/cycles", GetCyclesAsync)
            .RequireAuthorization(AuthPolicies.AllAuthenticated);
        
        cycleGroup.MapGet("/cycles/{id:long}", GetCycleAsync)
            .RequireAuthorization(AuthPolicies.AllAuthenticated);
        
        cycleGroup.MapGet("/cycles/recent/{deviceId}", GetRecentCyclesAsync)
            .RequireAuthorization(AuthPolicies.AllAuthenticated);
        
        cycleGroup.MapGet("/cycles/anomalies/{deviceId}", GetAnomalyCyclesAsync)
            .RequireAuthorization(AuthPolicies.AllAuthenticated);
        
        cycleGroup.MapGet("/stats/{deviceId}", GetStatsAsync)
            .RequireAuthorization(AuthPolicies.AllAuthenticated);
        
        cycleGroup.MapDelete("/cycles/{id:long}", DeleteCycleAsync)
            .RequireAuthorization(AuthPolicies.AdminOnly);

        // 基线学习端点
        var baselineGroup = app.MapGroup("/api/baselines")
            .WithTags("Baselines");

        baselineGroup.MapGet("/{deviceId}", GetBaselinesAsync)
            .RequireAuthorization(AuthPolicies.AllAuthenticated);
        
        baselineGroup.MapPost("/learn", LearnBaselinesAsync)
            .RequireAuthorization(AuthPolicies.OperatorOrAbove);
        
        baselineGroup.MapPost("/learn/current-angle", LearnCurrentAngleAsync)
            .RequireAuthorization(AuthPolicies.OperatorOrAbove);
        
        baselineGroup.MapPost("/learn/motor-balance", LearnMotorBalanceAsync)
            .RequireAuthorization(AuthPolicies.OperatorOrAbove);
        
        baselineGroup.MapDelete("/{deviceId}/{baselineType}", DeleteBaselineAsync)
            .RequireAuthorization(AuthPolicies.AdminOnly);
    }

    #region 周期分析端点

    private static async Task<IResult> AnalyzeCyclesAsync(
        [FromServices] ICycleAnalysisService analysisService,
        [FromServices] IWorkCycleRepository cycleRepo,
        [FromBody] AnalyzeCyclesRequest request,
        [FromQuery] bool save = false,
        CancellationToken ct = default)
    {
        var validationError = ValidateAnalyzeRequest(request);
        if (validationError != null)
            return Results.BadRequest(new ApiResponse<CycleAnalysisResult> { Success = false, Error = validationError });

        try
        {
            var analysisRequest = new CycleAnalysisRequest
            {
                DeviceId = request.DeviceId,
                AngleTagId = request.AngleTagId,
                Motor1CurrentTagId = request.Motor1CurrentTagId,
                Motor2CurrentTagId = request.Motor2CurrentTagId,
                StartTimeUtc = request.StartTimeUtc,
                EndTimeUtc = request.EndTimeUtc,
                AngleThreshold = request.AngleThreshold ?? 5.0,
                MinCycleDuration = request.MinCycleDuration ?? 20.0,
                MaxCycleDuration = request.MaxCycleDuration ?? 300.0
            };

            var result = await analysisService.AnalyzeCyclesAsync(analysisRequest, ct);

            // 保存周期到数据库
            if (save && result.Cycles.Count > 0)
            {
                await cycleRepo.CreateBatchAsync(result.Cycles, ct);
                Log.Information("Saved {Count} cycles to database", result.Cycles.Count);
            }

            return Results.Ok(new ApiResponse<CycleAnalysisResult> { Success = true, Data = result });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cycle analysis failed");
            return Results.BadRequest(new ApiResponse<CycleAnalysisResult> 
            { 
                Success = false, 
                Error = ex.Message 
            });
        }
    }

    private static async Task<IResult> GetCyclesAsync(
        [FromServices] IWorkCycleRepository repo,
        [FromQuery] string? deviceId,
        [FromQuery] long? startTime,
        [FromQuery] long? endTime,
        [FromQuery] bool? isAnomaly,
        [FromQuery] string? anomalyType,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var query = new WorkCycleQuery
        {
            DeviceId = deviceId,
            StartTimeUtc = startTime,
            EndTimeUtc = endTime,
            IsAnomaly = isAnomaly,
            AnomalyType = anomalyType,
            Limit = Math.Min(limit, 1000)
        };

        var cycles = await repo.QueryAsync(query, ct);
        return Results.Ok(new ApiResponse<IReadOnlyList<WorkCycle>> { Success = true, Data = cycles });
    }

    private static async Task<IResult> GetCycleAsync(
        [FromServices] IWorkCycleRepository repo,
        [FromRoute] long id,
        CancellationToken ct)
    {
        var cycle = await repo.GetAsync(id, ct);
        if (cycle == null)
            return Results.NotFound(new ApiResponse<WorkCycle> { Success = false, Error = "周期不存在" });

        return Results.Ok(new ApiResponse<WorkCycle> { Success = true, Data = cycle });
    }

    private static async Task<IResult> GetRecentCyclesAsync(
        [FromServices] IWorkCycleRepository repo,
        [FromRoute] string deviceId,
        [FromQuery] int count = 20,
        CancellationToken ct = default)
    {
        var cycles = await repo.GetRecentByDeviceAsync(deviceId, Math.Min(count, 100), ct);
        return Results.Ok(new ApiResponse<IReadOnlyList<WorkCycle>> { Success = true, Data = cycles });
    }

    private static async Task<IResult> GetAnomalyCyclesAsync(
        [FromServices] IWorkCycleRepository repo,
        [FromRoute] string deviceId,
        [FromQuery] long? after,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var cycles = await repo.GetAnomaliesByDeviceAsync(deviceId, after, Math.Min(limit, 200), ct);
        return Results.Ok(new ApiResponse<IReadOnlyList<WorkCycle>> { Success = true, Data = cycles });
    }

    private static async Task<IResult> GetStatsAsync(
        [FromServices] IWorkCycleRepository repo,
        [FromRoute] string deviceId,
        [FromQuery] long? startTime,
        [FromQuery] long? endTime,
        CancellationToken ct)
    {
        var stats = await repo.GetStatsSummaryAsync(deviceId, startTime, endTime, ct);
        return Results.Ok(new ApiResponse<CycleStatsSummary?> { Success = true, Data = stats });
    }

    private static async Task<IResult> DeleteCycleAsync(
        [FromServices] IWorkCycleRepository repo,
        [FromRoute] long id,
        CancellationToken ct)
    {
        await repo.DeleteAsync(id, ct);
        return Results.Ok(new ApiResponse<object> { Success = true, Data = null });
    }

    #endregion

    #region 基线端点

    private static async Task<IResult> GetBaselinesAsync(
        [FromServices] ICycleDeviceBaselineRepository repo,
        [FromRoute] string deviceId,
        CancellationToken ct)
    {
        var baselines = await repo.GetAllByDeviceAsync(deviceId, ct);
        
        // 解析 ModelJson 为可读对象
        var result = baselines.Select(b => new BaselineDto
        {
            DeviceId = b.DeviceId,
            BaselineType = b.BaselineType,
            SampleCount = b.SampleCount,
            UpdatedUtc = b.UpdatedUtc,
            Model = JsonSerializer.Deserialize<object>(b.ModelJson)
        }).ToList();

        return Results.Ok(new ApiResponse<List<BaselineDto>> { Success = true, Data = result });
    }

    private static async Task<IResult> LearnBaselinesAsync(
        [FromServices] IBaselineLearningService learningService,
        [FromBody] LearnBaselinesRequest request,
        CancellationToken ct)
    {
        var validationError = ValidateLearnRequest(request);
        if (validationError != null)
            return Results.BadRequest(new ApiResponse<object> { Success = false, Error = validationError });

        try
        {
            var config = new CycleAnalysisRequest
            {
                DeviceId = request.DeviceId,
                AngleTagId = request.AngleTagId,
                Motor1CurrentTagId = request.Motor1CurrentTagId,
                Motor2CurrentTagId = request.Motor2CurrentTagId,
                StartTimeUtc = request.StartTimeUtc,
                EndTimeUtc = request.EndTimeUtc
            };

            await learningService.LearnAllBaselinesAsync(
                config,
                request.StartTimeUtc,
                request.EndTimeUtc,
                ct);

            return Results.Ok(new ApiResponse<object> 
            { 
                Success = true, 
                Data = new { Message = "所有基线学习完成" } 
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Baseline learning failed");
            return Results.BadRequest(new ApiResponse<object> { Success = false, Error = ex.Message });
        }
    }

    private static async Task<IResult> LearnCurrentAngleAsync(
        [FromServices] IBaselineLearningService learningService,
        [FromBody] LearnCurrentAngleRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceId))
            return Results.BadRequest(new ApiResponse<CurrentAngleModel> { Success = false, Error = "DeviceId 不能为空" });

        try
        {
            var model = await learningService.LearnCurrentAngleModelAsync(
                request.DeviceId,
                request.AngleTagId,
                request.CurrentTagId,
                request.StartTimeUtc,
                request.EndTimeUtc,
                ct);

            return Results.Ok(new ApiResponse<CurrentAngleModel> { Success = true, Data = model });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Current-angle baseline learning failed");
            return Results.BadRequest(new ApiResponse<CurrentAngleModel> { Success = false, Error = ex.Message });
        }
    }

    private static async Task<IResult> LearnMotorBalanceAsync(
        [FromServices] IBaselineLearningService learningService,
        [FromBody] LearnMotorBalanceRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceId))
            return Results.BadRequest(new ApiResponse<MotorBalanceModel> { Success = false, Error = "DeviceId 不能为空" });

        try
        {
            var model = await learningService.LearnMotorBalanceModelAsync(
                request.DeviceId,
                request.Motor1TagId,
                request.Motor2TagId,
                request.StartTimeUtc,
                request.EndTimeUtc,
                ct);

            return Results.Ok(new ApiResponse<MotorBalanceModel> { Success = true, Data = model });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Motor balance baseline learning failed");
            return Results.BadRequest(new ApiResponse<MotorBalanceModel> { Success = false, Error = ex.Message });
        }
    }

    private static async Task<IResult> DeleteBaselineAsync(
        [FromServices] ICycleDeviceBaselineRepository repo,
        [FromRoute] string deviceId,
        [FromRoute] string baselineType,
        CancellationToken ct)
    {
        await repo.DeleteAsync(deviceId, baselineType, ct);
        return Results.Ok(new ApiResponse<object> { Success = true, Data = null });
    }

    #endregion

    #region 验证

    private static string? ValidateAnalyzeRequest(AnalyzeCyclesRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.DeviceId)) return "DeviceId 不能为空";
        if (string.IsNullOrWhiteSpace(r.AngleTagId)) return "AngleTagId 不能为空";
        if (string.IsNullOrWhiteSpace(r.Motor1CurrentTagId)) return "Motor1CurrentTagId 不能为空";
        if (string.IsNullOrWhiteSpace(r.Motor2CurrentTagId)) return "Motor2CurrentTagId 不能为空";
        if (r.StartTimeUtc <= 0) return "StartTimeUtc 必须大于 0";
        if (r.EndTimeUtc <= r.StartTimeUtc) return "EndTimeUtc 必须大于 StartTimeUtc";
        return null;
    }

    private static string? ValidateLearnRequest(LearnBaselinesRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.DeviceId)) return "DeviceId 不能为空";
        if (string.IsNullOrWhiteSpace(r.AngleTagId)) return "AngleTagId 不能为空";
        if (string.IsNullOrWhiteSpace(r.Motor1CurrentTagId)) return "Motor1CurrentTagId 不能为空";
        if (string.IsNullOrWhiteSpace(r.Motor2CurrentTagId)) return "Motor2CurrentTagId 不能为空";
        if (r.StartTimeUtc <= 0) return "StartTimeUtc 必须大于 0";
        if (r.EndTimeUtc <= r.StartTimeUtc) return "EndTimeUtc 必须大于 StartTimeUtc";
        return null;
    }

    #endregion

    #region Request/Response Models

    public sealed record AnalyzeCyclesRequest
    {
        public required string DeviceId { get; init; }
        public required string AngleTagId { get; init; }
        public required string Motor1CurrentTagId { get; init; }
        public required string Motor2CurrentTagId { get; init; }
        public required long StartTimeUtc { get; init; }
        public required long EndTimeUtc { get; init; }
        public double? AngleThreshold { get; init; }
        public double? MinCycleDuration { get; init; }
        public double? MaxCycleDuration { get; init; }
    }

    public sealed record LearnBaselinesRequest
    {
        public required string DeviceId { get; init; }
        public required string AngleTagId { get; init; }
        public required string Motor1CurrentTagId { get; init; }
        public required string Motor2CurrentTagId { get; init; }
        public required long StartTimeUtc { get; init; }
        public required long EndTimeUtc { get; init; }
    }

    public sealed record LearnCurrentAngleRequest
    {
        public required string DeviceId { get; init; }
        public required string AngleTagId { get; init; }
        public required string CurrentTagId { get; init; }
        public required long StartTimeUtc { get; init; }
        public required long EndTimeUtc { get; init; }
    }

    public sealed record LearnMotorBalanceRequest
    {
        public required string DeviceId { get; init; }
        public required string Motor1TagId { get; init; }
        public required string Motor2TagId { get; init; }
        public required long StartTimeUtc { get; init; }
        public required long EndTimeUtc { get; init; }
    }

    public sealed record BaselineDto
    {
        public required string DeviceId { get; init; }
        public required string BaselineType { get; init; }
        public int SampleCount { get; init; }
        public long UpdatedUtc { get; init; }
        public object? Model { get; init; }
    }

    #endregion
}
