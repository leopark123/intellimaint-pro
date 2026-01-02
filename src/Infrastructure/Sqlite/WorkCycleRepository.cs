using System.Text;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// 工作周期仓储实现
/// </summary>
public sealed class WorkCycleRepository : IWorkCycleRepository
{
    private readonly IDbExecutor _db;

    public WorkCycleRepository(IDbExecutor db)
    {
        _db = db;
    }

    public async Task<long> CreateAsync(WorkCycle cycle, CancellationToken ct)
    {
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
);
SELECT last_insert_rowid();";

        var result = await _db.ExecuteScalarAsync<long>(sql, new
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
            IsAnomaly = cycle.IsAnomaly ? 1 : 0,
            cycle.AnomalyType,
            cycle.DetailsJson,
            cycle.CreatedUtc
        }, ct);

        return result;
    }

    public async Task<int> CreateBatchAsync(IEnumerable<WorkCycle> cycles, CancellationToken ct)
    {
        int count = 0;
        foreach (var cycle in cycles)
        {
            await CreateAsync(cycle, ct);
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
FROM work_cycle
WHERE id = @Id;";

        var list = await _db.QueryAsync(sql, MapCycle, new { Id = id }, ct);
        return list.Count > 0 ? list[0] : null;
    }

    public async Task<IReadOnlyList<WorkCycle>> QueryAsync(WorkCycleQuery query, CancellationToken ct)
    {
        var sb = new StringBuilder(@"
SELECT id, device_id, segment_id, start_time_utc, end_time_utc, duration_seconds,
       max_angle, motor1_peak_current, motor2_peak_current, motor1_avg_current, motor2_avg_current,
       motor1_energy, motor2_energy, motor_balance_ratio, baseline_deviation_percent,
       anomaly_score, is_anomaly, anomaly_type, details_json, created_utc
FROM work_cycle
WHERE 1=1");

        if (!string.IsNullOrEmpty(query.DeviceId))
            sb.Append(" AND device_id = @DeviceId");
        if (query.StartTimeUtc.HasValue)
            sb.Append(" AND start_time_utc >= @StartTimeUtc");
        if (query.EndTimeUtc.HasValue)
            sb.Append(" AND start_time_utc <= @EndTimeUtc");
        if (query.IsAnomaly.HasValue)
            sb.Append(" AND is_anomaly = @IsAnomaly");
        if (!string.IsNullOrEmpty(query.AnomalyType))
            sb.Append(" AND anomaly_type = @AnomalyType");

        sb.Append(" ORDER BY start_time_utc DESC LIMIT @Limit;");

        return await _db.QueryAsync(sb.ToString(), MapCycle, new
        {
            query.DeviceId,
            query.StartTimeUtc,
            query.EndTimeUtc,
            IsAnomaly = query.IsAnomaly.HasValue ? (query.IsAnomaly.Value ? 1 : 0) : (int?)null,
            query.AnomalyType,
            query.Limit
        }, ct);
    }

    public async Task<IReadOnlyList<WorkCycle>> GetRecentByDeviceAsync(string deviceId, int count, CancellationToken ct)
    {
        const string sql = @"
SELECT id, device_id, segment_id, start_time_utc, end_time_utc, duration_seconds,
       max_angle, motor1_peak_current, motor2_peak_current, motor1_avg_current, motor2_avg_current,
       motor1_energy, motor2_energy, motor_balance_ratio, baseline_deviation_percent,
       anomaly_score, is_anomaly, anomaly_type, details_json, created_utc
FROM work_cycle
WHERE device_id = @DeviceId
ORDER BY start_time_utc DESC
LIMIT @Count;";

        return await _db.QueryAsync(sql, MapCycle, new { DeviceId = deviceId, Count = count }, ct);
    }

    public async Task<IReadOnlyList<WorkCycle>> GetAnomaliesByDeviceAsync(string deviceId, long? afterUtc, int limit, CancellationToken ct)
    {
        var sql = @"
SELECT id, device_id, segment_id, start_time_utc, end_time_utc, duration_seconds,
       max_angle, motor1_peak_current, motor2_peak_current, motor1_avg_current, motor2_avg_current,
       motor1_energy, motor2_energy, motor_balance_ratio, baseline_deviation_percent,
       anomaly_score, is_anomaly, anomaly_type, details_json, created_utc
FROM work_cycle
WHERE device_id = @DeviceId AND is_anomaly = 1";

        if (afterUtc.HasValue)
            sql += " AND start_time_utc > @AfterUtc";

        sql += " ORDER BY start_time_utc DESC LIMIT @Limit;";

        return await _db.QueryAsync(sql, MapCycle, new { DeviceId = deviceId, AfterUtc = afterUtc, Limit = limit }, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        const string sql = "DELETE FROM work_cycle WHERE id = @Id;";
        await _db.ExecuteNonQueryAsync(sql, new { Id = id }, ct);
    }

    public async Task<int> DeleteBeforeAsync(long cutoffUtc, CancellationToken ct)
    {
        const string sql = "DELETE FROM work_cycle WHERE created_utc < @CutoffUtc;";
        return await _db.ExecuteNonQueryAsync(sql, new { CutoffUtc = cutoffUtc }, ct);
    }

    public async Task<CycleStatsSummary?> GetStatsSummaryAsync(string deviceId, long? startUtc, long? endUtc, CancellationToken ct)
    {
        var sql = @"
SELECT 
    AVG(duration_seconds) as avg_duration,
    AVG(motor1_peak_current) as avg_motor1_peak,
    AVG(motor2_peak_current) as avg_motor2_peak,
    AVG(motor_balance_ratio) as avg_balance,
    AVG(anomaly_score) as avg_anomaly_score
FROM work_cycle
WHERE device_id = @DeviceId";

        if (startUtc.HasValue)
            sql += " AND start_time_utc >= @StartUtc";
        if (endUtc.HasValue)
            sql += " AND start_time_utc <= @EndUtc";

        var list = await _db.QueryAsync(sql, reader => new CycleStatsSummary
        {
            AvgDuration = reader.IsDBNull(0) ? 0 : reader.GetDouble(0),
            AvgMotor1PeakCurrent = reader.IsDBNull(1) ? 0 : reader.GetDouble(1),
            AvgMotor2PeakCurrent = reader.IsDBNull(2) ? 0 : reader.GetDouble(2),
            AvgMotorBalanceRatio = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
            AvgAnomalyScore = reader.IsDBNull(4) ? 0 : reader.GetDouble(4)
        }, new { DeviceId = deviceId, StartUtc = startUtc, EndUtc = endUtc }, ct);

        return list.Count > 0 ? list[0] : null;
    }

    private static WorkCycle MapCycle(SqliteDataReader reader)
    {
        return new WorkCycle
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            DeviceId = reader.GetString(reader.GetOrdinal("device_id")),
            SegmentId = reader.IsDBNull(reader.GetOrdinal("segment_id")) 
                ? null : reader.GetInt64(reader.GetOrdinal("segment_id")),
            StartTimeUtc = reader.GetInt64(reader.GetOrdinal("start_time_utc")),
            EndTimeUtc = reader.GetInt64(reader.GetOrdinal("end_time_utc")),
            DurationSeconds = reader.GetDouble(reader.GetOrdinal("duration_seconds")),
            MaxAngle = reader.GetDouble(reader.GetOrdinal("max_angle")),
            Motor1PeakCurrent = reader.GetDouble(reader.GetOrdinal("motor1_peak_current")),
            Motor2PeakCurrent = reader.GetDouble(reader.GetOrdinal("motor2_peak_current")),
            Motor1AvgCurrent = reader.GetDouble(reader.GetOrdinal("motor1_avg_current")),
            Motor2AvgCurrent = reader.GetDouble(reader.GetOrdinal("motor2_avg_current")),
            Motor1Energy = reader.GetDouble(reader.GetOrdinal("motor1_energy")),
            Motor2Energy = reader.GetDouble(reader.GetOrdinal("motor2_energy")),
            MotorBalanceRatio = reader.GetDouble(reader.GetOrdinal("motor_balance_ratio")),
            BaselineDeviationPercent = reader.GetDouble(reader.GetOrdinal("baseline_deviation_percent")),
            AnomalyScore = reader.GetDouble(reader.GetOrdinal("anomaly_score")),
            IsAnomaly = reader.GetInt32(reader.GetOrdinal("is_anomaly")) == 1,
            AnomalyType = reader.IsDBNull(reader.GetOrdinal("anomaly_type")) 
                ? null : reader.GetString(reader.GetOrdinal("anomaly_type")),
            DetailsJson = reader.IsDBNull(reader.GetOrdinal("details_json")) 
                ? null : reader.GetString(reader.GetOrdinal("details_json")),
            CreatedUtc = reader.GetInt64(reader.GetOrdinal("created_utc"))
        };
    }
}
