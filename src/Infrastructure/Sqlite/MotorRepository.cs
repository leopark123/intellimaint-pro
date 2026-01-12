using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// v64: SQLite 电机模型仓储实现
/// </summary>
public sealed class MotorModelRepository : IMotorModelRepository
{
    private readonly IDbExecutor _db;
    private readonly ILogger<MotorModelRepository> _logger;

    public MotorModelRepository(IDbExecutor db, ILogger<MotorModelRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MotorModel>> ListAsync(CancellationToken ct)
    {
        var rows = await _db.QueryAsync(
            "SELECT * FROM motor_model ORDER BY name",
            MapFromReader, null, ct);
        return rows;
    }

    public async Task<MotorModel?> GetAsync(string modelId, CancellationToken ct)
    {
        return await _db.QuerySingleAsync(
            "SELECT * FROM motor_model WHERE model_id = @ModelId",
            MapFromReader, new { ModelId = modelId }, ct);
    }

    public async Task CreateAsync(MotorModel model, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(@"
            INSERT INTO motor_model (
                model_id, name, description, motor_type, rated_power, rated_voltage,
                rated_current, rated_speed, rated_frequency, pole_pairs, vfd_model,
                bearing_model, bearing_rolling_elements, bearing_ball_diameter,
                bearing_pitch_diameter, bearing_contact_angle, created_utc, updated_utc, created_by
            ) VALUES (
                @ModelId, @Name, @Description, @Type, @RatedPower, @RatedVoltage,
                @RatedCurrent, @RatedSpeed, @RatedFrequency, @PolePairs, @VfdModel,
                @BearingModel, @BearingRollingElements, @BearingBallDiameter,
                @BearingPitchDiameter, @BearingContactAngle, @CreatedUtc, @UpdatedUtc, @CreatedBy
            )",
            new
            {
                model.ModelId,
                model.Name,
                model.Description,
                Type = (int)model.Type,
                model.RatedPower,
                model.RatedVoltage,
                model.RatedCurrent,
                model.RatedSpeed,
                model.RatedFrequency,
                model.PolePairs,
                model.VfdModel,
                model.BearingModel,
                model.BearingRollingElements,
                model.BearingBallDiameter,
                model.BearingPitchDiameter,
                model.BearingContactAngle,
                model.CreatedUtc,
                model.UpdatedUtc,
                model.CreatedBy
            }, ct);
        _logger.LogDebug("Created motor model: {ModelId}", model.ModelId);
    }

    public async Task UpdateAsync(MotorModel model, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(@"
            UPDATE motor_model SET
                name = @Name, description = @Description, motor_type = @Type,
                rated_power = @RatedPower, rated_voltage = @RatedVoltage,
                rated_current = @RatedCurrent, rated_speed = @RatedSpeed,
                rated_frequency = @RatedFrequency, pole_pairs = @PolePairs,
                vfd_model = @VfdModel, bearing_model = @BearingModel,
                bearing_rolling_elements = @BearingRollingElements,
                bearing_ball_diameter = @BearingBallDiameter,
                bearing_pitch_diameter = @BearingPitchDiameter,
                bearing_contact_angle = @BearingContactAngle,
                updated_utc = @UpdatedUtc
            WHERE model_id = @ModelId",
            new
            {
                model.ModelId,
                model.Name,
                model.Description,
                Type = (int)model.Type,
                model.RatedPower,
                model.RatedVoltage,
                model.RatedCurrent,
                model.RatedSpeed,
                model.RatedFrequency,
                model.PolePairs,
                model.VfdModel,
                model.BearingModel,
                model.BearingRollingElements,
                model.BearingBallDiameter,
                model.BearingPitchDiameter,
                model.BearingContactAngle,
                UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }, ct);
    }

    public async Task DeleteAsync(string modelId, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(
            "DELETE FROM motor_model WHERE model_id = @ModelId",
            new { ModelId = modelId }, ct);
        _logger.LogDebug("Deleted motor model: {ModelId}", modelId);
    }

    private static MotorModel MapFromReader(SqliteDataReader r) => new()
    {
        ModelId = r.GetString(r.GetOrdinal("model_id")),
        Name = r.GetString(r.GetOrdinal("name")),
        Description = r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")),
        Type = (MotorType)r.GetInt32(r.GetOrdinal("motor_type")),
        RatedPower = r.IsDBNull(r.GetOrdinal("rated_power")) ? null : r.GetDouble(r.GetOrdinal("rated_power")),
        RatedVoltage = r.IsDBNull(r.GetOrdinal("rated_voltage")) ? null : r.GetDouble(r.GetOrdinal("rated_voltage")),
        RatedCurrent = r.IsDBNull(r.GetOrdinal("rated_current")) ? null : r.GetDouble(r.GetOrdinal("rated_current")),
        RatedSpeed = r.IsDBNull(r.GetOrdinal("rated_speed")) ? null : r.GetDouble(r.GetOrdinal("rated_speed")),
        RatedFrequency = r.IsDBNull(r.GetOrdinal("rated_frequency")) ? null : r.GetDouble(r.GetOrdinal("rated_frequency")),
        PolePairs = r.IsDBNull(r.GetOrdinal("pole_pairs")) ? null : r.GetInt32(r.GetOrdinal("pole_pairs")),
        VfdModel = r.IsDBNull(r.GetOrdinal("vfd_model")) ? null : r.GetString(r.GetOrdinal("vfd_model")),
        BearingModel = r.IsDBNull(r.GetOrdinal("bearing_model")) ? null : r.GetString(r.GetOrdinal("bearing_model")),
        BearingRollingElements = r.IsDBNull(r.GetOrdinal("bearing_rolling_elements")) ? null : r.GetInt32(r.GetOrdinal("bearing_rolling_elements")),
        BearingBallDiameter = r.IsDBNull(r.GetOrdinal("bearing_ball_diameter")) ? null : r.GetDouble(r.GetOrdinal("bearing_ball_diameter")),
        BearingPitchDiameter = r.IsDBNull(r.GetOrdinal("bearing_pitch_diameter")) ? null : r.GetDouble(r.GetOrdinal("bearing_pitch_diameter")),
        BearingContactAngle = r.IsDBNull(r.GetOrdinal("bearing_contact_angle")) ? null : r.GetDouble(r.GetOrdinal("bearing_contact_angle")),
        CreatedUtc = r.GetInt64(r.GetOrdinal("created_utc")),
        UpdatedUtc = r.IsDBNull(r.GetOrdinal("updated_utc")) ? null : r.GetInt64(r.GetOrdinal("updated_utc")),
        CreatedBy = r.IsDBNull(r.GetOrdinal("created_by")) ? null : r.GetString(r.GetOrdinal("created_by"))
    };
}

/// <summary>
/// v64: SQLite 电机实例仓储实现
/// </summary>
public sealed class MotorInstanceRepository : IMotorInstanceRepository
{
    private readonly IDbExecutor _db;
    private readonly IMotorModelRepository _modelRepo;
    private readonly IMotorParameterMappingRepository _mappingRepo;
    private readonly IOperationModeRepository _modeRepo;
    private readonly IBaselineProfileRepository _baselineRepo;
    private readonly ILogger<MotorInstanceRepository> _logger;

    public MotorInstanceRepository(
        IDbExecutor db,
        IMotorModelRepository modelRepo,
        IMotorParameterMappingRepository mappingRepo,
        IOperationModeRepository modeRepo,
        IBaselineProfileRepository baselineRepo,
        ILogger<MotorInstanceRepository> logger)
    {
        _db = db;
        _modelRepo = modelRepo;
        _mappingRepo = mappingRepo;
        _modeRepo = modeRepo;
        _baselineRepo = baselineRepo;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MotorInstance>> ListAsync(CancellationToken ct)
    {
        return await _db.QueryAsync(
            "SELECT * FROM motor_instance ORDER BY name",
            MapFromReader, null, ct);
    }

    public async Task<IReadOnlyList<MotorInstance>> ListByDeviceAsync(string deviceId, CancellationToken ct)
    {
        return await _db.QueryAsync(
            "SELECT * FROM motor_instance WHERE device_id = @DeviceId ORDER BY name",
            MapFromReader, new { DeviceId = deviceId }, ct);
    }

    public async Task<IReadOnlyList<MotorInstance>> ListByModelAsync(string modelId, CancellationToken ct)
    {
        return await _db.QueryAsync(
            "SELECT * FROM motor_instance WHERE model_id = @ModelId ORDER BY name",
            MapFromReader, new { ModelId = modelId }, ct);
    }

    public async Task<MotorInstance?> GetAsync(string instanceId, CancellationToken ct)
    {
        return await _db.QuerySingleAsync(
            "SELECT * FROM motor_instance WHERE instance_id = @InstanceId",
            MapFromReader, new { InstanceId = instanceId }, ct);
    }

    public async Task<MotorInstanceDetail?> GetDetailAsync(string instanceId, CancellationToken ct)
    {
        var instance = await GetAsync(instanceId, ct);
        if (instance == null) return null;

        var model = await _modelRepo.GetAsync(instance.ModelId, ct);
        var mappings = await _mappingRepo.ListByInstanceAsync(instanceId, ct);
        var modes = await _modeRepo.ListByInstanceAsync(instanceId, ct);
        var baselineCount = await _baselineRepo.GetCountByInstanceAsync(instanceId, ct);

        return new MotorInstanceDetail
        {
            Instance = instance,
            Model = model,
            Mappings = mappings,
            Modes = modes,
            BaselineCount = baselineCount
        };
    }

    public async Task CreateAsync(MotorInstance instance, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(@"
            INSERT INTO motor_instance (
                instance_id, model_id, device_id, name, location,
                install_date, operating_hours, asset_number, diagnosis_enabled,
                created_utc, updated_utc
            ) VALUES (
                @InstanceId, @ModelId, @DeviceId, @Name, @Location,
                @InstallDate, @OperatingHours, @AssetNumber, @DiagnosisEnabled,
                @CreatedUtc, @UpdatedUtc
            )",
            new
            {
                instance.InstanceId,
                instance.ModelId,
                instance.DeviceId,
                instance.Name,
                instance.Location,
                instance.InstallDate,
                instance.OperatingHours,
                instance.AssetNumber,
                DiagnosisEnabled = instance.DiagnosisEnabled ? 1 : 0,
                instance.CreatedUtc,
                instance.UpdatedUtc
            }, ct);
        _logger.LogDebug("Created motor instance: {InstanceId}", instance.InstanceId);
    }

    public async Task UpdateAsync(MotorInstance instance, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(@"
            UPDATE motor_instance SET
                model_id = @ModelId, device_id = @DeviceId, name = @Name,
                location = @Location, install_date = @InstallDate,
                operating_hours = @OperatingHours, asset_number = @AssetNumber,
                diagnosis_enabled = @DiagnosisEnabled, updated_utc = @UpdatedUtc
            WHERE instance_id = @InstanceId",
            new
            {
                instance.InstanceId,
                instance.ModelId,
                instance.DeviceId,
                instance.Name,
                instance.Location,
                instance.InstallDate,
                instance.OperatingHours,
                instance.AssetNumber,
                DiagnosisEnabled = instance.DiagnosisEnabled ? 1 : 0,
                UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }, ct);
    }

    public async Task DeleteAsync(string instanceId, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(
            "DELETE FROM motor_instance WHERE instance_id = @InstanceId",
            new { InstanceId = instanceId }, ct);
        _logger.LogDebug("Deleted motor instance: {InstanceId}", instanceId);
    }

    public async Task UpdateOperatingHoursAsync(string instanceId, double hours, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(@"
            UPDATE motor_instance SET
                operating_hours = @Hours, updated_utc = @UpdatedUtc
            WHERE instance_id = @InstanceId",
            new
            {
                InstanceId = instanceId,
                Hours = hours,
                UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }, ct);
    }

    private static MotorInstance MapFromReader(SqliteDataReader r) => new()
    {
        InstanceId = r.GetString(r.GetOrdinal("instance_id")),
        ModelId = r.GetString(r.GetOrdinal("model_id")),
        DeviceId = r.GetString(r.GetOrdinal("device_id")),
        Name = r.GetString(r.GetOrdinal("name")),
        Location = r.IsDBNull(r.GetOrdinal("location")) ? null : r.GetString(r.GetOrdinal("location")),
        InstallDate = r.IsDBNull(r.GetOrdinal("install_date")) ? null : r.GetString(r.GetOrdinal("install_date")),
        OperatingHours = r.IsDBNull(r.GetOrdinal("operating_hours")) ? null : r.GetDouble(r.GetOrdinal("operating_hours")),
        AssetNumber = r.IsDBNull(r.GetOrdinal("asset_number")) ? null : r.GetString(r.GetOrdinal("asset_number")),
        DiagnosisEnabled = r.GetInt32(r.GetOrdinal("diagnosis_enabled")) == 1,
        CreatedUtc = r.GetInt64(r.GetOrdinal("created_utc")),
        UpdatedUtc = r.IsDBNull(r.GetOrdinal("updated_utc")) ? null : r.GetInt64(r.GetOrdinal("updated_utc"))
    };
}

/// <summary>
/// v64: SQLite 参数映射仓储实现
/// </summary>
public sealed class MotorParameterMappingRepository : IMotorParameterMappingRepository
{
    private readonly IDbExecutor _db;
    private readonly ILogger<MotorParameterMappingRepository> _logger;

    public MotorParameterMappingRepository(IDbExecutor db, ILogger<MotorParameterMappingRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MotorParameterMapping>> ListByInstanceAsync(string instanceId, CancellationToken ct)
    {
        return await _db.QueryAsync(
            "SELECT * FROM motor_parameter_mapping WHERE instance_id = @InstanceId ORDER BY parameter",
            MapFromReader, new { InstanceId = instanceId }, ct);
    }

    public async Task<MotorParameterMapping?> GetAsync(string mappingId, CancellationToken ct)
    {
        return await _db.QuerySingleAsync(
            "SELECT * FROM motor_parameter_mapping WHERE mapping_id = @MappingId",
            MapFromReader, new { MappingId = mappingId }, ct);
    }

    public async Task CreateAsync(MotorParameterMapping mapping, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(@"
            INSERT INTO motor_parameter_mapping (
                mapping_id, instance_id, parameter, tag_id,
                scale_factor, offset, used_for_diagnosis
            ) VALUES (
                @MappingId, @InstanceId, @Parameter, @TagId,
                @ScaleFactor, @Offset, @UsedForDiagnosis
            )",
            new
            {
                mapping.MappingId,
                mapping.InstanceId,
                Parameter = (int)mapping.Parameter,
                mapping.TagId,
                mapping.ScaleFactor,
                mapping.Offset,
                UsedForDiagnosis = mapping.UsedForDiagnosis ? 1 : 0
            }, ct);
    }

    public async Task CreateBatchAsync(IEnumerable<MotorParameterMapping> mappings, CancellationToken ct)
    {
        var paramsList = mappings.Select(m => new
        {
            m.MappingId,
            m.InstanceId,
            Parameter = (int)m.Parameter,
            m.TagId,
            m.ScaleFactor,
            m.Offset,
            UsedForDiagnosis = m.UsedForDiagnosis ? 1 : 0
        });

        await _db.ExecuteBatchAsync(@"
            INSERT INTO motor_parameter_mapping (
                mapping_id, instance_id, parameter, tag_id,
                scale_factor, offset, used_for_diagnosis
            ) VALUES (
                @MappingId, @InstanceId, @Parameter, @TagId,
                @ScaleFactor, @Offset, @UsedForDiagnosis
            )",
            paramsList, ct);
    }

    public async Task UpdateAsync(MotorParameterMapping mapping, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(@"
            UPDATE motor_parameter_mapping SET
                parameter = @Parameter, tag_id = @TagId,
                scale_factor = @ScaleFactor, offset = @Offset,
                used_for_diagnosis = @UsedForDiagnosis
            WHERE mapping_id = @MappingId",
            new
            {
                mapping.MappingId,
                Parameter = (int)mapping.Parameter,
                mapping.TagId,
                mapping.ScaleFactor,
                mapping.Offset,
                UsedForDiagnosis = mapping.UsedForDiagnosis ? 1 : 0
            }, ct);
    }

    public async Task DeleteAsync(string mappingId, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(
            "DELETE FROM motor_parameter_mapping WHERE mapping_id = @MappingId",
            new { MappingId = mappingId }, ct);
    }

    public async Task DeleteByInstanceAsync(string instanceId, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(
            "DELETE FROM motor_parameter_mapping WHERE instance_id = @InstanceId",
            new { InstanceId = instanceId }, ct);
    }

    private static MotorParameterMapping MapFromReader(SqliteDataReader r) => new()
    {
        MappingId = r.GetString(r.GetOrdinal("mapping_id")),
        InstanceId = r.GetString(r.GetOrdinal("instance_id")),
        Parameter = (MotorParameter)r.GetInt32(r.GetOrdinal("parameter")),
        TagId = r.GetString(r.GetOrdinal("tag_id")),
        ScaleFactor = r.GetDouble(r.GetOrdinal("scale_factor")),
        Offset = r.GetDouble(r.GetOrdinal("offset")),
        UsedForDiagnosis = r.GetInt32(r.GetOrdinal("used_for_diagnosis")) == 1
    };
}

/// <summary>
/// v64: SQLite 操作模式仓储实现
/// </summary>
public sealed class OperationModeRepository : IOperationModeRepository
{
    private readonly IDbExecutor _db;
    private readonly ILogger<OperationModeRepository> _logger;

    public OperationModeRepository(IDbExecutor db, ILogger<OperationModeRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<OperationMode>> ListByInstanceAsync(string instanceId, CancellationToken ct)
    {
        return await _db.QueryAsync(
            "SELECT * FROM operation_mode WHERE instance_id = @InstanceId ORDER BY priority DESC, name",
            MapFromReader, new { InstanceId = instanceId }, ct);
    }

    public async Task<IReadOnlyList<OperationMode>> ListEnabledByInstanceAsync(string instanceId, CancellationToken ct)
    {
        return await _db.QueryAsync(
            "SELECT * FROM operation_mode WHERE instance_id = @InstanceId AND enabled = 1 ORDER BY priority DESC, name",
            MapFromReader, new { InstanceId = instanceId }, ct);
    }

    public async Task<OperationMode?> GetAsync(string modeId, CancellationToken ct)
    {
        return await _db.QuerySingleAsync(
            "SELECT * FROM operation_mode WHERE mode_id = @ModeId",
            MapFromReader, new { ModeId = modeId }, ct);
    }

    public async Task CreateAsync(OperationMode mode, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(@"
            INSERT INTO operation_mode (
                mode_id, instance_id, name, description,
                trigger_tag_id, trigger_min_value, trigger_max_value,
                min_duration_ms, max_duration_ms, priority, enabled,
                created_utc, updated_utc
            ) VALUES (
                @ModeId, @InstanceId, @Name, @Description,
                @TriggerTagId, @TriggerMinValue, @TriggerMaxValue,
                @MinDurationMs, @MaxDurationMs, @Priority, @Enabled,
                @CreatedUtc, @UpdatedUtc
            )",
            new
            {
                mode.ModeId,
                mode.InstanceId,
                mode.Name,
                mode.Description,
                mode.TriggerTagId,
                mode.TriggerMinValue,
                mode.TriggerMaxValue,
                mode.MinDurationMs,
                mode.MaxDurationMs,
                mode.Priority,
                Enabled = mode.Enabled ? 1 : 0,
                mode.CreatedUtc,
                mode.UpdatedUtc
            }, ct);
    }

    public async Task UpdateAsync(OperationMode mode, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(@"
            UPDATE operation_mode SET
                name = @Name, description = @Description,
                trigger_tag_id = @TriggerTagId, trigger_min_value = @TriggerMinValue,
                trigger_max_value = @TriggerMaxValue, min_duration_ms = @MinDurationMs,
                max_duration_ms = @MaxDurationMs, priority = @Priority, enabled = @Enabled,
                updated_utc = @UpdatedUtc
            WHERE mode_id = @ModeId",
            new
            {
                mode.ModeId,
                mode.Name,
                mode.Description,
                mode.TriggerTagId,
                mode.TriggerMinValue,
                mode.TriggerMaxValue,
                mode.MinDurationMs,
                mode.MaxDurationMs,
                mode.Priority,
                Enabled = mode.Enabled ? 1 : 0,
                UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }, ct);
    }

    public async Task DeleteAsync(string modeId, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(
            "DELETE FROM operation_mode WHERE mode_id = @ModeId",
            new { ModeId = modeId }, ct);
    }

    public async Task SetEnabledAsync(string modeId, bool enabled, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(@"
            UPDATE operation_mode SET enabled = @Enabled, updated_utc = @UpdatedUtc WHERE mode_id = @ModeId",
            new { ModeId = modeId, Enabled = enabled ? 1 : 0, UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }, ct);
    }

    public async Task DeleteByInstanceAsync(string instanceId, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(
            "DELETE FROM operation_mode WHERE instance_id = @InstanceId",
            new { InstanceId = instanceId }, ct);
    }

    private static OperationMode MapFromReader(SqliteDataReader r) => new()
    {
        ModeId = r.GetString(r.GetOrdinal("mode_id")),
        InstanceId = r.GetString(r.GetOrdinal("instance_id")),
        Name = r.GetString(r.GetOrdinal("name")),
        Description = r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")),
        TriggerTagId = r.IsDBNull(r.GetOrdinal("trigger_tag_id")) ? null : r.GetString(r.GetOrdinal("trigger_tag_id")),
        TriggerMinValue = r.IsDBNull(r.GetOrdinal("trigger_min_value")) ? null : r.GetDouble(r.GetOrdinal("trigger_min_value")),
        TriggerMaxValue = r.IsDBNull(r.GetOrdinal("trigger_max_value")) ? null : r.GetDouble(r.GetOrdinal("trigger_max_value")),
        MinDurationMs = r.GetInt32(r.GetOrdinal("min_duration_ms")),
        MaxDurationMs = r.GetInt32(r.GetOrdinal("max_duration_ms")),
        Priority = r.GetInt32(r.GetOrdinal("priority")),
        Enabled = r.GetInt32(r.GetOrdinal("enabled")) == 1,
        CreatedUtc = r.GetInt64(r.GetOrdinal("created_utc")),
        UpdatedUtc = r.IsDBNull(r.GetOrdinal("updated_utc")) ? null : r.GetInt64(r.GetOrdinal("updated_utc"))
    };
}

/// <summary>
/// v64: SQLite 基线配置仓储实现
/// </summary>
public sealed class BaselineProfileRepository : IBaselineProfileRepository
{
    private readonly IDbExecutor _db;
    private readonly ILogger<BaselineProfileRepository> _logger;

    public BaselineProfileRepository(IDbExecutor db, ILogger<BaselineProfileRepository> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BaselineProfile>> ListByModeAsync(string modeId, CancellationToken ct)
    {
        return await _db.QueryAsync(
            "SELECT * FROM baseline_profile WHERE mode_id = @ModeId ORDER BY parameter",
            MapFromReader, new { ModeId = modeId }, ct);
    }

    public async Task<IReadOnlyList<BaselineProfile>> ListByInstanceAsync(string instanceId, CancellationToken ct)
    {
        return await _db.QueryAsync(@"
            SELECT bp.* FROM baseline_profile bp
            INNER JOIN operation_mode om ON bp.mode_id = om.mode_id
            WHERE om.instance_id = @InstanceId ORDER BY om.name, bp.parameter",
            MapFromReader, new { InstanceId = instanceId }, ct);
    }

    public async Task<BaselineProfile?> GetAsync(string baselineId, CancellationToken ct)
    {
        return await _db.QuerySingleAsync(
            "SELECT * FROM baseline_profile WHERE baseline_id = @BaselineId",
            MapFromReader, new { BaselineId = baselineId }, ct);
    }

    public async Task<BaselineProfile?> GetByModeAndParameterAsync(string modeId, MotorParameter parameter, CancellationToken ct)
    {
        return await _db.QuerySingleAsync(
            "SELECT * FROM baseline_profile WHERE mode_id = @ModeId AND parameter = @Parameter",
            MapFromReader, new { ModeId = modeId, Parameter = (int)parameter }, ct);
    }

    public async Task CreateAsync(BaselineProfile baseline, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(@"
            INSERT INTO baseline_profile (
                baseline_id, mode_id, parameter, mean, std_dev,
                min_value, max_value, percentile_05, percentile_95, median,
                frequency_profile_json, sample_count, learned_from_utc, learned_to_utc,
                confidence_level, version, created_utc, updated_utc
            ) VALUES (
                @BaselineId, @ModeId, @Parameter, @Mean, @StdDev,
                @MinValue, @MaxValue, @Percentile05, @Percentile95, @Median,
                @FrequencyProfileJson, @SampleCount, @LearnedFromUtc, @LearnedToUtc,
                @ConfidenceLevel, @Version, @CreatedUtc, @UpdatedUtc
            )",
            new
            {
                baseline.BaselineId,
                baseline.ModeId,
                Parameter = (int)baseline.Parameter,
                baseline.Mean,
                baseline.StdDev,
                baseline.MinValue,
                baseline.MaxValue,
                baseline.Percentile05,
                baseline.Percentile95,
                baseline.Median,
                baseline.FrequencyProfileJson,
                baseline.SampleCount,
                baseline.LearnedFromUtc,
                baseline.LearnedToUtc,
                baseline.ConfidenceLevel,
                baseline.Version,
                baseline.CreatedUtc,
                baseline.UpdatedUtc
            }, ct);
    }

    public async Task SaveBatchAsync(IEnumerable<BaselineProfile> baselines, CancellationToken ct)
    {
        foreach (var baseline in baselines)
        {
            // Upsert by mode_id + parameter
            await _db.ExecuteNonQueryAsync(@"
                INSERT INTO baseline_profile (
                    baseline_id, mode_id, parameter, mean, std_dev,
                    min_value, max_value, percentile_05, percentile_95, median,
                    frequency_profile_json, sample_count, learned_from_utc, learned_to_utc,
                    confidence_level, version, created_utc, updated_utc
                ) VALUES (
                    @BaselineId, @ModeId, @Parameter, @Mean, @StdDev,
                    @MinValue, @MaxValue, @Percentile05, @Percentile95, @Median,
                    @FrequencyProfileJson, @SampleCount, @LearnedFromUtc, @LearnedToUtc,
                    @ConfidenceLevel, @Version, @CreatedUtc, @UpdatedUtc
                )
                ON CONFLICT(mode_id, parameter) DO UPDATE SET
                    baseline_id = excluded.baseline_id, mean = excluded.mean,
                    std_dev = excluded.std_dev, min_value = excluded.min_value,
                    max_value = excluded.max_value, percentile_05 = excluded.percentile_05,
                    percentile_95 = excluded.percentile_95, median = excluded.median,
                    frequency_profile_json = excluded.frequency_profile_json,
                    sample_count = excluded.sample_count, learned_from_utc = excluded.learned_from_utc,
                    learned_to_utc = excluded.learned_to_utc, confidence_level = excluded.confidence_level,
                    version = baseline_profile.version + 1, updated_utc = excluded.updated_utc",
                new
                {
                    baseline.BaselineId,
                    baseline.ModeId,
                    Parameter = (int)baseline.Parameter,
                    baseline.Mean,
                    baseline.StdDev,
                    baseline.MinValue,
                    baseline.MaxValue,
                    baseline.Percentile05,
                    baseline.Percentile95,
                    baseline.Median,
                    baseline.FrequencyProfileJson,
                    baseline.SampleCount,
                    baseline.LearnedFromUtc,
                    baseline.LearnedToUtc,
                    baseline.ConfidenceLevel,
                    baseline.Version,
                    baseline.CreatedUtc,
                    UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }, ct);
        }
        _logger.LogDebug("Saved {Count} baseline profiles", baselines.Count());
    }

    public async Task UpdateAsync(BaselineProfile baseline, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(@"
            UPDATE baseline_profile SET
                mean = @Mean, std_dev = @StdDev, min_value = @MinValue, max_value = @MaxValue,
                percentile_05 = @Percentile05, percentile_95 = @Percentile95, median = @Median,
                frequency_profile_json = @FrequencyProfileJson, sample_count = @SampleCount,
                learned_from_utc = @LearnedFromUtc, learned_to_utc = @LearnedToUtc,
                confidence_level = @ConfidenceLevel, version = version + 1, updated_utc = @UpdatedUtc
            WHERE baseline_id = @BaselineId",
            new
            {
                baseline.BaselineId,
                baseline.Mean,
                baseline.StdDev,
                baseline.MinValue,
                baseline.MaxValue,
                baseline.Percentile05,
                baseline.Percentile95,
                baseline.Median,
                baseline.FrequencyProfileJson,
                baseline.SampleCount,
                baseline.LearnedFromUtc,
                baseline.LearnedToUtc,
                baseline.ConfidenceLevel,
                UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }, ct);
    }

    public async Task DeleteAsync(string baselineId, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(
            "DELETE FROM baseline_profile WHERE baseline_id = @BaselineId",
            new { BaselineId = baselineId }, ct);
    }

    public async Task DeleteByModeAsync(string modeId, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(
            "DELETE FROM baseline_profile WHERE mode_id = @ModeId",
            new { ModeId = modeId }, ct);
    }

    public async Task DeleteByInstanceAsync(string instanceId, CancellationToken ct)
    {
        await _db.ExecuteNonQueryAsync(@"
            DELETE FROM baseline_profile WHERE mode_id IN (
                SELECT mode_id FROM operation_mode WHERE instance_id = @InstanceId
            )",
            new { InstanceId = instanceId }, ct);
    }

    public async Task<int> GetCountByInstanceAsync(string instanceId, CancellationToken ct)
    {
        var result = await _db.ExecuteScalarAsync<int?>(@"
            SELECT COUNT(*) FROM baseline_profile bp
            INNER JOIN operation_mode om ON bp.mode_id = om.mode_id
            WHERE om.instance_id = @InstanceId",
            new { InstanceId = instanceId }, ct);
        return result ?? 0;
    }

    private static BaselineProfile MapFromReader(SqliteDataReader r) => new()
    {
        BaselineId = r.GetString(r.GetOrdinal("baseline_id")),
        ModeId = r.GetString(r.GetOrdinal("mode_id")),
        Parameter = (MotorParameter)r.GetInt32(r.GetOrdinal("parameter")),
        Mean = r.GetDouble(r.GetOrdinal("mean")),
        StdDev = r.GetDouble(r.GetOrdinal("std_dev")),
        MinValue = r.GetDouble(r.GetOrdinal("min_value")),
        MaxValue = r.GetDouble(r.GetOrdinal("max_value")),
        Percentile05 = r.IsDBNull(r.GetOrdinal("percentile_05")) ? 0 : r.GetDouble(r.GetOrdinal("percentile_05")),
        Percentile95 = r.IsDBNull(r.GetOrdinal("percentile_95")) ? 0 : r.GetDouble(r.GetOrdinal("percentile_95")),
        Median = r.IsDBNull(r.GetOrdinal("median")) ? 0 : r.GetDouble(r.GetOrdinal("median")),
        FrequencyProfileJson = r.IsDBNull(r.GetOrdinal("frequency_profile_json")) ? null : r.GetString(r.GetOrdinal("frequency_profile_json")),
        SampleCount = r.GetInt32(r.GetOrdinal("sample_count")),
        LearnedFromUtc = r.GetInt64(r.GetOrdinal("learned_from_utc")),
        LearnedToUtc = r.GetInt64(r.GetOrdinal("learned_to_utc")),
        ConfidenceLevel = r.IsDBNull(r.GetOrdinal("confidence_level")) ? 0 : r.GetDouble(r.GetOrdinal("confidence_level")),
        Version = r.GetInt32(r.GetOrdinal("version")),
        CreatedUtc = r.GetInt64(r.GetOrdinal("created_utc")),
        UpdatedUtc = r.IsDBNull(r.GetOrdinal("updated_utc")) ? null : r.GetInt64(r.GetOrdinal("updated_utc"))
    };
}
