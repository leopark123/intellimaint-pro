using IntelliMaint.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Host.Api.Services;

/// <summary>
/// v56.2: 数据清理后台服务 - 数据库无关实现
/// 定期删除旧的遥测数据，防止数据库无限增长
/// 支持 SQLite 和 TimescaleDB
/// </summary>
public sealed class DataCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataCleanupService> _logger;
    private readonly DataCleanupOptions _options;

    public DataCleanupService(
        IServiceScopeFactory scopeFactory,
        IOptions<DataCleanupOptions> options,
        ILogger<DataCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DataCleanupService started. Retention: Telemetry={TelemetryDays}d, Alarm={AlarmDays}d, Audit={AuditDays}d, Interval={Interval}h",
            _options.TelemetryRetentionDays,
            _options.AlarmRetentionDays,
            _options.AuditLogRetentionDays,
            _options.CleanupIntervalHours);

        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DataCleanupService error");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(_options.CleanupIntervalHours), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("DataCleanupService stopped");
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting data cleanup...");
        var totalDeleted = 0;

        using var scope = _scopeFactory.CreateScope();

        var timeSeriesDb = scope.ServiceProvider.GetRequiredService<ITimeSeriesDb>();
        var isSqlite = timeSeriesDb.DbType == TimeSeriesDbType.Sqlite;

        // 1. 清理原始遥测数据
        if (_options.TelemetryRetentionDays > 0)
        {
            var telemetryRepo = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
            var cutoffTs = DateTimeOffset.UtcNow.AddDays(-_options.TelemetryRetentionDays).ToUnixTimeMilliseconds();
            var safeCutoffTs = cutoffTs;

            if (isSqlite)
            {
                try
                {
                    var executor = scope.ServiceProvider.GetService<IntelliMaint.Infrastructure.Sqlite.IDbExecutor>();
                    if (executor != null)
                    {
                        var aggregatedTs = await executor.ExecuteScalarAsync<long>(
                            "SELECT COALESCE(last_processed_ts, 0) FROM aggregate_state WHERE table_name = 'telemetry_1m'",
                            null, ct);
                        if (aggregatedTs > 0) safeCutoffTs = Math.Min(cutoffTs, aggregatedTs);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get aggregate state");
                }
            }

            if (safeCutoffTs > 0)
            {
                var deleted = await telemetryRepo.DeleteBeforeAsync(safeCutoffTs, ct);
                totalDeleted += deleted;
                _logger.LogInformation("Deleted {Count} telemetry records", deleted);
            }
        }

        // 2-3. 清理聚合数据（仅 SQLite）
        if (isSqlite)
        {
            try
            {
                var executor = scope.ServiceProvider.GetService<IntelliMaint.Infrastructure.Sqlite.IDbExecutor>();
                if (executor != null)
                {
                    var cutoffTs1m = DateTimeOffset.UtcNow.AddDays(-_options.Telemetry1mRetentionDays).ToUnixTimeMilliseconds();
                    var deleted1m = await executor.ExecuteNonQueryAsync(
                        "DELETE FROM telemetry_1m WHERE ts_bucket < @CutoffTs",
                        new { CutoffTs = cutoffTs1m }, ct);
                    totalDeleted += deleted1m;

                    var cutoffTs1h = DateTimeOffset.UtcNow.AddDays(-_options.Telemetry1hRetentionDays).ToUnixTimeMilliseconds();
                    var deleted1h = await executor.ExecuteNonQueryAsync(
                        "DELETE FROM telemetry_1h WHERE ts_bucket < @CutoffTs",
                        new { CutoffTs = cutoffTs1h }, ct);
                    totalDeleted += deleted1h;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup aggregate tables");
            }
        }

        // 4. 清理告警数据
        if (_options.AlarmRetentionDays > 0)
        {
            var alarmRepo = scope.ServiceProvider.GetRequiredService<IAlarmRepository>();
            var cutoffTs = DateTimeOffset.UtcNow.AddDays(-_options.AlarmRetentionDays).ToUnixTimeMilliseconds();
            totalDeleted += await alarmRepo.DeleteBeforeAsync(cutoffTs, ct);
        }

        // 5. 清理审计日志
        if (_options.AuditLogRetentionDays > 0)
        {
            var auditRepo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
            var cutoffTs = DateTimeOffset.UtcNow.AddDays(-_options.AuditLogRetentionDays).ToUnixTimeMilliseconds();
            totalDeleted += await auditRepo.DeleteBeforeAsync(cutoffTs, ct);
        }

        // 6. 数据库维护
        if (_options.VacuumAfterCleanup && totalDeleted > 10000)
        {
            try
            {
                await timeSeriesDb.PerformMaintenanceAsync(new MaintenanceOptions
                {
                    Vacuum = true,
                    UpdateStatistics = true,
                    CleanupExpiredData = false,
                    CompressOldChunks = !isSqlite,
                    CompressAfterDays = _options.TelemetryRetentionDays
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Database maintenance failed");
            }
        }

        _logger.LogInformation("Data cleanup completed. Total deleted: {Count}", totalDeleted);
    }
}

public sealed class DataCleanupOptions
{
    public const string SectionName = "DataCleanup";
    public int TelemetryRetentionDays { get; set; } = 7;
    public int Telemetry1mRetentionDays { get; set; } = 30;
    public int Telemetry1hRetentionDays { get; set; } = 365;
    public int AlarmRetentionDays { get; set; } = 30;
    public int AuditLogRetentionDays { get; set; } = 90;
    public int CleanupIntervalHours { get; set; } = 6;
    public bool VacuumAfterCleanup { get; set; } = true;
}