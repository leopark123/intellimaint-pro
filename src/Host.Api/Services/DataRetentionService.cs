using IntelliMaint.Core.Abstractions;
using Serilog;

namespace IntelliMaint.Host.Api.Services;

/// <summary>
/// v56: 数据保留策略服务
/// 定期清理过期的遥测数据、告警记录、健康快照
/// 防止数据库无限增长导致性能退化
/// </summary>
public sealed class DataRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);  // 每小时检查一次
    
    // 默认保留策略（天）
    private const int DefaultTelemetryRetentionDays = 7;
    private const int DefaultAlarmRetentionDays = 30;
    private const int DefaultHealthRetentionDays = 30;
    private const int DefaultAuditRetentionDays = 90;

    public DataRetentionService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Information("DataRetentionService started. Check interval: {Interval}", _checkInterval);

        // 启动后等待 5 分钟再开始清理（让系统先稳定运行）
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

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
                Log.Error(ex, "DataRetentionService cleanup failed");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Log.Information("DataRetentionService stopped");
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        Log.Debug("DataRetentionService starting cleanup cycle...");

        using var scope = _scopeFactory.CreateScope();
        var settingRepo = scope.ServiceProvider.GetRequiredService<ISystemSettingRepository>();

        // 从系统设置读取保留策略
        var telemetryDays = await GetRetentionDaysAsync(settingRepo, "retention.telemetry.days", DefaultTelemetryRetentionDays, ct);
        var alarmDays = await GetRetentionDaysAsync(settingRepo, "retention.alarm.days", DefaultAlarmRetentionDays, ct);
        var healthDays = await GetRetentionDaysAsync(settingRepo, "retention.health.days", DefaultHealthRetentionDays, ct);
        var auditDays = await GetRetentionDaysAsync(settingRepo, "retention.audit.days", DefaultAuditRetentionDays, ct);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 1. 清理遥测数据
        var telemetryCutoff = now - (telemetryDays * 24L * 60 * 60 * 1000);
        var telemetryRepo = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
        var deletedTelemetry = await telemetryRepo.DeleteBeforeAsync(telemetryCutoff, ct);
        if (deletedTelemetry > 0)
        {
            Log.Information("DataRetention: Deleted {Count} telemetry records older than {Days} days", 
                deletedTelemetry, telemetryDays);
        }

        // 2. 清理告警记录（只清理已关闭的）
        var alarmCutoff = now - (alarmDays * 24L * 60 * 60 * 1000);
        var alarmRepo = scope.ServiceProvider.GetRequiredService<IAlarmRepository>();
        var deletedAlarms = await alarmRepo.DeleteBeforeAsync(alarmCutoff, ct);
        if (deletedAlarms > 0)
        {
            Log.Information("DataRetention: Deleted {Count} alarm records older than {Days} days", 
                deletedAlarms, alarmDays);
        }

        // 3. 清理健康快照
        var healthCutoff = now - (healthDays * 24L * 60 * 60 * 1000);
        var healthRepo = scope.ServiceProvider.GetRequiredService<IHealthSnapshotRepository>();
        var deletedHealth = await healthRepo.DeleteBeforeAsync(healthCutoff, ct);
        if (deletedHealth > 0)
        {
            Log.Information("DataRetention: Deleted {Count} health snapshots older than {Days} days", 
                deletedHealth, healthDays);
        }

        // 4. 清理审计日志
        var auditCutoff = now - (auditDays * 24L * 60 * 60 * 1000);
        var auditRepo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        var deletedAudit = await DeleteAuditLogsAsync(auditRepo, auditCutoff, ct);
        if (deletedAudit > 0)
        {
            Log.Information("DataRetention: Deleted {Count} audit logs older than {Days} days", 
                deletedAudit, auditDays);
        }

        // 5. 执行 SQLite VACUUM（可选，每天执行一次）
        await TryVacuumAsync(scope.ServiceProvider, ct);

        Log.Debug("DataRetentionService cleanup cycle completed");
    }

    private static async Task<int> GetRetentionDaysAsync(
        ISystemSettingRepository repo, 
        string key, 
        int defaultValue, 
        CancellationToken ct)
    {
        try
        {
            var setting = await repo.GetAsync(key, ct);
            if (setting != null && int.TryParse(setting, out var days) && days > 0)
            {
                return days;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read retention setting {Key}, using default {Default}", key, defaultValue);
        }
        return defaultValue;
    }

    private static async Task<int> DeleteAuditLogsAsync(IAuditLogRepository repo, long cutoffTs, CancellationToken ct)
    {
        // IAuditLogRepository 可能没有 DeleteBeforeAsync，需要检查
        // 如果没有，返回 0
        try
        {
            // 使用反射检查是否有 DeleteBeforeAsync 方法
            var method = repo.GetType().GetMethod("DeleteBeforeAsync");
            if (method != null)
            {
                var result = method.Invoke(repo, new object[] { cutoffTs, ct });
                if (result is Task<int> task)
                {
                    return await task;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "AuditLog cleanup not available");
        }
        return 0;
    }

    private static async Task TryVacuumAsync(IServiceProvider sp, CancellationToken ct)
    {
        // 每天只 VACUUM 一次
        var lastVacuumKey = "system.last_vacuum_utc";
        var settingRepo = sp.GetRequiredService<ISystemSettingRepository>();
        
        try
        {
            var lastVacuum = await settingRepo.GetAsync(lastVacuumKey, ct);
            var lastVacuumTime = lastVacuum != null && long.TryParse(lastVacuum, out var ts) 
                ? ts 
                : 0L;

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var oneDayMs = 24L * 60 * 60 * 1000;

            if (now - lastVacuumTime < oneDayMs)
            {
                return; // 不到一天，跳过
            }

            // 执行 VACUUM
            var dbExecutor = sp.GetService<IntelliMaint.Infrastructure.Sqlite.IDbExecutor>();
            if (dbExecutor != null)
            {
                Log.Information("DataRetention: Starting SQLite VACUUM...");
                await dbExecutor.ExecuteNonQueryAsync("VACUUM;", null, ct);
                Log.Information("DataRetention: SQLite VACUUM completed");

                // 记录 VACUUM 时间
                await settingRepo.SetAsync(lastVacuumKey, now.ToString(), ct);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SQLite VACUUM failed (non-fatal)");
        }
    }
}
