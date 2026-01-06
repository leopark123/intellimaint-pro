using System;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace IntelliMaint.Host.Api.Services;

/// <summary>
/// v56.1: 统计缓存服务 - 避免每次请求都执行 COUNT(*) 全表扫描
/// 后台每 60 秒刷新一次统计数据，API 直接读取缓存值
/// v56.2: 使用 ITimeSeriesDb 抽象层，支持 SQLite 和 TimescaleDB
/// </summary>
public sealed class StatsCacheService : BackgroundService
{
    private readonly ITimeSeriesDb _db;
    private readonly IDeviceRepository _deviceRepo;
    private readonly ITagRepository _tagRepo;
    private readonly IAlarmRepository _alarmRepo;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(60);

    // 缓存的统计数据
    private volatile CachedStats _stats = new();
    private volatile bool _initialized = false;

    public StatsCacheService(
        ITimeSeriesDb db,
        IDeviceRepository deviceRepo,
        ITagRepository tagRepo,
        IAlarmRepository alarmRepo)
    {
        _db = db;
        _deviceRepo = deviceRepo;
        _tagRepo = tagRepo;
        _alarmRepo = alarmRepo;
    }

    /// <summary>
    /// 获取缓存的统计数据（非阻塞）
    /// </summary>
    public CachedStats GetStats() => _stats;

    /// <summary>
    /// 统计数据是否已初始化
    /// </summary>
    public bool IsInitialized => _initialized;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Information("[StatsCacheService] Starting stats cache service");

        // 首次立即刷新
        await RefreshStatsAsync(stoppingToken);
        _initialized = true;
        Log.Information("[StatsCacheService] Initial stats loaded");

        // 定期刷新
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_refreshInterval, stoppingToken);
                await RefreshStatsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[StatsCacheService] Failed to refresh stats");
            }
        }

        Log.Information("[StatsCacheService] Stopped");
    }

    private async Task RefreshStatsAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 使用 ITimeSeriesDb 获取数据库统计信息
            var dbStatsTask = _db.GetStatisticsAsync(ct);
            
            // 使用仓储获取设备和标签统计
            var devicesTask = _deviceRepo.ListAsync(ct);
            var tagsTask = _tagRepo.ListAllAsync(ct);
            var openAlarmsTask = _alarmRepo.GetOpenCountAsync(null, ct);

            await Task.WhenAll(dbStatsTask, devicesTask, tagsTask, openAlarmsTask);

            var dbStats = await dbStatsTask;
            var devices = await devicesTask;
            var tags = await tagsTask;

            // 原子更新缓存
            _stats = new CachedStats
            {
                TotalTelemetryPoints = dbStats.TotalTelemetryRows,
                TotalAlarms = dbStats.Tables.FirstOrDefault(t => t.TableName == "alarm")?.RowCount ?? 0,
                TotalDevices = devices.Count,
                EnabledDevices = devices.Count(d => d.Enabled),
                TotalTags = tags.Count,
                EnabledTags = tags.Count(t => t.Enabled),
                OpenAlarms = await openAlarmsTask,
                Last24HoursTelemetryPoints = 0, // Will be computed from aggregate table if needed
                DatabaseSizeBytes = dbStats.DatabaseSizeBytes,
                LastRefreshUtc = DateTimeOffset.UtcNow
            };

            sw.Stop();
            Log.Debug("[StatsCacheService] Stats refreshed in {ElapsedMs}ms: Telemetry={Telemetry}, Alarms={Alarms}",
                sw.ElapsedMilliseconds, _stats.TotalTelemetryPoints, _stats.TotalAlarms);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error(ex, "[StatsCacheService] Failed to refresh stats after {ElapsedMs}ms", sw.ElapsedMilliseconds);
            throw;
        }
    }

    /// <summary>
    /// 强制刷新统计数据（供 API 调用）
    /// </summary>
    public async Task ForceRefreshAsync(CancellationToken ct = default)
    {
        await RefreshStatsAsync(ct);
        Log.Information("[StatsCacheService] Force refresh completed");
    }
}

/// <summary>
/// 缓存的统计数据
/// </summary>
public sealed record CachedStats
{
    public long TotalTelemetryPoints { get; init; }
    public long TotalAlarms { get; init; }
    public long TotalDevices { get; init; }
    public long EnabledDevices { get; init; }
    public long TotalTags { get; init; }
    public long EnabledTags { get; init; }
    public long OpenAlarms { get; init; }
    public long Last24HoursTelemetryPoints { get; init; }
    public long DatabaseSizeBytes { get; init; }
    public DateTimeOffset LastRefreshUtc { get; init; }
}
