using IntelliMaint.Application.Services;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace IntelliMaint.Host.Api.Endpoints;

/// <summary>
/// v64: 电机故障预测 API 端点
/// </summary>
public static class MotorEndpoints
{
    public static void MapMotorEndpoints(this WebApplication app)
    {
        // ========== 电机模型 ==========
        var modelGroup = app.MapGroup("/api/motor-models")
            .RequireAuthorization()
            .WithTags("Motor Models");

        modelGroup.MapGet("/", ListMotorModels)
            .WithName("ListMotorModels")
            .WithSummary("获取所有电机模型");

        modelGroup.MapGet("/{modelId}", GetMotorModel)
            .WithName("GetMotorModel")
            .WithSummary("获取指定电机模型");

        modelGroup.MapPost("/", CreateMotorModel)
            .RequireAuthorization("OperatorOrAbove")
            .WithName("CreateMotorModel")
            .WithSummary("创建电机模型");

        modelGroup.MapPut("/{modelId}", UpdateMotorModel)
            .RequireAuthorization("OperatorOrAbove")
            .WithName("UpdateMotorModel")
            .WithSummary("更新电机模型");

        modelGroup.MapDelete("/{modelId}", DeleteMotorModel)
            .RequireAuthorization("AdminOnly")
            .WithName("DeleteMotorModel")
            .WithSummary("删除电机模型");

        // ========== 电机实例 ==========
        var instanceGroup = app.MapGroup("/api/motor-instances")
            .RequireAuthorization()
            .WithTags("Motor Instances");

        instanceGroup.MapGet("/", ListMotorInstances)
            .WithName("ListMotorInstances")
            .WithSummary("获取所有电机实例");

        instanceGroup.MapGet("/{instanceId}", GetMotorInstance)
            .WithName("GetMotorInstance")
            .WithSummary("获取指定电机实例");

        instanceGroup.MapGet("/{instanceId}/detail", GetMotorInstanceDetail)
            .WithName("GetMotorInstanceDetail")
            .WithSummary("获取电机实例详情（包含模型、映射、模式）");

        instanceGroup.MapPost("/", CreateMotorInstance)
            .RequireAuthorization("OperatorOrAbove")
            .WithName("CreateMotorInstance")
            .WithSummary("创建电机实例");

        instanceGroup.MapPut("/{instanceId}", UpdateMotorInstance)
            .RequireAuthorization("OperatorOrAbove")
            .WithName("UpdateMotorInstance")
            .WithSummary("更新电机实例");

        instanceGroup.MapDelete("/{instanceId}", DeleteMotorInstance)
            .RequireAuthorization("AdminOnly")
            .WithName("DeleteMotorInstance")
            .WithSummary("删除电机实例");

        // ========== 参数映射 ==========
        instanceGroup.MapGet("/{instanceId}/mappings", ListParameterMappings)
            .WithName("ListParameterMappings")
            .WithSummary("获取电机实例的参数映射");

        instanceGroup.MapPost("/{instanceId}/mappings", CreateParameterMapping)
            .RequireAuthorization("OperatorOrAbove")
            .WithName("CreateParameterMapping")
            .WithSummary("添加参数映射");

        instanceGroup.MapPost("/{instanceId}/mappings/batch", CreateParameterMappingBatch)
            .RequireAuthorization("OperatorOrAbove")
            .WithName("CreateParameterMappingBatch")
            .WithSummary("批量添加参数映射");

        instanceGroup.MapDelete("/{instanceId}/mappings/{mappingId}", DeleteParameterMapping)
            .RequireAuthorization("OperatorOrAbove")
            .WithName("DeleteParameterMapping")
            .WithSummary("删除参数映射");

        // ========== 操作模式 ==========
        instanceGroup.MapGet("/{instanceId}/modes", ListOperationModes)
            .WithName("ListOperationModes")
            .WithSummary("获取电机实例的操作模式");

        instanceGroup.MapPost("/{instanceId}/modes", CreateOperationMode)
            .RequireAuthorization("OperatorOrAbove")
            .WithName("CreateOperationMode")
            .WithSummary("创建操作模式");

        instanceGroup.MapPut("/{instanceId}/modes/{modeId}", UpdateOperationMode)
            .RequireAuthorization("OperatorOrAbove")
            .WithName("UpdateOperationMode")
            .WithSummary("更新操作模式");

        instanceGroup.MapDelete("/{instanceId}/modes/{modeId}", DeleteOperationMode)
            .RequireAuthorization("OperatorOrAbove")
            .WithName("DeleteOperationMode")
            .WithSummary("删除操作模式");

        instanceGroup.MapPatch("/{instanceId}/modes/{modeId}/enable", SetOperationModeEnabled)
            .RequireAuthorization("OperatorOrAbove")
            .WithName("SetOperationModeEnabled")
            .WithSummary("设置操作模式启用状态");

        // ========== 基线管理 ==========
        instanceGroup.MapGet("/{instanceId}/baselines", ListBaselines)
            .WithName("ListBaselines")
            .WithSummary("获取电机实例的所有基线");

        instanceGroup.MapGet("/{instanceId}/modes/{modeId}/baselines", ListBaselinesByMode)
            .WithName("ListBaselinesByMode")
            .WithSummary("获取指定操作模式的基线");

        instanceGroup.MapDelete("/{instanceId}/modes/{modeId}/baselines", DeleteBaselinesByMode)
            .RequireAuthorization("OperatorOrAbove")
            .WithName("DeleteBaselinesByMode")
            .WithSummary("删除指定操作模式的基线");

        // ========== 基线学习 ==========
        instanceGroup.MapPost("/{instanceId}/learn", StartBaselineLearning)
            .RequireAuthorization("OperatorOrAbove")
            .WithName("StartBaselineLearning")
            .WithSummary("启动基线学习任务");

        instanceGroup.MapPost("/{instanceId}/learn-all", LearnAllModes)
            .RequireAuthorization("OperatorOrAbove")
            .WithName("LearnAllModes")
            .WithSummary("为所有启用的操作模式学习基线");

        instanceGroup.MapGet("/{instanceId}/learning-tasks", ListLearningTasks)
            .WithName("ListLearningTasks")
            .WithSummary("获取学习任务列表");

        instanceGroup.MapGet("/{instanceId}/learning-tasks/{taskId}", GetLearningTask)
            .WithName("GetLearningTask")
            .WithSummary("获取学习任务状态");

        // ========== 操作模式检测 ==========
        instanceGroup.MapGet("/{instanceId}/current-mode", GetCurrentOperationMode)
            .WithName("GetCurrentOperationMode")
            .WithSummary("检测当前操作模式");

        // ========== 故障诊断 ==========
        instanceGroup.MapPost("/{instanceId}/diagnose", DiagnoseInstance)
            .RequireAuthorization("OperatorOrAbove")
            .WithName("DiagnoseInstance")
            .WithSummary("执行实时故障诊断");

        instanceGroup.MapGet("/{instanceId}/diagnosis", GetLatestDiagnosis)
            .WithName("GetLatestDiagnosis")
            .WithSummary("获取最新诊断结果");

        // 获取所有诊断结果
        app.MapGet("/api/motor-diagnoses", GetAllDiagnoses)
            .RequireAuthorization()
            .WithTags("Motor Instances")
            .WithName("GetAllDiagnoses")
            .WithSummary("获取所有电机的最新诊断结果");
    }

    // ========== 电机模型实现 ==========

    private static async Task<IResult> ListMotorModels(
        [FromServices] IMotorModelRepository repo,
        CancellationToken ct)
    {
        var models = await repo.ListAsync(ct);
        return Results.Ok(new { success = true, data = models });
    }

    private static async Task<IResult> GetMotorModel(
        string modelId,
        [FromServices] IMotorModelRepository repo,
        CancellationToken ct)
    {
        var model = await repo.GetAsync(modelId, ct);
        return model != null ? Results.Ok(new { success = true, data = model }) : Results.NotFound();
    }

    private static async Task<IResult> CreateMotorModel(
        [FromBody] CreateMotorModelRequest request,
        [FromServices] IMotorModelRepository repo,
        HttpContext context,
        CancellationToken ct)
    {
        var userId = context.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        var model = new MotorModel
        {
            ModelId = Guid.NewGuid().ToString(),
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            RatedPower = request.RatedPower,
            RatedVoltage = request.RatedVoltage,
            RatedCurrent = request.RatedCurrent,
            RatedSpeed = request.RatedSpeed,
            RatedFrequency = request.RatedFrequency,
            PolePairs = request.PolePairs,
            VfdModel = request.VfdModel,
            BearingModel = request.BearingModel,
            BearingRollingElements = request.BearingRollingElements,
            BearingBallDiameter = request.BearingBallDiameter,
            BearingPitchDiameter = request.BearingPitchDiameter,
            BearingContactAngle = request.BearingContactAngle,
            CreatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            CreatedBy = userId
        };

        await repo.CreateAsync(model, ct);
        return Results.Ok(new { success = true, data = model });
    }

    private static async Task<IResult> UpdateMotorModel(
        string modelId,
        [FromBody] CreateMotorModelRequest request,
        [FromServices] IMotorModelRepository repo,
        CancellationToken ct)
    {
        var existing = await repo.GetAsync(modelId, ct);
        if (existing == null) return Results.NotFound();

        var model = existing with
        {
            Name = request.Name,
            Description = request.Description,
            Type = request.Type,
            RatedPower = request.RatedPower,
            RatedVoltage = request.RatedVoltage,
            RatedCurrent = request.RatedCurrent,
            RatedSpeed = request.RatedSpeed,
            RatedFrequency = request.RatedFrequency,
            PolePairs = request.PolePairs,
            VfdModel = request.VfdModel,
            BearingModel = request.BearingModel,
            BearingRollingElements = request.BearingRollingElements,
            BearingBallDiameter = request.BearingBallDiameter,
            BearingPitchDiameter = request.BearingPitchDiameter,
            BearingContactAngle = request.BearingContactAngle,
            UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await repo.UpdateAsync(model, ct);
        return Results.Ok(new { success = true, data = model });
    }

    private static async Task<IResult> DeleteMotorModel(
        string modelId,
        [FromServices] IMotorModelRepository repo,
        [FromServices] IMotorInstanceRepository instanceRepo,
        CancellationToken ct)
    {
        var instances = await instanceRepo.ListByModelAsync(modelId, ct);
        if (instances.Count > 0)
        {
            return Results.BadRequest(new { error = $"无法删除：该模型有 {instances.Count} 个关联实例" });
        }

        await repo.DeleteAsync(modelId, ct);
        return Results.Ok(new { success = true });
    }

    // ========== 电机实例实现 ==========

    private static async Task<IResult> ListMotorInstances(
        [FromQuery] string? deviceId,
        [FromQuery] string? modelId,
        [FromServices] IMotorInstanceRepository repo,
        CancellationToken ct)
    {
        IReadOnlyList<MotorInstance> instances;

        if (!string.IsNullOrEmpty(deviceId))
            instances = await repo.ListByDeviceAsync(deviceId, ct);
        else if (!string.IsNullOrEmpty(modelId))
            instances = await repo.ListByModelAsync(modelId, ct);
        else
            instances = await repo.ListAsync(ct);

        return Results.Ok(new { success = true, data = instances });
    }

    private static async Task<IResult> GetMotorInstance(
        string instanceId,
        [FromServices] IMotorInstanceRepository repo,
        CancellationToken ct)
    {
        var instance = await repo.GetAsync(instanceId, ct);
        return instance != null ? Results.Ok(new { success = true, data = instance }) : Results.NotFound();
    }

    private static async Task<IResult> GetMotorInstanceDetail(
        string instanceId,
        [FromServices] IMotorInstanceRepository repo,
        CancellationToken ct)
    {
        var detail = await repo.GetDetailAsync(instanceId, ct);
        return detail != null ? Results.Ok(new { success = true, data = detail }) : Results.NotFound();
    }

    private static async Task<IResult> CreateMotorInstance(
        [FromBody] CreateMotorInstanceRequest request,
        [FromServices] IMotorInstanceRepository repo,
        [FromServices] IMotorModelRepository modelRepo,
        [FromServices] IDeviceRepository deviceRepo,
        CancellationToken ct)
    {
        // 验证模型存在
        var model = await modelRepo.GetAsync(request.ModelId, ct);
        if (model == null)
            return Results.BadRequest(new { error = $"电机模型不存在: {request.ModelId}" });

        // 验证设备存在
        var device = await deviceRepo.GetAsync(request.DeviceId, ct);
        if (device == null)
            return Results.BadRequest(new { error = $"设备不存在: {request.DeviceId}" });

        var instance = new MotorInstance
        {
            InstanceId = Guid.NewGuid().ToString(),
            ModelId = request.ModelId,
            DeviceId = request.DeviceId,
            Name = request.Name,
            Location = request.Location,
            InstallDate = request.InstallDate,
            AssetNumber = request.AssetNumber,
            DiagnosisEnabled = true,
            CreatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await repo.CreateAsync(instance, ct);
        return Results.Ok(new { success = true, data = instance });
    }

    private static async Task<IResult> UpdateMotorInstance(
        string instanceId,
        [FromBody] CreateMotorInstanceRequest request,
        [FromServices] IMotorInstanceRepository repo,
        CancellationToken ct)
    {
        var existing = await repo.GetAsync(instanceId, ct);
        if (existing == null) return Results.NotFound();

        var instance = existing with
        {
            ModelId = request.ModelId,
            DeviceId = request.DeviceId,
            Name = request.Name,
            Location = request.Location,
            InstallDate = request.InstallDate,
            AssetNumber = request.AssetNumber,
            UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await repo.UpdateAsync(instance, ct);
        return Results.Ok(new { success = true, data = instance });
    }

    private static async Task<IResult> DeleteMotorInstance(
        string instanceId,
        [FromServices] IMotorInstanceRepository repo,
        CancellationToken ct)
    {
        await repo.DeleteAsync(instanceId, ct);
        return Results.Ok(new { success = true });
    }

    // ========== 参数映射实现 ==========

    private static async Task<IResult> ListParameterMappings(
        string instanceId,
        [FromServices] IMotorParameterMappingRepository repo,
        CancellationToken ct)
    {
        var mappings = await repo.ListByInstanceAsync(instanceId, ct);
        return Results.Ok(new { success = true, data = mappings });
    }

    private static async Task<IResult> CreateParameterMapping(
        string instanceId,
        [FromBody] CreateParameterMappingRequest request,
        [FromServices] IMotorParameterMappingRepository repo,
        [FromServices] ITagRepository tagRepo,
        CancellationToken ct)
    {
        // 验证标签存在
        var tag = await tagRepo.GetAsync(request.TagId, ct);
        if (tag == null)
            return Results.BadRequest(new { error = $"标签不存在: {request.TagId}" });

        var mapping = new MotorParameterMapping
        {
            MappingId = Guid.NewGuid().ToString(),
            InstanceId = instanceId,
            Parameter = request.Parameter,
            TagId = request.TagId,
            ScaleFactor = request.ScaleFactor,
            Offset = request.Offset,
            UsedForDiagnosis = request.UsedForDiagnosis
        };

        await repo.CreateAsync(mapping, ct);
        return Results.Ok(new { success = true, data = mapping });
    }

    private static async Task<IResult> CreateParameterMappingBatch(
        string instanceId,
        [FromBody] List<CreateParameterMappingRequest> requests,
        [FromServices] IMotorParameterMappingRepository repo,
        CancellationToken ct)
    {
        var mappings = requests.Select(r => new MotorParameterMapping
        {
            MappingId = Guid.NewGuid().ToString(),
            InstanceId = instanceId,
            Parameter = r.Parameter,
            TagId = r.TagId,
            ScaleFactor = r.ScaleFactor,
            Offset = r.Offset,
            UsedForDiagnosis = r.UsedForDiagnosis
        }).ToList();

        await repo.CreateBatchAsync(mappings, ct);
        return Results.Ok(new { success = true, data = new { created = mappings.Count } });
    }

    private static async Task<IResult> DeleteParameterMapping(
        string instanceId,
        string mappingId,
        [FromServices] IMotorParameterMappingRepository repo,
        CancellationToken ct)
    {
        await repo.DeleteAsync(mappingId, ct);
        return Results.Ok(new { success = true });
    }

    // ========== 操作模式实现 ==========

    private static async Task<IResult> ListOperationModes(
        string instanceId,
        [FromQuery] bool? enabledOnly,
        [FromServices] IOperationModeRepository repo,
        CancellationToken ct)
    {
        var modes = enabledOnly == true
            ? await repo.ListEnabledByInstanceAsync(instanceId, ct)
            : await repo.ListByInstanceAsync(instanceId, ct);
        return Results.Ok(new { success = true, data = modes });
    }

    private static async Task<IResult> CreateOperationMode(
        string instanceId,
        [FromBody] CreateOperationModeRequest request,
        [FromServices] IOperationModeRepository repo,
        CancellationToken ct)
    {
        var mode = new OperationMode
        {
            ModeId = Guid.NewGuid().ToString(),
            InstanceId = instanceId,
            Name = request.Name,
            Description = request.Description,
            TriggerTagId = request.TriggerTagId,
            TriggerMinValue = request.TriggerMinValue,
            TriggerMaxValue = request.TriggerMaxValue,
            MinDurationMs = request.MinDurationMs,
            MaxDurationMs = request.MaxDurationMs,
            Priority = request.Priority,
            Enabled = true,
            CreatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await repo.CreateAsync(mode, ct);
        return Results.Ok(new { success = true, data = mode });
    }

    private static async Task<IResult> UpdateOperationMode(
        string instanceId,
        string modeId,
        [FromBody] CreateOperationModeRequest request,
        [FromServices] IOperationModeRepository repo,
        CancellationToken ct)
    {
        var existing = await repo.GetAsync(modeId, ct);
        if (existing == null) return Results.NotFound();

        var mode = existing with
        {
            Name = request.Name,
            Description = request.Description,
            TriggerTagId = request.TriggerTagId,
            TriggerMinValue = request.TriggerMinValue,
            TriggerMaxValue = request.TriggerMaxValue,
            MinDurationMs = request.MinDurationMs,
            MaxDurationMs = request.MaxDurationMs,
            Priority = request.Priority,
            UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await repo.UpdateAsync(mode, ct);
        return Results.Ok(new { success = true, data = mode });
    }

    private static async Task<IResult> DeleteOperationMode(
        string instanceId,
        string modeId,
        [FromServices] IOperationModeRepository repo,
        CancellationToken ct)
    {
        await repo.DeleteAsync(modeId, ct);
        return Results.Ok(new { success = true });
    }

    private static async Task<IResult> SetOperationModeEnabled(
        string instanceId,
        string modeId,
        [FromQuery] bool enabled,
        [FromServices] IOperationModeRepository repo,
        CancellationToken ct)
    {
        await repo.SetEnabledAsync(modeId, enabled, ct);
        return Results.Ok(new { success = true, data = new { modeId, enabled } });
    }

    // ========== 基线实现 ==========

    private static async Task<IResult> ListBaselines(
        string instanceId,
        [FromServices] IBaselineProfileRepository repo,
        CancellationToken ct)
    {
        var baselines = await repo.ListByInstanceAsync(instanceId, ct);
        return Results.Ok(new { success = true, data = baselines });
    }

    private static async Task<IResult> ListBaselinesByMode(
        string instanceId,
        string modeId,
        [FromServices] IBaselineProfileRepository repo,
        CancellationToken ct)
    {
        var baselines = await repo.ListByModeAsync(modeId, ct);
        return Results.Ok(new { success = true, data = baselines });
    }

    private static async Task<IResult> DeleteBaselinesByMode(
        string instanceId,
        string modeId,
        [FromServices] IBaselineProfileRepository repo,
        CancellationToken ct)
    {
        await repo.DeleteByModeAsync(modeId, ct);
        return Results.Ok(new { success = true });
    }

    // ========== 基线学习实现 ==========

    private static async Task<IResult> StartBaselineLearning(
        string instanceId,
        [FromBody] StartLearningRequest request,
        [FromServices] MotorBaselineLearningService learningService,
        CancellationToken ct)
    {
        try
        {
            var taskId = await learningService.StartLearningAsync(new MotorBaselineLearningRequest
            {
                InstanceId = instanceId,
                ModeId = request.ModeId,
                StartTs = request.StartTs,
                EndTs = request.EndTs,
                MinSamples = request.MinSamples
            }, ct);

            return Results.Ok(new { taskId, message = "Learning task started" });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> LearnAllModes(
        string instanceId,
        [FromBody] LearnAllModesRequest? request,
        [FromServices] MotorBaselineLearningService learningService,
        CancellationToken ct)
    {
        try
        {
            var taskId = await learningService.LearnAllModesAsync(
                instanceId,
                request?.StartTs,
                request?.EndTs,
                ct);

            return Results.Ok(new { success = true, data = new { taskId, message = "基线学习任务已启动" } });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Ok(new { success = false, error = ex.Message });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { success = false, error = $"启动学习失败: {ex.Message}" });
        }
    }

    private static Task<IResult> ListLearningTasks(
        string instanceId,
        [FromServices] MotorBaselineLearningService learningService)
    {
        var tasks = learningService.GetTasksByInstance(instanceId);
        return Task.FromResult(Results.Ok(new { success = true, data = tasks }));
    }

    private static Task<IResult> GetLearningTask(
        string instanceId,
        string taskId,
        [FromServices] MotorBaselineLearningService learningService)
    {
        var state = learningService.GetTaskState(taskId);
        return state != null
            ? Task.FromResult(Results.Ok(state))
            : Task.FromResult(Results.NotFound());
    }

    private static async Task<IResult> GetCurrentOperationMode(
        string instanceId,
        [FromServices] IMotorInstanceRepository instanceRepo,
        [FromServices] IMotorParameterMappingRepository mappingRepo,
        [FromServices] OperationModeDetector modeDetector,
        CancellationToken ct)
    {
        var instance = await instanceRepo.GetAsync(instanceId, ct);
        if (instance == null)
            return Results.NotFound();

        var mappings = await mappingRepo.ListByInstanceAsync(instanceId, ct);
        if (mappings.Count == 0)
            return Results.Ok(new { mode = (OperationMode?)null, message = "No parameter mappings configured" });

        var currentMode = await modeDetector.DetectModeFromTelemetryAsync(
            instanceId, instance.DeviceId, mappings, ct);

        var state = modeDetector.GetCurrentState(instanceId);

        return Results.Ok(new
        {
            mode = currentMode,
            state = state != null ? new { state.ModeId, state.EnterTime } : null
        });
    }

    // ========== 故障诊断实现 ==========

    private static async Task<IResult> DiagnoseInstance(
        string instanceId,
        [FromBody] DiagnoseRequest? request,
        [FromServices] MotorFaultDetectionService faultService,
        CancellationToken ct)
    {
        var config = request != null
            ? new FaultDetectionConfig
            {
                MinorThreshold = request.MinorThreshold ?? 2.0,
                ModerateThreshold = request.ModerateThreshold ?? 3.0,
                SevereThreshold = request.SevereThreshold ?? 4.0,
                CriticalThreshold = request.CriticalThreshold ?? 5.0,
                EnableAlarmGeneration = request.EnableAlarmGeneration ?? false
            }
            : new FaultDetectionConfig { EnableAlarmGeneration = false };

        var (result, errorReason) = await faultService.DiagnoseWithReasonAsync(instanceId, config, ct);

        if (result == null)
        {
            return Results.Ok(new { success = false, error = errorReason ?? "无法执行诊断，请检查实例配置和基线数据" });
        }

        return Results.Ok(new { success = true, data = result });
    }

    private static Task<IResult> GetLatestDiagnosis(
        string instanceId,
        [FromServices] MotorFaultDetectionService faultService)
    {
        var result = faultService.GetLatestResult(instanceId);
        return result != null
            ? Task.FromResult(Results.Ok(new { success = true, data = result }))
            : Task.FromResult(Results.Ok(new { success = false, error = "暂无诊断结果，请先执行诊断" }));
    }

    private static Task<IResult> GetAllDiagnoses(
        [FromServices] MotorFaultDetectionService faultService)
    {
        var results = faultService.GetAllLatestResults();
        return Task.FromResult(Results.Ok(new { success = true, data = results }));
    }
}

/// <summary>
/// 诊断请求
/// </summary>
public sealed class DiagnoseRequest
{
    /// <summary>轻微偏离阈值</summary>
    public double? MinorThreshold { get; init; }

    /// <summary>中度偏离阈值</summary>
    public double? ModerateThreshold { get; init; }

    /// <summary>严重偏离阈值</summary>
    public double? SevereThreshold { get; init; }

    /// <summary>危急偏离阈值</summary>
    public double? CriticalThreshold { get; init; }

    /// <summary>是否生成告警</summary>
    public bool? EnableAlarmGeneration { get; init; }
}

/// <summary>
/// 启动学习请求
/// </summary>
public sealed class StartLearningRequest
{
    /// <summary>操作模式ID</summary>
    public required string ModeId { get; init; }

    /// <summary>数据开始时间戳</summary>
    public long? StartTs { get; init; }

    /// <summary>数据结束时间戳</summary>
    public long? EndTs { get; init; }

    /// <summary>最小样本数</summary>
    public int? MinSamples { get; init; }
}

/// <summary>
/// 学习所有模式请求
/// </summary>
public sealed class LearnAllModesRequest
{
    /// <summary>数据开始时间戳</summary>
    public long? StartTs { get; init; }

    /// <summary>数据结束时间戳</summary>
    public long? EndTs { get; init; }
}
