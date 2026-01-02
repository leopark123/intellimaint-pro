using IntelliMaint.Infrastructure.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntelliMaint.Host.Api.Services;

/// <summary>
/// v56: 数据聚合服务
/// 将原始遥测数据聚合为分钟级和小时级数据
/// </summary>
public sealed class DataAggregationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataAggregationService> _logger;
    private readonly DataCleanupOptions _options;

    // 聚合间隔
    private readonly TimeSpan _aggregateInterval = TimeSpan.FromMinutes(1);

    public DataAggregationService(
        IServiceScopeFactory scopeFactory,
        IOptions<DataCleanupOptions> options,
        ILogger<DataAggregationService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DataAggregationService started");

        // 等待系统启动完成
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await AggregateAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DataAggregationService error");
            }

            try
            {
                await Task.Delay(_aggregateInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("DataAggregationService stopped");
    }

    private async Task AggregateAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbExecutor>();

        // 1. 分钟级聚合
        await AggregateToMinuteAsync(db, ct);

        // 2. 小时级聚合（每小时执行一次）
        var now = DateTime.UtcNow;
        if (now.Minute == 0 || now.Minute == 1)
        {
            await AggregateToHourAsync(db, ct);
        }
    }

    /// <summary>
    /// 将原始数据聚合为分钟级
    /// </summary>
    private async Task AggregateToMinuteAsync(IDbExecutor db, CancellationToken ct)
    {
        // 获取上次处理位置
        var lastTs = await db.ExecuteScalarAsync<long>(
            "SELECT last_processed_ts FROM aggregate_state WHERE table_name = 'telemetry_1m'",
            null, ct);

        // 当前时间向下取整到分钟
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var currentMinuteBucket = (nowMs / 60000) * 60000;
        
        // 聚合上一分钟的数据（确保数据完整）
        var targetBucket = currentMinuteBucket - 60000;
        
        if (lastTs >= targetBucket)
        {
            return; // 已经处理过了
        }

        // 从上次处理位置开始，到上一分钟结束
        var startTs = lastTs > 0 ? lastTs : targetBucket - 60000;
        var endTs = targetBucket + 60000 - 1;

        const string aggregateSql = @"
            INSERT OR REPLACE INTO telemetry_1m (
                device_id, tag_id, ts_bucket, 
                min_value, max_value, avg_value, first_value, last_value, count
            )
            SELECT 
                device_id,
                tag_id,
                (ts / 60000) * 60000 as ts_bucket,
                MIN(COALESCE(float64_value, float32_value, int32_value, int16_value, 0)) as min_value,
                MAX(COALESCE(float64_value, float32_value, int32_value, int16_value, 0)) as max_value,
                AVG(COALESCE(float64_value, float32_value, int32_value, int16_value, 0)) as avg_value,
                (SELECT COALESCE(float64_value, float32_value, int32_value, int16_value, 0) 
                 FROM telemetry t2 
                 WHERE t2.device_id = telemetry.device_id 
                   AND t2.tag_id = telemetry.tag_id 
                   AND t2.ts >= (telemetry.ts / 60000) * 60000
                   AND t2.ts < (telemetry.ts / 60000) * 60000 + 60000
                 ORDER BY t2.ts ASC, t2.seq ASC LIMIT 1) as first_value,
                (SELECT COALESCE(float64_value, float32_value, int32_value, int16_value, 0) 
                 FROM telemetry t2 
                 WHERE t2.device_id = telemetry.device_id 
                   AND t2.tag_id = telemetry.tag_id 
                   AND t2.ts >= (telemetry.ts / 60000) * 60000
                   AND t2.ts < (telemetry.ts / 60000) * 60000 + 60000
                 ORDER BY t2.ts DESC, t2.seq DESC LIMIT 1) as last_value,
                COUNT(*) as count
            FROM telemetry
            WHERE ts >= @StartTs AND ts <= @EndTs
              AND value_type IN (4, 5, 6, 7, 10, 11)  -- 数值类型
            GROUP BY device_id, tag_id, (ts / 60000) * 60000";

        var affected = await db.ExecuteNonQueryAsync(aggregateSql, new { StartTs = startTs, EndTs = endTs }, ct);

        if (affected > 0)
        {
            // 更新处理位置
            await db.ExecuteNonQueryAsync(
                "UPDATE aggregate_state SET last_processed_ts = @Ts WHERE table_name = 'telemetry_1m'",
                new { Ts = targetBucket }, ct);

            _logger.LogDebug("Aggregated {Count} minute buckets (ts: {Start} - {End})", 
                affected, startTs, endTs);
        }
    }

    /// <summary>
    /// 将分钟级数据聚合为小时级
    /// </summary>
    private async Task AggregateToHourAsync(IDbExecutor db, CancellationToken ct)
    {
        // 获取上次处理位置
        var lastTs = await db.ExecuteScalarAsync<long>(
            "SELECT last_processed_ts FROM aggregate_state WHERE table_name = 'telemetry_1h'",
            null, ct);

        // 当前时间向下取整到小时
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var currentHourBucket = (nowMs / 3600000) * 3600000;
        
        // 聚合上一小时的数据
        var targetBucket = currentHourBucket - 3600000;
        
        if (lastTs >= targetBucket)
        {
            return; // 已经处理过了
        }

        const string aggregateSql = @"
            INSERT OR REPLACE INTO telemetry_1h (
                device_id, tag_id, ts_bucket, 
                min_value, max_value, avg_value, first_value, last_value, count
            )
            SELECT 
                device_id,
                tag_id,
                (ts_bucket / 3600000) * 3600000 as hour_bucket,
                MIN(min_value) as min_value,
                MAX(max_value) as max_value,
                SUM(avg_value * count) / SUM(count) as avg_value,
                (SELECT first_value FROM telemetry_1m t2 
                 WHERE t2.device_id = telemetry_1m.device_id 
                   AND t2.tag_id = telemetry_1m.tag_id 
                   AND t2.ts_bucket >= (telemetry_1m.ts_bucket / 3600000) * 3600000
                   AND t2.ts_bucket < (telemetry_1m.ts_bucket / 3600000) * 3600000 + 3600000
                 ORDER BY t2.ts_bucket ASC LIMIT 1) as first_value,
                (SELECT last_value FROM telemetry_1m t2 
                 WHERE t2.device_id = telemetry_1m.device_id 
                   AND t2.tag_id = telemetry_1m.tag_id 
                   AND t2.ts_bucket >= (telemetry_1m.ts_bucket / 3600000) * 3600000
                   AND t2.ts_bucket < (telemetry_1m.ts_bucket / 3600000) * 3600000 + 3600000
                 ORDER BY t2.ts_bucket DESC LIMIT 1) as last_value,
                SUM(count) as count
            FROM telemetry_1m
            WHERE ts_bucket >= @StartTs AND ts_bucket < @EndTs
            GROUP BY device_id, tag_id, (ts_bucket / 3600000) * 3600000";

        var startTs = lastTs > 0 ? lastTs : targetBucket - 3600000;
        var endTs = targetBucket + 3600000;

        var affected = await db.ExecuteNonQueryAsync(aggregateSql, new { StartTs = startTs, EndTs = endTs }, ct);

        if (affected > 0)
        {
            // 更新处理位置
            await db.ExecuteNonQueryAsync(
                "UPDATE aggregate_state SET last_processed_ts = @Ts WHERE table_name = 'telemetry_1h'",
                new { Ts = targetBucket }, ct);

            _logger.LogInformation("Aggregated {Count} hour buckets", affected);
        }
    }
}
