using Dapper;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// SQLite Edge 配置仓储实现
/// </summary>
public sealed class EdgeConfigRepository : IEdgeConfigRepository
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly ILogger<EdgeConfigRepository> _logger;

    public EdgeConfigRepository(ISqliteConnectionFactory factory, ILogger<EdgeConfigRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<EdgeConfigDto?> GetAsync(string edgeId, CancellationToken ct)
    {
        const string sql = @"
            SELECT
                edge_id AS EdgeId,
                name AS Name,
                description AS Description,
                processing_enabled AS ProcessingEnabled,
                default_deadband AS DefaultDeadband,
                default_deadband_percent AS DefaultDeadbandPercent,
                default_min_interval_ms AS DefaultMinIntervalMs,
                force_upload_interval_ms AS ForceUploadIntervalMs,
                outlier_enabled AS OutlierEnabled,
                outlier_sigma_threshold AS OutlierSigmaThreshold,
                outlier_action AS OutlierAction,
                store_forward_enabled AS StoreForwardEnabled,
                max_store_size_mb AS MaxStoreSizeMB,
                retention_days AS RetentionDays,
                compression_enabled AS CompressionEnabled,
                compression_algorithm AS CompressionAlgorithm,
                health_check_interval_ms AS HealthCheckIntervalMs,
                health_check_timeout_ms AS HealthCheckTimeoutMs,
                offline_threshold AS OfflineThreshold,
                send_batch_size AS SendBatchSize,
                send_interval_ms AS SendIntervalMs,
                created_utc AS CreatedUtc,
                updated_utc AS UpdatedUtc,
                updated_by AS UpdatedBy
            FROM edge_config
            WHERE edge_id = @EdgeId
        ";

        using var conn = _factory.CreateConnection();
        var row = await conn.QueryFirstOrDefaultAsync<EdgeConfigRow>(
            new CommandDefinition(sql, new { EdgeId = edgeId }, cancellationToken: ct));

        return row?.ToDto();
    }

    public async Task<IReadOnlyList<EdgeSummaryDto>> ListAllAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT
                ec.edge_id AS EdgeId,
                ec.name AS Name,
                ec.description AS Description,
                COALESCE(es.is_online, 0) AS IsOnline,
                es.last_heartbeat_utc AS LastHeartbeatUtc,
                (SELECT COUNT(*) FROM device d WHERE d.device_id LIKE ec.edge_id || '%') AS DeviceCount,
                (SELECT COUNT(*) FROM tag t JOIN device d ON t.device_id = d.device_id WHERE d.device_id LIKE ec.edge_id || '%') AS TagCount
            FROM edge_config ec
            LEFT JOIN edge_status es ON ec.edge_id = es.edge_id
            ORDER BY ec.name
        ";

        using var conn = _factory.CreateConnection();
        var result = await conn.QueryAsync<EdgeSummaryDto>(
            new CommandDefinition(sql, cancellationToken: ct));

        return result.ToList();
    }

    public async Task UpsertAsync(EdgeConfigDto config, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO edge_config (
                edge_id, name, description,
                processing_enabled, default_deadband, default_deadband_percent,
                default_min_interval_ms, force_upload_interval_ms,
                outlier_enabled, outlier_sigma_threshold, outlier_action,
                store_forward_enabled, max_store_size_mb, retention_days,
                compression_enabled, compression_algorithm,
                health_check_interval_ms, health_check_timeout_ms, offline_threshold,
                send_batch_size, send_interval_ms,
                created_utc, updated_utc, updated_by
            ) VALUES (
                @EdgeId, @Name, @Description,
                @ProcessingEnabled, @DefaultDeadband, @DefaultDeadbandPercent,
                @DefaultMinIntervalMs, @ForceUploadIntervalMs,
                @OutlierEnabled, @OutlierSigmaThreshold, @OutlierAction,
                @StoreForwardEnabled, @MaxStoreSizeMB, @RetentionDays,
                @CompressionEnabled, @CompressionAlgorithm,
                @HealthCheckIntervalMs, @HealthCheckTimeoutMs, @OfflineThreshold,
                @SendBatchSize, @SendIntervalMs,
                @CreatedUtc, @UpdatedUtc, @UpdatedBy
            )
            ON CONFLICT(edge_id) DO UPDATE SET
                name = @Name,
                description = @Description,
                processing_enabled = @ProcessingEnabled,
                default_deadband = @DefaultDeadband,
                default_deadband_percent = @DefaultDeadbandPercent,
                default_min_interval_ms = @DefaultMinIntervalMs,
                force_upload_interval_ms = @ForceUploadIntervalMs,
                outlier_enabled = @OutlierEnabled,
                outlier_sigma_threshold = @OutlierSigmaThreshold,
                outlier_action = @OutlierAction,
                store_forward_enabled = @StoreForwardEnabled,
                max_store_size_mb = @MaxStoreSizeMB,
                retention_days = @RetentionDays,
                compression_enabled = @CompressionEnabled,
                compression_algorithm = @CompressionAlgorithm,
                health_check_interval_ms = @HealthCheckIntervalMs,
                health_check_timeout_ms = @HealthCheckTimeoutMs,
                offline_threshold = @OfflineThreshold,
                send_batch_size = @SendBatchSize,
                send_interval_ms = @SendIntervalMs,
                updated_utc = @UpdatedUtc,
                updated_by = @UpdatedBy
        ";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            config.EdgeId,
            config.Name,
            config.Description,
            ProcessingEnabled = config.Processing.Enabled ? 1 : 0,
            config.Processing.DefaultDeadband,
            config.Processing.DefaultDeadbandPercent,
            config.Processing.DefaultMinIntervalMs,
            config.Processing.ForceUploadIntervalMs,
            OutlierEnabled = config.Processing.OutlierEnabled ? 1 : 0,
            config.Processing.OutlierSigmaThreshold,
            config.Processing.OutlierAction,
            StoreForwardEnabled = config.StoreForward.Enabled ? 1 : 0,
            config.StoreForward.MaxStoreSizeMB,
            config.StoreForward.RetentionDays,
            CompressionEnabled = config.StoreForward.CompressionEnabled ? 1 : 0,
            config.StoreForward.CompressionAlgorithm,
            config.Network.HealthCheckIntervalMs,
            config.Network.HealthCheckTimeoutMs,
            config.Network.OfflineThreshold,
            config.Network.SendBatchSize,
            config.Network.SendIntervalMs,
            config.CreatedUtc,
            config.UpdatedUtc,
            config.UpdatedBy
        }, cancellationToken: ct));

        _logger.LogDebug("Upserted edge config: {EdgeId}", config.EdgeId);
    }

    public async Task DeleteAsync(string edgeId, CancellationToken ct)
    {
        const string sql = "DELETE FROM edge_config WHERE edge_id = @EdgeId";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { EdgeId = edgeId }, cancellationToken: ct));

        _logger.LogInformation("Deleted edge config: {EdgeId}", edgeId);
    }

    private class EdgeConfigRow
    {
        public string EdgeId { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public bool ProcessingEnabled { get; set; }
        public double DefaultDeadband { get; set; }
        public double DefaultDeadbandPercent { get; set; }
        public int DefaultMinIntervalMs { get; set; }
        public int ForceUploadIntervalMs { get; set; }
        public bool OutlierEnabled { get; set; }
        public double OutlierSigmaThreshold { get; set; }
        public string OutlierAction { get; set; } = "Mark";
        public bool StoreForwardEnabled { get; set; }
        public int MaxStoreSizeMB { get; set; }
        public int RetentionDays { get; set; }
        public bool CompressionEnabled { get; set; }
        public string CompressionAlgorithm { get; set; } = "Gzip";
        public int HealthCheckIntervalMs { get; set; }
        public int HealthCheckTimeoutMs { get; set; }
        public int OfflineThreshold { get; set; }
        public int SendBatchSize { get; set; }
        public int SendIntervalMs { get; set; }
        public long CreatedUtc { get; set; }
        public long? UpdatedUtc { get; set; }
        public string? UpdatedBy { get; set; }

        public EdgeConfigDto ToDto() => new()
        {
            EdgeId = EdgeId,
            Name = Name,
            Description = Description,
            Processing = new ProcessingConfigDto
            {
                Enabled = ProcessingEnabled,
                DefaultDeadband = DefaultDeadband,
                DefaultDeadbandPercent = DefaultDeadbandPercent,
                DefaultMinIntervalMs = DefaultMinIntervalMs,
                ForceUploadIntervalMs = ForceUploadIntervalMs,
                OutlierEnabled = OutlierEnabled,
                OutlierSigmaThreshold = OutlierSigmaThreshold,
                OutlierAction = OutlierAction
            },
            StoreForward = new StoreForwardConfigDto
            {
                Enabled = StoreForwardEnabled,
                MaxStoreSizeMB = MaxStoreSizeMB,
                RetentionDays = RetentionDays,
                CompressionEnabled = CompressionEnabled,
                CompressionAlgorithm = CompressionAlgorithm
            },
            Network = new NetworkConfigDto
            {
                HealthCheckIntervalMs = HealthCheckIntervalMs,
                HealthCheckTimeoutMs = HealthCheckTimeoutMs,
                OfflineThreshold = OfflineThreshold,
                SendBatchSize = SendBatchSize,
                SendIntervalMs = SendIntervalMs
            },
            CreatedUtc = CreatedUtc,
            UpdatedUtc = UpdatedUtc,
            UpdatedBy = UpdatedBy
        };
    }
}

/// <summary>
/// SQLite 标签处理配置仓储实现
/// </summary>
public sealed class TagProcessingConfigRepository : ITagProcessingConfigRepository
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly ILogger<TagProcessingConfigRepository> _logger;

    public TagProcessingConfigRepository(ISqliteConnectionFactory factory, ILogger<TagProcessingConfigRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<PagedTagConfigResult> ListByEdgeAsync(string edgeId, int page, int pageSize, string? search, CancellationToken ct)
    {
        var offset = (page - 1) * pageSize;
        var whereClause = "WHERE edge_id = @EdgeId";
        if (!string.IsNullOrWhiteSpace(search))
        {
            whereClause += " AND tag_id LIKE @Search";
        }

        var countSql = $"SELECT COUNT(*) FROM tag_processing_config {whereClause}";
        var dataSql = $@"
            SELECT
                id AS Id,
                edge_id AS EdgeId,
                tag_id AS TagId,
                deadband AS Deadband,
                deadband_percent AS DeadbandPercent,
                min_interval_ms AS MinIntervalMs,
                bypass AS Bypass,
                priority AS Priority,
                description AS Description,
                created_utc AS CreatedUtc,
                updated_utc AS UpdatedUtc
            FROM tag_processing_config
            {whereClause}
            ORDER BY priority DESC, tag_id
            LIMIT @PageSize OFFSET @Offset
        ";

        using var conn = _factory.CreateConnection();
        var total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            countSql, new { EdgeId = edgeId, Search = $"%{search}%" }, cancellationToken: ct));

        var items = await conn.QueryAsync<TagProcessingConfigDto>(new CommandDefinition(
            dataSql, new { EdgeId = edgeId, Search = $"%{search}%", PageSize = pageSize, Offset = offset }, cancellationToken: ct));

        return new PagedTagConfigResult
        {
            Items = items.ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<TagProcessingConfigDto?> GetAsync(string edgeId, string tagId, CancellationToken ct)
    {
        const string sql = @"
            SELECT
                id AS Id,
                edge_id AS EdgeId,
                tag_id AS TagId,
                deadband AS Deadband,
                deadband_percent AS DeadbandPercent,
                min_interval_ms AS MinIntervalMs,
                bypass AS Bypass,
                priority AS Priority,
                description AS Description,
                created_utc AS CreatedUtc,
                updated_utc AS UpdatedUtc
            FROM tag_processing_config
            WHERE edge_id = @EdgeId AND tag_id = @TagId
        ";

        using var conn = _factory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<TagProcessingConfigDto>(
            new CommandDefinition(sql, new { EdgeId = edgeId, TagId = tagId }, cancellationToken: ct));
    }

    public async Task BatchUpsertAsync(string edgeId, IEnumerable<TagProcessingConfigDto> configs, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO tag_processing_config (
                edge_id, tag_id, deadband, deadband_percent, min_interval_ms,
                bypass, priority, description, created_utc, updated_utc
            ) VALUES (
                @EdgeId, @TagId, @Deadband, @DeadbandPercent, @MinIntervalMs,
                @Bypass, @Priority, @Description, @CreatedUtc, @UpdatedUtc
            )
            ON CONFLICT(edge_id, tag_id) DO UPDATE SET
                deadband = @Deadband,
                deadband_percent = @DeadbandPercent,
                min_interval_ms = @MinIntervalMs,
                bypass = @Bypass,
                priority = @Priority,
                description = @Description,
                updated_utc = @UpdatedUtc
        ";

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var conn = _factory.CreateConnection();
        using var transaction = conn.BeginTransaction();

        try
        {
            foreach (var config in configs)
            {
                await conn.ExecuteAsync(new CommandDefinition(sql, new
                {
                    EdgeId = edgeId,
                    config.TagId,
                    config.Deadband,
                    config.DeadbandPercent,
                    config.MinIntervalMs,
                    Bypass = config.Bypass ? 1 : 0,
                    config.Priority,
                    config.Description,
                    CreatedUtc = now,
                    UpdatedUtc = now
                }, transaction: transaction, cancellationToken: ct));
            }

            await transaction.CommitAsync(ct);
            _logger.LogDebug("Batch upserted {Count} tag processing configs for edge {EdgeId}", configs.Count(), edgeId);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task DeleteAsync(string edgeId, string tagId, CancellationToken ct)
    {
        const string sql = "DELETE FROM tag_processing_config WHERE edge_id = @EdgeId AND tag_id = @TagId";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { EdgeId = edgeId, TagId = tagId }, cancellationToken: ct));
    }

    public async Task DeleteByEdgeAsync(string edgeId, CancellationToken ct)
    {
        const string sql = "DELETE FROM tag_processing_config WHERE edge_id = @EdgeId";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new { EdgeId = edgeId }, cancellationToken: ct));
    }
}

/// <summary>
/// SQLite Edge 状态仓储实现
/// </summary>
public sealed class EdgeStatusRepository : IEdgeStatusRepository
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly ILogger<EdgeStatusRepository> _logger;

    public EdgeStatusRepository(ISqliteConnectionFactory factory, ILogger<EdgeStatusRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<EdgeStatusDto?> GetAsync(string edgeId, CancellationToken ct)
    {
        const string sql = @"
            SELECT
                edge_id AS EdgeId,
                is_online AS IsOnline,
                pending_points AS PendingPoints,
                filter_rate AS FilterRate,
                sent_count AS SentCount,
                stored_mb AS StoredMB,
                last_heartbeat_utc AS LastHeartbeatUtc,
                version AS Version
            FROM edge_status
            WHERE edge_id = @EdgeId
        ";

        using var conn = _factory.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<EdgeStatusDto>(
            new CommandDefinition(sql, new { EdgeId = edgeId }, cancellationToken: ct));
    }

    public async Task UpdateAsync(EdgeStatusDto status, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO edge_status (
                edge_id, is_online, pending_points, filter_rate,
                sent_count, stored_mb, last_heartbeat_utc, version
            ) VALUES (
                @EdgeId, @IsOnline, @PendingPoints, @FilterRate,
                @SentCount, @StoredMB, @LastHeartbeatUtc, @Version
            )
            ON CONFLICT(edge_id) DO UPDATE SET
                is_online = @IsOnline,
                pending_points = @PendingPoints,
                filter_rate = @FilterRate,
                sent_count = @SentCount,
                stored_mb = @StoredMB,
                last_heartbeat_utc = @LastHeartbeatUtc,
                version = @Version
        ";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            status.EdgeId,
            IsOnline = status.IsOnline ? 1 : 0,
            status.PendingPoints,
            status.FilterRate,
            status.SentCount,
            status.StoredMB,
            status.LastHeartbeatUtc,
            status.Version
        }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<EdgeStatusDto>> ListAllAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT
                edge_id AS EdgeId,
                is_online AS IsOnline,
                pending_points AS PendingPoints,
                filter_rate AS FilterRate,
                sent_count AS SentCount,
                stored_mb AS StoredMB,
                last_heartbeat_utc AS LastHeartbeatUtc,
                version AS Version
            FROM edge_status
            ORDER BY edge_id
        ";

        using var conn = _factory.CreateConnection();
        var result = await conn.QueryAsync<EdgeStatusDto>(new CommandDefinition(sql, cancellationToken: ct));
        return result.ToList();
    }
}
