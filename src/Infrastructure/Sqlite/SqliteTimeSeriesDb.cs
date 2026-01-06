using System.Diagnostics;
using IntelliMaint.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// v56.2: SQLite 时序数据库实现
/// </summary>
public sealed class SqliteTimeSeriesDb : ITimeSeriesDb
{
    private readonly IDbExecutor _db;
    private readonly ISchemaManager _schemaManager;
    private readonly ILogger<SqliteTimeSeriesDb> _logger;

    public SqliteTimeSeriesDb(
        IDbExecutor db,
        ISchemaManager schemaManager,
        ILogger<SqliteTimeSeriesDb> logger)
    {
        _db = db;
        _schemaManager = schemaManager;
        _logger = logger;
    }

    public TimeSeriesDbType DbType => TimeSeriesDbType.Sqlite;

    public string Version => "SQLite 3.x";

    public bool SupportsNativeTimeSeries => false;

    public bool SupportsContinuousAggregates => false;

    public async Task<DbHealthStatus> GetHealthAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var diagnostics = new Dictionary<string, object>();

        try
        {
            // 测试连接
            var result = await _db.ExecuteScalarAsync<long>("SELECT 1", null, ct);
            sw.Stop();

            // 获取 PRAGMA 信息
            var journalMode = await _db.ExecuteScalarAsync<string>("PRAGMA journal_mode", null, ct) ?? "unknown";
            var walCheckpoint = await _db.ExecuteScalarAsync<string>("PRAGMA wal_checkpoint", null, ct);
            var pageCount = await _db.ExecuteScalarAsync<long>("PRAGMA page_count", null, ct);
            var pageSize = await _db.ExecuteScalarAsync<long>("PRAGMA page_size", null, ct);
            var freePages = await _db.ExecuteScalarAsync<long>("PRAGMA freelist_count", null, ct);

            diagnostics["journal_mode"] = journalMode;
            diagnostics["page_count"] = pageCount;
            diagnostics["page_size"] = pageSize;
            diagnostics["free_pages"] = freePages;
            diagnostics["database_size_mb"] = Math.Round((pageCount * pageSize) / 1024.0 / 1024.0, 2);
            diagnostics["free_space_mb"] = Math.Round((freePages * pageSize) / 1024.0 / 1024.0, 2);

            return new DbHealthStatus
            {
                IsHealthy = true,
                Status = "Healthy",
                LatencyMs = sw.Elapsed.TotalMilliseconds,
                ActiveConnections = 1, // SQLite 单连接
                MaxConnections = 1,
                IsWritable = true,
                LastCheckedUtc = DateTimeOffset.UtcNow,
                Diagnostics = diagnostics
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Database health check failed");

            return new DbHealthStatus
            {
                IsHealthy = false,
                Status = "Unhealthy",
                LatencyMs = sw.Elapsed.TotalMilliseconds,
                IsWritable = false,
                LastCheckedUtc = DateTimeOffset.UtcNow,
                ErrorMessage = ex.Message,
                Diagnostics = diagnostics
            };
        }
    }

    public async Task<DbStatistics> GetStatisticsAsync(CancellationToken ct = default)
    {
        var tables = new List<TableStatistics>();

        // 获取数据库大小
        var pageCount = await _db.ExecuteScalarAsync<long>("PRAGMA page_count", null, ct);
        var pageSize = await _db.ExecuteScalarAsync<long>("PRAGMA page_size", null, ct);
        var dbSizeBytes = pageCount * pageSize;

        // 获取各表统计
        var tableNames = new[] { "telemetry", "telemetry_1m", "telemetry_1h", "alarm", "device", "tag", "user", "audit_log" };

        foreach (var tableName in tableNames)
        {
            try
            {
                var rowCount = await _db.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {tableName}", null, ct);
                tables.Add(new TableStatistics
                {
                    TableName = tableName,
                    RowCount = rowCount,
                    SizeBytes = 0, // SQLite 不提供单表大小
                    IsHypertable = false
                });
            }
            catch
            {
                // 表可能不存在，忽略
            }
        }

        // 获取遥测数据时间范围
        long? oldestTs = null, newestTs = null;
        try
        {
            oldestTs = await _db.ExecuteScalarAsync<long?>("SELECT MIN(ts) FROM telemetry", null, ct);
            newestTs = await _db.ExecuteScalarAsync<long?>("SELECT MAX(ts) FROM telemetry", null, ct);
        }
        catch { }

        // 获取索引数量
        var indexCount = await _db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index'", null, ct);

        var telemetryTable = tables.FirstOrDefault(t => t.TableName == "telemetry");

        return new DbStatistics
        {
            DatabaseSizeBytes = dbSizeBytes,
            Tables = tables,
            TotalIndexCount = (int)indexCount,
            OldestDataTs = oldestTs,
            NewestDataTs = newestTs,
            TotalTelemetryRows = telemetryTable?.RowCount ?? 0,
            GeneratedUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task<MaintenanceResult> PerformMaintenanceAsync(MaintenanceOptions options, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var operations = new List<string>();
        var warnings = new List<string>();
        long rowsDeleted = 0;
        long spaceReclaimed = 0;

        try
        {
            // 获取初始大小
            var initialPageCount = await _db.ExecuteScalarAsync<long>("PRAGMA page_count", null, ct);
            var pageSize = await _db.ExecuteScalarAsync<long>("PRAGMA page_size", null, ct);
            var initialSize = initialPageCount * pageSize;

            // 1. 清理过期数据
            if (options.CleanupExpiredData)
            {
                _logger.LogInformation("Starting expired data cleanup...");

                // 清理 7 天前的遥测数据
                var cutoffTs = DateTimeOffset.UtcNow.AddDays(-7).ToUnixTimeMilliseconds();
                var deleted = await _db.ExecuteNonQueryAsync(
                    "DELETE FROM telemetry WHERE ts < @CutoffTs",
                    new { CutoffTs = cutoffTs }, ct);
                rowsDeleted += deleted;
                operations.Add($"Deleted {deleted} expired telemetry rows");

                // 清理 30 天前的分钟聚合
                var cutoff1m = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeMilliseconds();
                var deleted1m = await _db.ExecuteNonQueryAsync(
                    "DELETE FROM telemetry_1m WHERE ts_bucket < @CutoffTs",
                    new { CutoffTs = cutoff1m }, ct);
                if (deleted1m > 0)
                {
                    rowsDeleted += deleted1m;
                    operations.Add($"Deleted {deleted1m} expired 1m aggregate rows");
                }
            }

            // 2. 更新统计信息
            if (options.UpdateStatistics)
            {
                await _db.ExecuteNonQueryAsync("ANALYZE", null, ct);
                operations.Add("Updated statistics (ANALYZE)");
            }

            // 3. 执行 VACUUM
            if (options.Vacuum)
            {
                _logger.LogInformation("Running VACUUM...");
                await _db.ExecuteNonQueryAsync("VACUUM", null, ct);
                operations.Add("Vacuum completed");
            }

            // 4. 重建索引
            if (options.ReindexAll)
            {
                _logger.LogInformation("Reindexing all tables...");
                await _db.ExecuteNonQueryAsync("REINDEX", null, ct);
                operations.Add("Reindexed all tables");
            }

            // 计算释放的空间
            var finalPageCount = await _db.ExecuteScalarAsync<long>("PRAGMA page_count", null, ct);
            var finalSize = finalPageCount * pageSize;
            spaceReclaimed = Math.Max(0, initialSize - finalSize);

            sw.Stop();
            _logger.LogInformation(
                "Maintenance completed in {DurationMs}ms. Rows deleted: {RowsDeleted}, Space reclaimed: {SpaceReclaimed}MB",
                sw.ElapsedMilliseconds, rowsDeleted, spaceReclaimed / 1024.0 / 1024.0);

            return new MaintenanceResult
            {
                Success = true,
                DurationMs = sw.ElapsedMilliseconds,
                RowsDeleted = rowsDeleted,
                SpaceReclaimedBytes = spaceReclaimed,
                Operations = operations,
                Warnings = warnings,
                CompletedUtc = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Maintenance failed after {DurationMs}ms", sw.ElapsedMilliseconds);

            return new MaintenanceResult
            {
                Success = false,
                DurationMs = sw.ElapsedMilliseconds,
                RowsDeleted = rowsDeleted,
                SpaceReclaimedBytes = spaceReclaimed,
                Operations = operations,
                Warnings = warnings,
                Error = ex.Message,
                CompletedUtc = DateTimeOffset.UtcNow
            };
        }
    }
}
