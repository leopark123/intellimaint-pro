using System.Diagnostics;
using Dapper;
using IntelliMaint.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB 时序数据库实现
/// </summary>
public sealed class TimescaleDbTimeSeriesDb : ITimeSeriesDb
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<TimescaleDbTimeSeriesDb> _logger;
    private string? _cachedVersion;

    public TimescaleDbTimeSeriesDb(
        INpgsqlConnectionFactory factory,
        ILogger<TimescaleDbTimeSeriesDb> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public TimeSeriesDbType DbType => TimeSeriesDbType.TimescaleDb;
    public string Version => _cachedVersion ?? "unknown";
    public bool SupportsNativeTimeSeries => true;
    public bool SupportsContinuousAggregates => true;

    public async Task<DbHealthStatus> GetHealthAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            using var conn = _factory.CreateConnection();

            // 测试基本查询
            var version = await conn.ExecuteScalarAsync<string>(
                new CommandDefinition("SELECT version()", cancellationToken: ct));

            // 获取 TimescaleDB 版本
            var tsVersion = await conn.ExecuteScalarAsync<string>(
                new CommandDefinition(
                    "SELECT extversion FROM pg_extension WHERE extname = 'timescaledb'",
                    cancellationToken: ct));

            // 获取数据库大小
            var dbSize = await conn.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    "SELECT pg_database_size(current_database())",
                    cancellationToken: ct));

            // 获取连接数
            var connections = await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()",
                    cancellationToken: ct));

            sw.Stop();

            // 缓存版本信息
            _cachedVersion = tsVersion ?? "unknown";

            return new DbHealthStatus
            {
                IsHealthy = true,
                Status = "Healthy",
                LatencyMs = sw.ElapsedMilliseconds,
                ActiveConnections = connections,
                MaxConnections = 100,
                IsWritable = true,
                LastCheckedUtc = DateTimeOffset.UtcNow,
                Diagnostics = new Dictionary<string, object>
                {
                    ["postgres_version"] = version ?? "unknown",
                    ["timescaledb_version"] = tsVersion ?? "unknown",
                    ["database_size_bytes"] = dbSize,
                    ["database_size_mb"] = Math.Round(dbSize / 1024.0 / 1024.0, 2),
                    ["active_connections"] = connections
                }
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "TimescaleDB health check failed");

            return new DbHealthStatus
            {
                IsHealthy = false,
                Status = "Unhealthy",
                LatencyMs = sw.ElapsedMilliseconds,
                IsWritable = false,
                LastCheckedUtc = DateTimeOffset.UtcNow,
                ErrorMessage = ex.Message,
                Diagnostics = new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                }
            };
        }
    }

    public async Task<DbStatistics> GetStatisticsAsync(CancellationToken ct = default)
    {
        try
        {
            using var conn = _factory.CreateConnection();

            // 获取数据库大小
            var dbSize = await conn.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    "SELECT pg_database_size(current_database())",
                    cancellationToken: ct));

            // 获取表统计
            var tableStats = await conn.QueryAsync<(string TableName, long RowCount, long SizeBytes)>(
                new CommandDefinition(
                    @"SELECT
                        relname as TableName,
                        n_live_tup as RowCount,
                        pg_total_relation_size(relid) as SizeBytes
                    FROM pg_stat_user_tables
                    ORDER BY n_live_tup DESC",
                    cancellationToken: ct));

            // 获取索引数量
            var indexCount = await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    "SELECT count(*) FROM pg_indexes WHERE schemaname = 'public'",
                    cancellationToken: ct));

            // 获取遥测数据行数（使用 TimescaleDB 的 hypertable chunks 统计）
            // 对于 hypertable，数据分布在多个 chunk 中，需要汇总所有 chunk 的行数
            var telemetryCount = await conn.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    @"SELECT COALESCE(
                        (SELECT SUM(n_live_tup) FROM pg_stat_user_tables pst
                         JOIN timescaledb_information.chunks c
                         ON pst.relname = c.chunk_name AND pst.schemaname = c.chunk_schema
                         WHERE c.hypertable_name = 'telemetry'),
                        0
                    )",
                    cancellationToken: ct));

            var tables = tableStats.Select(t => new TableStatistics
            {
                TableName = t.TableName,
                RowCount = t.RowCount,
                SizeBytes = t.SizeBytes
            }).ToList();

            return new DbStatistics
            {
                DatabaseSizeBytes = dbSize,
                TotalTelemetryRows = telemetryCount,
                TotalIndexCount = indexCount,
                Tables = tables,
                GeneratedUtc = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get TimescaleDB statistics");
            return new DbStatistics
            {
                DatabaseSizeBytes = 0,
                TotalTelemetryRows = 0,
                TotalIndexCount = 0,
                Tables = new List<TableStatistics>(),
                GeneratedUtc = DateTimeOffset.UtcNow
            };
        }
    }

    public async Task<MaintenanceResult> PerformMaintenanceAsync(
        MaintenanceOptions options,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var operations = new List<string>();
        var warnings = new List<string>();
        long spaceReclaimed = 0;
        int chunksCompressed = 0;

        try
        {
            using var conn = _factory.CreateConnection();

            // 获取维护前的大小
            var sizeBefore = await conn.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    "SELECT pg_database_size(current_database())",
                    cancellationToken: ct));

            // 运行 VACUUM ANALYZE
            if (options.Vacuum)
            {
                operations.Add("Starting VACUUM ANALYZE...");
                await conn.ExecuteAsync(
                    new CommandDefinition("VACUUM ANALYZE", cancellationToken: ct));
                operations.Add("VACUUM ANALYZE completed");
            }

            // 更新统计信息
            if (options.UpdateStatistics)
            {
                operations.Add("Updating statistics...");
                await conn.ExecuteAsync(
                    new CommandDefinition("ANALYZE", cancellationToken: ct));
                operations.Add("Statistics updated");
            }

            // 压缩旧数据块（TimescaleDB 特性）
            if (options.CompressOldChunks)
            {
                operations.Add("Compressing old chunks...");

                // 手动压缩 N 天前的 telemetry 数据
                var cutoffTs = DateTimeOffset.UtcNow.AddDays(-options.CompressAfterDays).ToUnixTimeMilliseconds();
                try
                {
                    chunksCompressed = await conn.ExecuteScalarAsync<int>(
                        new CommandDefinition(
                            @"SELECT count(*) FROM (
                                SELECT compress_chunk(c.chunk_name::regclass)
                                FROM timescaledb_information.chunks c
                                WHERE c.hypertable_name = 'telemetry'
                                AND NOT c.is_compressed
                                AND c.range_end < to_timestamp(@CutoffTs / 1000.0)
                            ) x",
                            new { CutoffTs = cutoffTs },
                            cancellationToken: ct));
                    operations.Add($"Compressed {chunksCompressed} chunks");
                }
                catch (Exception ex)
                {
                    warnings.Add($"Chunk compression failed: {ex.Message}");
                }
            }

            // 获取维护后的大小
            var sizeAfter = await conn.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    "SELECT pg_database_size(current_database())",
                    cancellationToken: ct));

            spaceReclaimed = sizeBefore - sizeAfter;
            sw.Stop();

            operations.Add($"Maintenance completed. Space reclaimed: {spaceReclaimed} bytes");

            _logger.LogInformation(
                "TimescaleDB maintenance completed. Space reclaimed: {SpaceReclaimed} bytes",
                spaceReclaimed);

            return new MaintenanceResult
            {
                Success = true,
                DurationMs = sw.ElapsedMilliseconds,
                SpaceReclaimedBytes = spaceReclaimed,
                ChunksCompressed = chunksCompressed,
                Operations = operations,
                Warnings = warnings,
                CompletedUtc = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            operations.Add($"Error: {ex.Message}");
            _logger.LogError(ex, "TimescaleDB maintenance failed");

            return new MaintenanceResult
            {
                Success = false,
                DurationMs = sw.ElapsedMilliseconds,
                SpaceReclaimedBytes = spaceReclaimed,
                ChunksCompressed = chunksCompressed,
                Operations = operations,
                Warnings = warnings,
                Error = ex.Message,
                CompletedUtc = DateTimeOffset.UtcNow
            };
        }
    }
}
