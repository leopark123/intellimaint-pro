using Dapper;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// v64: TimescaleDB 电机模型仓储实现
/// </summary>
public sealed class MotorModelRepository : IMotorModelRepository
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<MotorModelRepository> _logger;

    public MotorModelRepository(INpgsqlConnectionFactory factory, ILogger<MotorModelRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MotorModel>> ListAsync(CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<MotorModelRow>(
            new CommandDefinition("SELECT * FROM motor_model ORDER BY name", cancellationToken: ct));
        return rows.Select(MapFromRow).ToList();
    }

    public async Task<MotorModel?> GetAsync(string modelId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync<MotorModelRow>(
            new CommandDefinition(
                "SELECT * FROM motor_model WHERE model_id = @modelId",
                new { modelId }, cancellationToken: ct));
        return row != null ? MapFromRow(row) : null;
    }

    public async Task CreateAsync(MotorModel model, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(@"
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
            }, cancellationToken: ct));
        _logger.LogDebug("Created motor model: {ModelId}", model.ModelId);
    }

    public async Task UpdateAsync(MotorModel model, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(@"
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
            }, cancellationToken: ct));
    }

    public async Task DeleteAsync(string modelId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM motor_model WHERE model_id = @modelId",
            new { modelId }, cancellationToken: ct));
        _logger.LogDebug("Deleted motor model: {ModelId}", modelId);
    }

    private static MotorModel MapFromRow(MotorModelRow row) => new()
    {
        ModelId = row.model_id,
        Name = row.name,
        Description = row.description,
        Type = (MotorType)row.motor_type,
        RatedPower = row.rated_power,
        RatedVoltage = row.rated_voltage,
        RatedCurrent = row.rated_current,
        RatedSpeed = row.rated_speed,
        RatedFrequency = row.rated_frequency,
        PolePairs = row.pole_pairs,
        VfdModel = row.vfd_model,
        BearingModel = row.bearing_model,
        BearingRollingElements = row.bearing_rolling_elements,
        BearingBallDiameter = row.bearing_ball_diameter,
        BearingPitchDiameter = row.bearing_pitch_diameter,
        BearingContactAngle = row.bearing_contact_angle,
        CreatedUtc = row.created_utc,
        UpdatedUtc = row.updated_utc,
        CreatedBy = row.created_by
    };

    private sealed class MotorModelRow
    {
        public string model_id { get; set; } = "";
        public string name { get; set; } = "";
        public string? description { get; set; }
        public int motor_type { get; set; }
        public double? rated_power { get; set; }
        public double? rated_voltage { get; set; }
        public double? rated_current { get; set; }
        public double? rated_speed { get; set; }
        public double? rated_frequency { get; set; }
        public int? pole_pairs { get; set; }
        public string? vfd_model { get; set; }
        public string? bearing_model { get; set; }
        public int? bearing_rolling_elements { get; set; }
        public double? bearing_ball_diameter { get; set; }
        public double? bearing_pitch_diameter { get; set; }
        public double? bearing_contact_angle { get; set; }
        public long created_utc { get; set; }
        public long? updated_utc { get; set; }
        public string? created_by { get; set; }
    }
}

/// <summary>
/// v64: TimescaleDB 电机实例仓储实现
/// </summary>
public sealed class MotorInstanceRepository : IMotorInstanceRepository
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly IMotorModelRepository _modelRepo;
    private readonly IMotorParameterMappingRepository _mappingRepo;
    private readonly IOperationModeRepository _modeRepo;
    private readonly IBaselineProfileRepository _baselineRepo;
    private readonly ILogger<MotorInstanceRepository> _logger;

    public MotorInstanceRepository(
        INpgsqlConnectionFactory factory,
        IMotorModelRepository modelRepo,
        IMotorParameterMappingRepository mappingRepo,
        IOperationModeRepository modeRepo,
        IBaselineProfileRepository baselineRepo,
        ILogger<MotorInstanceRepository> logger)
    {
        _factory = factory;
        _modelRepo = modelRepo;
        _mappingRepo = mappingRepo;
        _modeRepo = modeRepo;
        _baselineRepo = baselineRepo;
        _logger = logger;
    }

    public async Task<IReadOnlyList<MotorInstance>> ListAsync(CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<MotorInstanceRow>(
            new CommandDefinition("SELECT * FROM motor_instance ORDER BY name", cancellationToken: ct));
        return rows.Select(MapFromRow).ToList();
    }

    public async Task<IReadOnlyList<MotorInstance>> ListByDeviceAsync(string deviceId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<MotorInstanceRow>(
            new CommandDefinition(
                "SELECT * FROM motor_instance WHERE device_id = @deviceId ORDER BY name",
                new { deviceId }, cancellationToken: ct));
        return rows.Select(MapFromRow).ToList();
    }

    public async Task<IReadOnlyList<MotorInstance>> ListByModelAsync(string modelId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<MotorInstanceRow>(
            new CommandDefinition(
                "SELECT * FROM motor_instance WHERE model_id = @modelId ORDER BY name",
                new { modelId }, cancellationToken: ct));
        return rows.Select(MapFromRow).ToList();
    }

    public async Task<MotorInstance?> GetAsync(string instanceId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync<MotorInstanceRow>(
            new CommandDefinition(
                "SELECT * FROM motor_instance WHERE instance_id = @instanceId",
                new { instanceId }, cancellationToken: ct));
        return row != null ? MapFromRow(row) : null;
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
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO motor_instance (
                instance_id, model_id, device_id, name, location,
                install_date, operating_hours, asset_number, diagnosis_enabled, created_utc, updated_utc
            ) VALUES (
                @InstanceId, @ModelId, @DeviceId, @Name, @Location,
                @InstallDate, @OperatingHours, @AssetNumber, @DiagnosisEnabled, @CreatedUtc, @UpdatedUtc
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
                instance.DiagnosisEnabled,
                instance.CreatedUtc,
                instance.UpdatedUtc
            }, cancellationToken: ct));
        _logger.LogDebug("Created motor instance: {InstanceId}", instance.InstanceId);
    }

    public async Task UpdateAsync(MotorInstance instance, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(@"
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
                instance.DiagnosisEnabled,
                UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }, cancellationToken: ct));
    }

    public async Task DeleteAsync(string instanceId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM motor_instance WHERE instance_id = @instanceId",
            new { instanceId }, cancellationToken: ct));
        _logger.LogDebug("Deleted motor instance: {InstanceId}", instanceId);
    }

    public async Task UpdateOperatingHoursAsync(string instanceId, double hours, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(@"
            UPDATE motor_instance SET operating_hours = @hours, updated_utc = @updatedUtc
            WHERE instance_id = @instanceId",
            new { instanceId, hours, updatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
            cancellationToken: ct));
    }

    private static MotorInstance MapFromRow(MotorInstanceRow row) => new()
    {
        InstanceId = row.instance_id,
        ModelId = row.model_id,
        DeviceId = row.device_id,
        Name = row.name,
        Location = row.location,
        InstallDate = row.install_date,
        OperatingHours = row.operating_hours,
        AssetNumber = row.asset_number,
        DiagnosisEnabled = row.diagnosis_enabled,
        CreatedUtc = row.created_utc,
        UpdatedUtc = row.updated_utc
    };

    private sealed class MotorInstanceRow
    {
        public string instance_id { get; set; } = "";
        public string model_id { get; set; } = "";
        public string device_id { get; set; } = "";
        public string name { get; set; } = "";
        public string? location { get; set; }
        public string? install_date { get; set; }
        public double? operating_hours { get; set; }
        public string? asset_number { get; set; }
        public bool diagnosis_enabled { get; set; }
        public long created_utc { get; set; }
        public long? updated_utc { get; set; }
    }
}

/// <summary>
/// v64: TimescaleDB 参数映射仓储实现
/// </summary>
public sealed class MotorParameterMappingRepository : IMotorParameterMappingRepository
{
    private readonly INpgsqlConnectionFactory _factory;

    public MotorParameterMappingRepository(INpgsqlConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<MotorParameterMapping>> ListByInstanceAsync(string instanceId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<MappingRow>(
            new CommandDefinition(
                "SELECT * FROM motor_parameter_mapping WHERE instance_id = @instanceId ORDER BY parameter",
                new { instanceId }, cancellationToken: ct));
        return rows.Select(MapFromRow).ToList();
    }

    public async Task<MotorParameterMapping?> GetAsync(string mappingId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync<MappingRow>(
            new CommandDefinition(
                "SELECT * FROM motor_parameter_mapping WHERE mapping_id = @mappingId",
                new { mappingId }, cancellationToken: ct));
        return row != null ? MapFromRow(row) : null;
    }

    public async Task CreateAsync(MotorParameterMapping mapping, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO motor_parameter_mapping (
                mapping_id, instance_id, parameter, tag_id, scale_factor, offset_value, used_for_diagnosis
            ) VALUES (@MappingId, @InstanceId, @Parameter, @TagId, @ScaleFactor, @Offset, @UsedForDiagnosis)",
            new
            {
                mapping.MappingId,
                mapping.InstanceId,
                Parameter = (int)mapping.Parameter,
                mapping.TagId,
                mapping.ScaleFactor,
                mapping.Offset,
                mapping.UsedForDiagnosis
            }, cancellationToken: ct));
    }

    public async Task CreateBatchAsync(IEnumerable<MotorParameterMapping> mappings, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        using var transaction = conn.BeginTransaction();
        try
        {
            foreach (var m in mappings)
            {
                await conn.ExecuteAsync(new CommandDefinition(@"
                    INSERT INTO motor_parameter_mapping (
                        mapping_id, instance_id, parameter, tag_id, scale_factor, offset_value, used_for_diagnosis
                    ) VALUES (@MappingId, @InstanceId, @Parameter, @TagId, @ScaleFactor, @Offset, @UsedForDiagnosis)",
                    new { m.MappingId, m.InstanceId, Parameter = (int)m.Parameter, m.TagId, m.ScaleFactor, m.Offset, m.UsedForDiagnosis },
                    transaction, cancellationToken: ct));
            }
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task UpdateAsync(MotorParameterMapping mapping, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(@"
            UPDATE motor_parameter_mapping SET
                parameter = @Parameter, tag_id = @TagId, scale_factor = @ScaleFactor,
                offset_value = @Offset, used_for_diagnosis = @UsedForDiagnosis
            WHERE mapping_id = @MappingId",
            new { mapping.MappingId, Parameter = (int)mapping.Parameter, mapping.TagId, mapping.ScaleFactor, mapping.Offset, mapping.UsedForDiagnosis },
            cancellationToken: ct));
    }

    public async Task DeleteAsync(string mappingId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM motor_parameter_mapping WHERE mapping_id = @mappingId",
            new { mappingId }, cancellationToken: ct));
    }

    public async Task DeleteByInstanceAsync(string instanceId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM motor_parameter_mapping WHERE instance_id = @instanceId",
            new { instanceId }, cancellationToken: ct));
    }

    private static MotorParameterMapping MapFromRow(MappingRow row) => new()
    {
        MappingId = row.mapping_id,
        InstanceId = row.instance_id,
        Parameter = (MotorParameter)row.parameter,
        TagId = row.tag_id,
        ScaleFactor = row.scale_factor,
        Offset = row.offset_value,
        UsedForDiagnosis = row.used_for_diagnosis
    };

    private sealed class MappingRow
    {
        public string mapping_id { get; set; } = "";
        public string instance_id { get; set; } = "";
        public int parameter { get; set; }
        public string tag_id { get; set; } = "";
        public double scale_factor { get; set; }
        public double offset_value { get; set; }
        public bool used_for_diagnosis { get; set; }
    }
}

/// <summary>
/// v64: TimescaleDB 操作模式仓储实现
/// </summary>
public sealed class OperationModeRepository : IOperationModeRepository
{
    private readonly INpgsqlConnectionFactory _factory;

    public OperationModeRepository(INpgsqlConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<OperationMode>> ListByInstanceAsync(string instanceId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<ModeRow>(
            new CommandDefinition(
                "SELECT * FROM operation_mode WHERE instance_id = @instanceId ORDER BY priority DESC, name",
                new { instanceId }, cancellationToken: ct));
        return rows.Select(MapFromRow).ToList();
    }

    public async Task<IReadOnlyList<OperationMode>> ListEnabledByInstanceAsync(string instanceId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<ModeRow>(
            new CommandDefinition(
                "SELECT * FROM operation_mode WHERE instance_id = @instanceId AND enabled = true ORDER BY priority DESC, name",
                new { instanceId }, cancellationToken: ct));
        return rows.Select(MapFromRow).ToList();
    }

    public async Task<OperationMode?> GetAsync(string modeId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync<ModeRow>(
            new CommandDefinition(
                "SELECT * FROM operation_mode WHERE mode_id = @modeId",
                new { modeId }, cancellationToken: ct));
        return row != null ? MapFromRow(row) : null;
    }

    public async Task CreateAsync(OperationMode mode, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO operation_mode (
                mode_id, instance_id, name, description, trigger_tag_id,
                trigger_min_value, trigger_max_value, min_duration_ms, max_duration_ms,
                priority, enabled, created_utc, updated_utc
            ) VALUES (
                @ModeId, @InstanceId, @Name, @Description, @TriggerTagId,
                @TriggerMinValue, @TriggerMaxValue, @MinDurationMs, @MaxDurationMs,
                @Priority, @Enabled, @CreatedUtc, @UpdatedUtc
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
                mode.Enabled,
                mode.CreatedUtc,
                mode.UpdatedUtc
            }, cancellationToken: ct));
    }

    public async Task UpdateAsync(OperationMode mode, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(@"
            UPDATE operation_mode SET
                name = @Name, description = @Description, trigger_tag_id = @TriggerTagId,
                trigger_min_value = @TriggerMinValue, trigger_max_value = @TriggerMaxValue,
                min_duration_ms = @MinDurationMs, max_duration_ms = @MaxDurationMs,
                priority = @Priority, enabled = @Enabled, updated_utc = @UpdatedUtc
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
                mode.Enabled,
                UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }, cancellationToken: ct));
    }

    public async Task DeleteAsync(string modeId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM operation_mode WHERE mode_id = @modeId",
            new { modeId }, cancellationToken: ct));
    }

    public async Task SetEnabledAsync(string modeId, bool enabled, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(@"
            UPDATE operation_mode SET enabled = @enabled, updated_utc = @updatedUtc WHERE mode_id = @modeId",
            new { modeId, enabled, updatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
            cancellationToken: ct));
    }

    public async Task DeleteByInstanceAsync(string instanceId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM operation_mode WHERE instance_id = @instanceId",
            new { instanceId }, cancellationToken: ct));
    }

    private static OperationMode MapFromRow(ModeRow row) => new()
    {
        ModeId = row.mode_id,
        InstanceId = row.instance_id,
        Name = row.name,
        Description = row.description,
        TriggerTagId = row.trigger_tag_id,
        TriggerMinValue = row.trigger_min_value,
        TriggerMaxValue = row.trigger_max_value,
        MinDurationMs = row.min_duration_ms,
        MaxDurationMs = row.max_duration_ms,
        Priority = row.priority,
        Enabled = row.enabled,
        CreatedUtc = row.created_utc,
        UpdatedUtc = row.updated_utc
    };

    private sealed class ModeRow
    {
        public string mode_id { get; set; } = "";
        public string instance_id { get; set; } = "";
        public string name { get; set; } = "";
        public string? description { get; set; }
        public string? trigger_tag_id { get; set; }
        public double? trigger_min_value { get; set; }
        public double? trigger_max_value { get; set; }
        public int min_duration_ms { get; set; }
        public int max_duration_ms { get; set; }
        public int priority { get; set; }
        public bool enabled { get; set; }
        public long created_utc { get; set; }
        public long? updated_utc { get; set; }
    }
}

/// <summary>
/// v64: TimescaleDB 基线配置仓储实现
/// </summary>
public sealed class BaselineProfileRepository : IBaselineProfileRepository
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<BaselineProfileRepository> _logger;

    public BaselineProfileRepository(INpgsqlConnectionFactory factory, ILogger<BaselineProfileRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BaselineProfile>> ListByModeAsync(string modeId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<BaselineRow>(
            new CommandDefinition(
                "SELECT * FROM baseline_profile WHERE mode_id = @modeId ORDER BY parameter",
                new { modeId }, cancellationToken: ct));
        return rows.Select(MapFromRow).ToList();
    }

    public async Task<IReadOnlyList<BaselineProfile>> ListByInstanceAsync(string instanceId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<BaselineRow>(
            new CommandDefinition(@"
                SELECT bp.* FROM baseline_profile bp
                INNER JOIN operation_mode om ON bp.mode_id = om.mode_id
                WHERE om.instance_id = @instanceId ORDER BY om.name, bp.parameter",
                new { instanceId }, cancellationToken: ct));
        return rows.Select(MapFromRow).ToList();
    }

    public async Task<BaselineProfile?> GetAsync(string baselineId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync<BaselineRow>(
            new CommandDefinition(
                "SELECT * FROM baseline_profile WHERE baseline_id = @baselineId",
                new { baselineId }, cancellationToken: ct));
        return row != null ? MapFromRow(row) : null;
    }

    public async Task<BaselineProfile?> GetByModeAndParameterAsync(string modeId, MotorParameter parameter, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync<BaselineRow>(
            new CommandDefinition(
                "SELECT * FROM baseline_profile WHERE mode_id = @modeId AND parameter = @parameter",
                new { modeId, parameter = (int)parameter }, cancellationToken: ct));
        return row != null ? MapFromRow(row) : null;
    }

    public async Task CreateAsync(BaselineProfile baseline, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(@"
            INSERT INTO baseline_profile (
                baseline_id, mode_id, parameter, mean, std_dev, min_value, max_value,
                percentile_05, percentile_95, median, frequency_profile_json,
                sample_count, learned_from_utc, learned_to_utc, confidence_level,
                version, created_utc, updated_utc
            ) VALUES (
                @BaselineId, @ModeId, @Parameter, @Mean, @StdDev, @MinValue, @MaxValue,
                @Percentile05, @Percentile95, @Median, @FrequencyProfileJson::jsonb,
                @SampleCount, @LearnedFromUtc, @LearnedToUtc, @ConfidenceLevel,
                @Version, @CreatedUtc, @UpdatedUtc
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
            }, cancellationToken: ct));
    }

    public async Task SaveBatchAsync(IEnumerable<BaselineProfile> baselines, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        using var transaction = conn.BeginTransaction();
        try
        {
            foreach (var b in baselines)
            {
                await conn.ExecuteAsync(new CommandDefinition(@"
                    INSERT INTO baseline_profile (
                        baseline_id, mode_id, parameter, mean, std_dev, min_value, max_value,
                        percentile_05, percentile_95, median, frequency_profile_json,
                        sample_count, learned_from_utc, learned_to_utc, confidence_level,
                        version, created_utc, updated_utc
                    ) VALUES (
                        @BaselineId, @ModeId, @Parameter, @Mean, @StdDev, @MinValue, @MaxValue,
                        @Percentile05, @Percentile95, @Median, @FrequencyProfileJson::jsonb,
                        @SampleCount, @LearnedFromUtc, @LearnedToUtc, @ConfidenceLevel,
                        @Version, @CreatedUtc, @UpdatedUtc
                    )
                    ON CONFLICT (mode_id, parameter) DO UPDATE SET
                        baseline_id = EXCLUDED.baseline_id, mean = EXCLUDED.mean,
                        std_dev = EXCLUDED.std_dev, min_value = EXCLUDED.min_value,
                        max_value = EXCLUDED.max_value, percentile_05 = EXCLUDED.percentile_05,
                        percentile_95 = EXCLUDED.percentile_95, median = EXCLUDED.median,
                        frequency_profile_json = EXCLUDED.frequency_profile_json,
                        sample_count = EXCLUDED.sample_count, learned_from_utc = EXCLUDED.learned_from_utc,
                        learned_to_utc = EXCLUDED.learned_to_utc, confidence_level = EXCLUDED.confidence_level,
                        version = baseline_profile.version + 1, updated_utc = EXCLUDED.updated_utc",
                    new
                    {
                        b.BaselineId,
                        b.ModeId,
                        Parameter = (int)b.Parameter,
                        b.Mean,
                        b.StdDev,
                        b.MinValue,
                        b.MaxValue,
                        b.Percentile05,
                        b.Percentile95,
                        b.Median,
                        b.FrequencyProfileJson,
                        b.SampleCount,
                        b.LearnedFromUtc,
                        b.LearnedToUtc,
                        b.ConfidenceLevel,
                        b.Version,
                        b.CreatedUtc,
                        UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    }, transaction, cancellationToken: ct));
            }
            transaction.Commit();
            _logger.LogDebug("Saved {Count} baseline profiles", baselines.Count());
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task UpdateAsync(BaselineProfile baseline, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(@"
            UPDATE baseline_profile SET
                mean = @Mean, std_dev = @StdDev, min_value = @MinValue, max_value = @MaxValue,
                percentile_05 = @Percentile05, percentile_95 = @Percentile95, median = @Median,
                frequency_profile_json = @FrequencyProfileJson::jsonb, sample_count = @SampleCount,
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
            }, cancellationToken: ct));
    }

    public async Task DeleteAsync(string baselineId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM baseline_profile WHERE baseline_id = @baselineId",
            new { baselineId }, cancellationToken: ct));
    }

    public async Task DeleteByModeAsync(string modeId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM baseline_profile WHERE mode_id = @modeId",
            new { modeId }, cancellationToken: ct));
    }

    public async Task DeleteByInstanceAsync(string instanceId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(@"
            DELETE FROM baseline_profile WHERE mode_id IN (
                SELECT mode_id FROM operation_mode WHERE instance_id = @instanceId
            )",
            new { instanceId }, cancellationToken: ct));
    }

    public async Task<int> GetCountByInstanceAsync(string instanceId, CancellationToken ct)
    {
        using var conn = _factory.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(@"
            SELECT COUNT(*) FROM baseline_profile bp
            INNER JOIN operation_mode om ON bp.mode_id = om.mode_id
            WHERE om.instance_id = @instanceId",
            new { instanceId }, cancellationToken: ct));
    }

    private static BaselineProfile MapFromRow(BaselineRow row) => new()
    {
        BaselineId = row.baseline_id,
        ModeId = row.mode_id,
        Parameter = (MotorParameter)row.parameter,
        Mean = row.mean,
        StdDev = row.std_dev,
        MinValue = row.min_value,
        MaxValue = row.max_value,
        Percentile05 = row.percentile_05 ?? 0,
        Percentile95 = row.percentile_95 ?? 0,
        Median = row.median ?? 0,
        FrequencyProfileJson = row.frequency_profile_json,
        SampleCount = row.sample_count,
        LearnedFromUtc = row.learned_from_utc,
        LearnedToUtc = row.learned_to_utc,
        ConfidenceLevel = row.confidence_level ?? 0,
        Version = row.version,
        CreatedUtc = row.created_utc,
        UpdatedUtc = row.updated_utc
    };

    private sealed class BaselineRow
    {
        public string baseline_id { get; set; } = "";
        public string mode_id { get; set; } = "";
        public int parameter { get; set; }
        public double mean { get; set; }
        public double std_dev { get; set; }
        public double min_value { get; set; }
        public double max_value { get; set; }
        public double? percentile_05 { get; set; }
        public double? percentile_95 { get; set; }
        public double? median { get; set; }
        public string? frequency_profile_json { get; set; }
        public int sample_count { get; set; }
        public long learned_from_utc { get; set; }
        public long learned_to_utc { get; set; }
        public double? confidence_level { get; set; }
        public int version { get; set; }
        public long created_utc { get; set; }
        public long? updated_utc { get; set; }
    }
}
