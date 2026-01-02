using IntelliMaint.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Host.Api.Services;

/// <summary>
/// v56: 数据清理后台服务
/// 定期删除旧的遥测数据，防止数据库无限增长
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

        // 等待一段时间后再开始清理，让系统完全启动
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
        var executor = scope.ServiceProvider.GetRequiredService<IntelliMaint.Infrastructure.Sqlite.IDbExecutor>();

        // 1. 清理原始遥测数据（仅清理已聚合的数据）
        if (_options.TelemetryRetentionDays > 0)
        {
            var telemetryRepo = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
            var cutoffTs = DateTimeOffset.UtcNow.AddDays(-_options.TelemetryRetentionDays).ToUnixTimeMilliseconds();
            
            // 检查聚合进度，只删除已聚合的数据
            var aggregatedTs = await executor.ExecuteScalarAsync<long>(
                "SELECT last_processed_ts FROM aggregate_state WHERE table_name = 'telemetry_1m'",
                null, ct);
            
            // 取较小值：只删除既过期又已聚合的数据
            var safeCutoffTs = Math.Min(cutoffTs, aggregatedTs);
            
            if (safeCutoffTs > 0)
            {
                var deleted = await telemetryRepo.DeleteBeforeAsync(safeCutoffTs, ct);
                totalDeleted += deleted;
                _logger.LogInformation("Deleted {Count} telemetry records older than {Days} days (cutoff: {Cutoff})", 
                    deleted, _options.TelemetryRetentionDays, safeCutoffTs);
            }
        }
        
        // 2. 清理分钟级聚合数据（保留30天）
        {
            var cutoffTs = DateTimeOffset.UtcNow.AddDays(-_options.Telemetry1mRetentionDays).ToUnixTimeMilliseconds();
            
            // 检查小时级聚合进度
            var aggregatedTs = await executor.ExecuteScalarAsync<long>(
                "SELECT last_processed_ts FROM aggregate_state WHERE table_name = 'telemetry_1h'",
                null, ct);
            
            var safeCutoffTs = Math.Min(cutoffTs, aggregatedTs);
            
            if (safeCutoffTs > 0)
            {
                var deleted = await executor.ExecuteNonQueryAsync(
                    "DELETE FROM telemetry_1m WHERE ts_bucket < @CutoffTs",
                    new { CutoffTs = safeCutoffTs },
                    ct);
                totalDeleted += deleted;
                _logger.LogInformation("Deleted {Count} minute-level aggregates older than {Days} days", 
                    deleted, _options.Telemetry1mRetentionDays);
            }
        }
        
        // 3. 清理小时级聚合数据（保留1年）
        {
            var cutoffTs = DateTimeOffset.UtcNow.AddDays(-_options.Telemetry1hRetentionDays).ToUnixTimeMilliseconds();
            var deleted = await executor.ExecuteNonQueryAsync(
                "DELETE FROM telemetry_1h WHERE ts_bucket < @CutoffTs",
                new { CutoffTs = cutoffTs },
                ct);
            if (deleted > 0)
            {
                totalDeleted += deleted;
                _logger.LogInformation("Deleted {Count} hour-level aggregates older than {Days} days", 
                    deleted, _options.Telemetry1hRetentionDays);
            }
        }

        // 4. 清理告警数据
        if (_options.AlarmRetentionDays > 0)
        {
            var alarmRepo = scope.ServiceProvider.GetRequiredService<IAlarmRepository>();
            var cutoffTs = DateTimeOffset.UtcNow.AddDays(-_options.AlarmRetentionDays).ToUnixTimeMilliseconds();
            var deleted = await alarmRepo.DeleteBeforeAsync(cutoffTs, ct);
            totalDeleted += deleted;
            _logger.LogInformation("Deleted {Count} alarm records older than {Days} days", deleted, _options.AlarmRetentionDays);
        }

        // 5. 清理审计日志
        if (_options.AuditLogRetentionDays > 0)
        {
            var cutoffTs = DateTimeOffset.UtcNow.AddDays(-_options.AuditLogRetentionDays).ToUnixTimeMilliseconds();
            var deleted = await executor.ExecuteNonQueryAsync(
                "DELETE FROM audit_log WHERE ts < @CutoffTs",
                new { CutoffTs = cutoffTs },
                ct);
            totalDeleted += deleted;
            _logger.LogInformation("Deleted {Count} audit log records older than {Days} days", deleted, _options.AuditLogRetentionDays);
        }

        // 6. 执行 VACUUM 压缩数据库（可选，会锁表）
        if (_options.VacuumAfterCleanup && totalDeleted > 10000)
        {
            _logger.LogInformation("Running VACUUM to reclaim disk space...");
            await executor.ExecuteNonQueryAsync("VACUUM", null, ct);
            _logger.LogInformation("VACUUM completed");
        }

        _logger.LogInformation("Data cleanup completed. Total deleted: {Count}", totalDeleted);
    }
}

/// <summary>
/// 数据清理配置选项
/// </summary>
public sealed class DataCleanupOptions
{
    public const string SectionName = "DataCleanup";

    /// <summary>原始遥测数据保留天数（默认7天）</summary>
    public int TelemetryRetentionDays { get; set; } = 7;
    
    /// <summary>分钟级聚合数据保留天数（默认30天）</summary>
    public int Telemetry1mRetentionDays { get; set; } = 30;
    
    /// <summary>小时级聚合数据保留天数（默认365天）</summary>
    public int Telemetry1hRetentionDays { get; set; } = 365;

    /// <summary>告警数据保留天数（默认30天）</summary>
    public int AlarmRetentionDays { get; set; } = 30;

    /// <summary>审计日志保留天数（默认90天）</summary>
    public int AuditLogRetentionDays { get; set; } = 90;

    /// <summary>清理间隔小时数（默认6小时）</summary>
    public int CleanupIntervalHours { get; set; } = 6;

    /// <summary>清理后是否执行 VACUUM（默认 true）</summary>
    public bool VacuumAfterCleanup { get; set; } = true;
}
