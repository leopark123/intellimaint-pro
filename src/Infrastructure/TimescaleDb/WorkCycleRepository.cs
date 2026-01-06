using System.Text.Json;
using Dapper;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB WorkCycle repository implementation
/// </summary>
public sealed class WorkCycleRepository : IWorkCycleRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<WorkCycleRepository> _logger;

    public WorkCycleRepository(INpgsqlConnectionFactory factory, ILogger<WorkCycleRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<long> CreateAsync(WorkCycle cycle, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        const string sql = @"
            INSERT INTO work_cycle (
                device_id, segment_id, start_time_utc, end_time_utc, duration_seconds,
                max_angle, motor1_peak_current, motor2_peak_current, motor1_avg_current, motor2_avg_current,
                motor1_energy, motor2_energy, motor_balance_ratio, baseline_deviation_percent,
                anomaly_score, is_anomaly, anomaly_type, details_json, created_utc
            ) VALUES (
                @DeviceId, @SegmentId, @StartTimeUtc, @EndTimeUtc, @DurationSeconds,
                @MaxAngle, @Motor1PeakCurrent, @Motor2PeakCurrent, @Motor1AvgCurrent, @Motor2AvgCurrent,
                @Motor1Energy, @Motor2Energy, @MotorBalanceRatio, @BaselineDeviationPercent,
                @AnomalyScore, @IsAnomaly, @AnomalyType, @DetailsJson, @CreatedUtc
            )
            RETURNING id";

        using var conn = _factory.CreateConnection();
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, new
        {
            cycle.DeviceId,
            cycle.SegmentId,
            cycle.StartTimeUtc,
            cycle.EndTimeUtc,
            cycle.DurationSeconds,
            cycle.MaxAngle,
            cycle.Motor1PeakCurrent,
            cycle.Motor2PeakCurrent,
            cycle.Motor1AvgCurrent,
            cycle.Motor2AvgCurrent,
            cycle.Motor1Energy,
            cycle.Motor2Energy,
            cycle.MotorBalanceRatio,
            cycle.BaselineDeviationPercent,
            cycle.AnomalyScore,
            cycle.IsAnomaly,
            cycle.AnomalyType,
            cycle.DetailsJson,
            CreatedUtc = cycle.CreatedUtc > 0 ? cycle.CreatedUtc : nowUtc
        }, cancellationToken: ct));
    }

    public async Task<int> CreateBatchAsync(IEnumerable<WorkCycle> cycles, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var count = 0;

        const string sql = @"
            INSERT INTO work_cycle (
                device_id, segment_id, start_time_utc, end_time_utc, duration_seconds,
                max_angle, motor1_peak_current, motor2_peak_current, motor1_avg_current, motor2_avg_current,
                motor1_energy, motor2_energy, motor_balance_ratio, baseline_deviation_percent,
                anomaly_score, is_anomaly, anomaly_type, details_json, created_utc
            ) VALUES (
                @DeviceId, @SegmentId, @StartTimeUtc, @EndTimeUtc, @DurationSeconds,
                @MaxAngle, @Motor1PeakCurrent, @Motor2PeakCurrent, @Motor1AvgCurrent, @Motor2AvgCurrent,
                @Motor1Energy, @Motor2Energy, @MotorBalanceRatio, @BaselineDeviationPercent,
                @AnomalyScore, @IsAnomaly, @AnomalyType, @DetailsJson, @CreatedUtc
            )";

        using var conn = _factory.CreateConnection();
        foreach (var cycle in cycles)
        {
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                cycle.DeviceId,
                cycle.SegmentId,
                cycle.StartTimeUtc,
                cycle.EndTimeUtc,
                cycle.DurationSeconds,
                cycle.MaxAngle,
                cycle.Motor1PeakCurrent,
                cycle.Motor2PeakCurrent,
                cycle.Motor1AvgCurrent,
                cycle.Motor2AvgCurrent,
                cycle.Motor1Energy,
                cycle.Motor2Energy,
                cycle.MotorBalanceRatio,
                cycle.BaselineDeviationPercent,
                cycle.AnomalyScore,
                cycle.IsAnomaly,
                cycle.AnomalyType,
                cycle.DetailsJson,
                CreatedUtc = cycle.CreatedUtc > 0 ? cycle.CreatedUtc : nowUtc
            }, cancellationToken: ct));
            count++;
        }
        return count;
    }

    public async Task<WorkCycle?> GetAsync(long id, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, device_id, segment_id, start_time_utc, end_time_utc, duration_seconds,
                   max_angle, motor1_peak_current, motor2_peak_current, motor1_avg_current, motor2_avg_current,
                   motor1_energy, motor2_energy, motor_balance_ratio, baseline_deviation_percent,
                   anomaly_score, is_anomaly, anomaly_type, details_json, created_utc
            FROM work_cycle WHERE id = @Id";

        using var conn = _factory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<WorkCycleRow>(
            new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
        return row is null ? null : MapToWorkCycle(row);
    }

    public async Task<IReadOnlyList<WorkCycle>> QueryAsync(WorkCycleQuery query, CancellationToken ct)
    {
        var conditions = new List<string>();
        var p = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(query.DeviceId))
        {
            conditions.Add("device_id = @DeviceId");
            p.Add("DeviceId", query.DeviceId);
        }

        if (query.StartTimeUtc.HasValue)
        {
            conditions.Add("start_time_utc >= @StartTimeUtc");
            p.Add("StartTimeUtc", query.StartTimeUtc.Value);
        }

        if (query.EndTimeUtc.HasValue)
        {
            conditions.Add("end_time_utc <= @EndTimeUtc");
            p.Add("EndTimeUtc", query.EndTimeUtc.Value);
        }

        if (query.IsAnomaly.HasValue)
        {
            conditions.Add("is_anomaly = @IsAnomaly");
            p.Add("IsAnomaly", query.IsAnomaly.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.AnomalyType))
        {
            conditions.Add("anomaly_type = @AnomalyType");
            p.Add("AnomalyType", query.AnomalyType);
        }

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        var limit = Math.Clamp(query.Limit, 1, 500);
        p.Add("Limit", limit);

        var sql = $@"
            SELECT id, device_id, segment_id, start_time_utc, end_time_utc, duration_seconds,
                   max_angle, motor1_peak_current, motor2_peak_current, motor1_avg_current, motor2_avg_current,
                   motor1_energy, motor2_energy, motor_balance_ratio, baseline_deviation_percent,
                   anomaly_score, is_anomaly, anomaly_type, details_json, created_utc
            FROM work_cycle
            {whereClause}
            ORDER BY start_time_utc DESC
            LIMIT @Limit";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<WorkCycleRow>(new CommandDefinition(sql, p, cancellationToken: ct));
        return rows.Select(MapToWorkCycle).ToList();
    }

    public async Task<IReadOnlyList<WorkCycle>> ListBySegmentAsync(long segmentId, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, device_id, segment_id, start_time_utc, end_time_utc, duration_seconds,
                   max_angle, motor1_peak_current, motor2_peak_current, motor1_avg_current, motor2_avg_current,
                   motor1_energy, motor2_energy, motor_balance_ratio, baseline_deviation_percent,
                   anomaly_score, is_anomaly, anomaly_type, details_json, created_utc
            FROM work_cycle
            WHERE segment_id = @SegmentId
            ORDER BY start_time_utc ASC";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<WorkCycleRow>(
            new CommandDefinition(sql, new { SegmentId = segmentId }, cancellationToken: ct));
        return rows.Select(MapToWorkCycle).ToList();
    }

    public async Task<int> GetCountByDeviceAsync(string deviceId, long? startTs, long? endTs, CancellationToken ct)
    {
        var sql = "SELECT COUNT(1) FROM work_cycle WHERE device_id = @DeviceId";
        var p = new DynamicParameters();
        p.Add("DeviceId", deviceId);

        if (startTs.HasValue)
        {
            sql += " AND start_time_utc >= @StartTs";
            p.Add("StartTs", startTs.Value);
        }

        if (endTs.HasValue)
        {
            sql += " AND end_time_utc <= @EndTs";
            p.Add("EndTs", endTs.Value);
        }

        using var conn = _factory.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, p, cancellationToken: ct));
    }

    public async Task<int> GetAnomalyCountByDeviceAsync(string deviceId, long? startTs, long? endTs, CancellationToken ct)
    {
        var sql = "SELECT COUNT(1) FROM work_cycle WHERE device_id = @DeviceId AND is_anomaly = true";
        var p = new DynamicParameters();
        p.Add("DeviceId", deviceId);

        if (startTs.HasValue)
        {
            sql += " AND start_time_utc >= @StartTs";
            p.Add("StartTs", startTs.Value);
        }

        if (endTs.HasValue)
        {
            sql += " AND end_time_utc <= @EndTs";
            p.Add("EndTs", endTs.Value);
        }

        using var conn = _factory.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, p, cancellationToken: ct));
    }

    public async Task<int> DeleteBeforeAsync(long cutoffUtc, CancellationToken ct)
    {
        const string sql = "DELETE FROM work_cycle WHERE created_utc < @CutoffUtc";

        using var conn = _factory.CreateConnection();
        var affected = await conn.ExecuteAsync(
            new CommandDefinition(sql, new { CutoffUtc = cutoffUtc }, cancellationToken: ct));

        _logger.LogInformation("Deleted {Count} work cycles before {CutoffUtc}", affected, cutoffUtc);
        return affected;
    }

    public async Task<IReadOnlyList<WorkCycle>> GetRecentByDeviceAsync(string deviceId, int count, CancellationToken ct)
    {
        var limit = Math.Clamp(count, 1, 500);

        const string sql = @"
            SELECT id, device_id, segment_id, start_time_utc, end_time_utc, duration_seconds,
                   max_angle, motor1_peak_current, motor2_peak_current, motor1_avg_current, motor2_avg_current,
                   motor1_energy, motor2_energy, motor_balance_ratio, baseline_deviation_percent,
                   anomaly_score, is_anomaly, anomaly_type, details_json, created_utc
            FROM work_cycle
            WHERE device_id = @DeviceId
            ORDER BY start_time_utc DESC
            LIMIT @Limit";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<WorkCycleRow>(
            new CommandDefinition(sql, new { DeviceId = deviceId, Limit = limit }, cancellationToken: ct));
        return rows.Select(MapToWorkCycle).ToList();
    }

    public async Task<IReadOnlyList<WorkCycle>> GetAnomaliesByDeviceAsync(string deviceId, long? afterUtc, int limit, CancellationToken ct)
    {
        var sql = new System.Text.StringBuilder(@"
            SELECT id, device_id, segment_id, start_time_utc, end_time_utc, duration_seconds,
                   max_angle, motor1_peak_current, motor2_peak_current, motor1_avg_current, motor2_avg_current,
                   motor1_energy, motor2_energy, motor_balance_ratio, baseline_deviation_percent,
                   anomaly_score, is_anomaly, anomaly_type, details_json, created_utc
            FROM work_cycle
            WHERE device_id = @DeviceId AND is_anomaly = true");

        var p = new DynamicParameters();
        p.Add("DeviceId", deviceId);
        p.Add("Limit", Math.Clamp(limit, 1, 500));

        if (afterUtc.HasValue)
        {
            sql.Append(" AND start_time_utc > @AfterUtc");
            p.Add("AfterUtc", afterUtc.Value);
        }

        sql.Append(" ORDER BY start_time_utc DESC LIMIT @Limit");

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<WorkCycleRow>(new CommandDefinition(sql.ToString(), p, cancellationToken: ct));
        return rows.Select(MapToWorkCycle).ToList();
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        const string sql = "DELETE FROM work_cycle WHERE id = @Id";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }

    public async Task<CycleStatsSummary?> GetStatsSummaryAsync(string deviceId, long? startUtc, long? endUtc, CancellationToken ct)
    {
        var sql = new System.Text.StringBuilder(@"
            SELECT
                COUNT(1) as cycle_count,
                AVG(duration_seconds) as avg_duration,
                AVG(motor1_peak_current) as avg_motor1_peak,
                AVG(motor2_peak_current) as avg_motor2_peak,
                AVG(motor_balance_ratio) as avg_balance_ratio,
                AVG(anomaly_score) as avg_anomaly_score,
                COUNT(CASE WHEN is_anomaly THEN 1 END) as anomaly_count
            FROM work_cycle
            WHERE device_id = @DeviceId");

        var p = new DynamicParameters();
        p.Add("DeviceId", deviceId);

        if (startUtc.HasValue)
        {
            sql.Append(" AND start_time_utc >= @StartUtc");
            p.Add("StartUtc", startUtc.Value);
        }

        if (endUtc.HasValue)
        {
            sql.Append(" AND end_time_utc <= @EndUtc");
            p.Add("EndUtc", endUtc.Value);
        }

        using var conn = _factory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<SummaryRow>(
            new CommandDefinition(sql.ToString(), p, cancellationToken: ct));

        if (row is null || row.cycle_count == 0)
            return null;

        return new CycleStatsSummary
        {
            AvgDuration = row.avg_duration,
            AvgMotor1PeakCurrent = row.avg_motor1_peak,
            AvgMotor2PeakCurrent = row.avg_motor2_peak,
            AvgMotorBalanceRatio = row.avg_balance_ratio,
            AvgAnomalyScore = row.avg_anomaly_score
        };
    }

    private static WorkCycle MapToWorkCycle(WorkCycleRow row) => new()
    {
        Id = row.id,
        DeviceId = row.device_id,
        SegmentId = row.segment_id,
        StartTimeUtc = row.start_time_utc,
        EndTimeUtc = row.end_time_utc,
        DurationSeconds = row.duration_seconds,
        MaxAngle = row.max_angle,
        Motor1PeakCurrent = row.motor1_peak_current,
        Motor2PeakCurrent = row.motor2_peak_current,
        Motor1AvgCurrent = row.motor1_avg_current,
        Motor2AvgCurrent = row.motor2_avg_current,
        Motor1Energy = row.motor1_energy,
        Motor2Energy = row.motor2_energy,
        MotorBalanceRatio = row.motor_balance_ratio,
        BaselineDeviationPercent = row.baseline_deviation_percent,
        AnomalyScore = row.anomaly_score,
        IsAnomaly = row.is_anomaly,
        AnomalyType = row.anomaly_type,
        DetailsJson = row.details_json,
        CreatedUtc = row.created_utc
    };

    private sealed class WorkCycleRow
    {
        public long id { get; set; }
        public string device_id { get; set; } = "";
        public long? segment_id { get; set; }
        public long start_time_utc { get; set; }
        public long end_time_utc { get; set; }
        public double duration_seconds { get; set; }
        public double max_angle { get; set; }
        public double motor1_peak_current { get; set; }
        public double motor2_peak_current { get; set; }
        public double motor1_avg_current { get; set; }
        public double motor2_avg_current { get; set; }
        public double motor1_energy { get; set; }
        public double motor2_energy { get; set; }
        public double motor_balance_ratio { get; set; }
        public double baseline_deviation_percent { get; set; }
        public double anomaly_score { get; set; }
        public bool is_anomaly { get; set; }
        public string? anomaly_type { get; set; }
        public string? details_json { get; set; }
        public long created_utc { get; set; }
    }

    private sealed class SummaryRow
    {
        public int cycle_count { get; set; }
        public double avg_duration { get; set; }
        public double avg_motor1_peak { get; set; }
        public double avg_motor2_peak { get; set; }
        public double avg_balance_ratio { get; set; }
        public double avg_anomaly_score { get; set; }
        public int anomaly_count { get; set; }
    }
}
