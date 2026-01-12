using Dapper;
using IntelliMaint.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB Schema manager - verifies schema exists (schema created by Docker init scripts)
/// </summary>
public sealed class SchemaManager
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<SchemaManager> _logger;

    public SchemaManager(INpgsqlConnectionFactory factory, ILogger<SchemaManager> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Verifying TimescaleDB schema...");

        try
        {
            using var conn = _factory.CreateConnection();

            // Verify schema version table exists
            var version = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT version FROM schema_version ORDER BY version DESC LIMIT 1",
                cancellationToken: ct));

            if (version.HasValue)
            {
                _logger.LogInformation("TimescaleDB schema verified. Version: {Version}", version.Value);
            }
            else
            {
                _logger.LogWarning("TimescaleDB schema_version table is empty. Schema may not be properly initialized.");
            }

            // Verify key tables exist
            var tableCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public'",
                cancellationToken: ct));

            _logger.LogInformation("Found {TableCount} tables in TimescaleDB", tableCount);

            // Verify hypertables
            var hypertableCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM timescaledb_information.hypertables",
                cancellationToken: ct));

            _logger.LogInformation("Found {HypertableCount} hypertables", hypertableCount);

            // v61: Ensure tag_importance_config table exists (for migrations from older versions)
            await EnsureTagImportanceTableAsync(conn, ct);

            // v62: Ensure tag_correlation_rule table exists (for migrations from older versions)
            await EnsureTagCorrelationRuleTableAsync(conn, ct);

            // v65: Ensure edge_config tables exist (for migrations from older versions)
            await EnsureEdgeConfigTablesAsync(conn, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify TimescaleDB schema. Ensure database is initialized with Docker init scripts.");
            throw new InvalidOperationException(
                "TimescaleDB schema verification failed. Please ensure the database container is running and initialized.",
                ex);
        }
    }

    /// <summary>
    /// v61: 确保 tag_importance_config 表存在（用于从旧版本迁移）
    /// </summary>
    private async Task EnsureTagImportanceTableAsync(Npgsql.NpgsqlConnection conn, CancellationToken ct)
    {
        const string checkSql = @"
            SELECT EXISTS (
                SELECT FROM information_schema.tables
                WHERE table_schema = 'public'
                AND table_name = 'tag_importance_config'
            )";

        var exists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(checkSql, cancellationToken: ct));
        if (exists)
        {
            _logger.LogDebug("tag_importance_config table already exists");
            return;
        }

        _logger.LogInformation("Creating tag_importance_config table...");

        const string createSql = @"
            CREATE TABLE tag_importance_config (
                id SERIAL PRIMARY KEY,
                pattern TEXT NOT NULL,
                importance INTEGER NOT NULL DEFAULT 40,
                description TEXT,
                priority INTEGER NOT NULL DEFAULT 0,
                enabled BOOLEAN NOT NULL DEFAULT TRUE,
                created_utc BIGINT NOT NULL,
                updated_utc BIGINT NOT NULL
            );
            CREATE INDEX idx_tag_importance_enabled ON tag_importance_config (enabled) WHERE enabled = TRUE;
            CREATE INDEX idx_tag_importance_priority ON tag_importance_config (priority DESC);
        ";

        await conn.ExecuteAsync(new CommandDefinition(createSql, cancellationToken: ct));

        // Insert default rules
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const string insertSql = @"
            INSERT INTO tag_importance_config (pattern, importance, description, priority, enabled, created_utc, updated_utc)
            VALUES
                ('*Temperature*', 100, '温度类标签 - 关键指标', 100, TRUE, @NowUtc, @NowUtc),
                ('*Current*', 100, '电流类标签 - 关键指标', 99, TRUE, @NowUtc, @NowUtc),
                ('*Vibration*', 100, '振动类标签 - 关键指标', 98, TRUE, @NowUtc, @NowUtc),
                ('*Pressure*', 70, '压力类标签 - 重要指标', 70, TRUE, @NowUtc, @NowUtc),
                ('*Speed*', 70, '速度类标签 - 重要指标', 69, TRUE, @NowUtc, @NowUtc),
                ('*Position*', 40, '位置类标签 - 次要指标', 50, TRUE, @NowUtc, @NowUtc),
                ('*Humidity*', 20, '湿度类标签 - 辅助指标', 20, TRUE, @NowUtc, @NowUtc)
        ";

        await conn.ExecuteAsync(new CommandDefinition(insertSql, new { NowUtc = nowUtc }, cancellationToken: ct));

        _logger.LogInformation("tag_importance_config table created with default rules");
    }

    /// <summary>
    /// v62: 确保 tag_correlation_rule 表存在（用于从旧版本迁移）
    /// </summary>
    private async Task EnsureTagCorrelationRuleTableAsync(Npgsql.NpgsqlConnection conn, CancellationToken ct)
    {
        const string checkSql = @"
            SELECT EXISTS (
                SELECT FROM information_schema.tables
                WHERE table_schema = 'public'
                AND table_name = 'tag_correlation_rule'
            )";

        var exists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(checkSql, cancellationToken: ct));
        if (exists)
        {
            _logger.LogDebug("tag_correlation_rule table already exists");
            return;
        }

        _logger.LogInformation("Creating tag_correlation_rule table...");

        const string createSql = @"
            CREATE TABLE tag_correlation_rule (
                id SERIAL PRIMARY KEY,
                name TEXT NOT NULL,
                device_pattern TEXT,
                tag1_pattern TEXT NOT NULL,
                tag2_pattern TEXT NOT NULL,
                correlation_type INTEGER NOT NULL DEFAULT 0,
                threshold DOUBLE PRECISION NOT NULL DEFAULT 0.7,
                risk_description TEXT,
                penalty_score INTEGER NOT NULL DEFAULT 15,
                enabled BOOLEAN NOT NULL DEFAULT TRUE,
                priority INTEGER NOT NULL DEFAULT 0,
                created_utc BIGINT NOT NULL,
                updated_utc BIGINT NOT NULL
            );
            CREATE INDEX idx_tag_correlation_enabled ON tag_correlation_rule (enabled, priority DESC);
        ";

        await conn.ExecuteAsync(new CommandDefinition(createSql, cancellationToken: ct));

        // Insert default rules
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const string insertSql = @"
            INSERT INTO tag_correlation_rule (name, device_pattern, tag1_pattern, tag2_pattern, correlation_type, threshold, risk_description, penalty_score, enabled, priority, created_utc, updated_utc)
            VALUES
                ('温度电流同升', '*', '*Temperature*', '*Current*', 0, 0.7, '温度和电流同时升高，可能过载', 20, TRUE, 100, @NowUtc, @NowUtc),
                ('振动温度同升', '*', '*Vibration*', '*Temperature*', 0, 0.7, '振动和温度同时升高，可能轴承故障', 25, TRUE, 99, @NowUtc, @NowUtc),
                ('压力流量反向', '*', '*Pressure*', '*Flow*', 1, 0.6, '压力升高流量降低，可能管路堵塞', 20, TRUE, 98, @NowUtc, @NowUtc)
        ";

        await conn.ExecuteAsync(new CommandDefinition(insertSql, new { NowUtc = nowUtc }, cancellationToken: ct));

        _logger.LogInformation("tag_correlation_rule table created with default rules");
    }

    /// <summary>
    /// v65: 确保 edge_config 表存在（用于从旧版本迁移）
    /// </summary>
    private async Task EnsureEdgeConfigTablesAsync(Npgsql.NpgsqlConnection conn, CancellationToken ct)
    {
        // 检查 edge_config 表是否存在
        const string checkEdgeConfigSql = @"
            SELECT EXISTS (
                SELECT FROM information_schema.tables
                WHERE table_schema = 'public'
                AND table_name = 'edge_config'
            )";

        var edgeConfigExists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(checkEdgeConfigSql, cancellationToken: ct));

        if (!edgeConfigExists)
        {
            _logger.LogInformation("Creating edge_config table...");

            const string createEdgeConfigSql = @"
                CREATE TABLE edge_config (
                    edge_id VARCHAR(64) PRIMARY KEY,
                    name VARCHAR(128) NOT NULL,
                    description TEXT,
                    processing_enabled BOOLEAN NOT NULL DEFAULT TRUE,
                    default_deadband DOUBLE PRECISION NOT NULL DEFAULT 0.01,
                    default_deadband_percent DOUBLE PRECISION NOT NULL DEFAULT 0.5,
                    default_min_interval_ms INTEGER NOT NULL DEFAULT 1000,
                    force_upload_interval_ms INTEGER NOT NULL DEFAULT 60000,
                    outlier_enabled BOOLEAN NOT NULL DEFAULT TRUE,
                    outlier_sigma_threshold DOUBLE PRECISION NOT NULL DEFAULT 4.0,
                    outlier_action VARCHAR(16) NOT NULL DEFAULT 'Mark',
                    store_forward_enabled BOOLEAN NOT NULL DEFAULT TRUE,
                    max_store_size_mb INTEGER NOT NULL DEFAULT 1000,
                    retention_days INTEGER NOT NULL DEFAULT 7,
                    compression_enabled BOOLEAN NOT NULL DEFAULT TRUE,
                    compression_algorithm VARCHAR(16) NOT NULL DEFAULT 'Gzip',
                    health_check_interval_ms INTEGER NOT NULL DEFAULT 5000,
                    health_check_timeout_ms INTEGER NOT NULL DEFAULT 3000,
                    offline_threshold INTEGER NOT NULL DEFAULT 3,
                    send_batch_size INTEGER NOT NULL DEFAULT 500,
                    send_interval_ms INTEGER NOT NULL DEFAULT 500,
                    created_utc BIGINT NOT NULL,
                    updated_utc BIGINT,
                    updated_by VARCHAR(64)
                );
            ";

            await conn.ExecuteAsync(new CommandDefinition(createEdgeConfigSql, cancellationToken: ct));
            _logger.LogInformation("edge_config table created");
        }

        // 检查 tag_processing_config 表是否存在
        const string checkTagProcessingSql = @"
            SELECT EXISTS (
                SELECT FROM information_schema.tables
                WHERE table_schema = 'public'
                AND table_name = 'tag_processing_config'
            )";

        var tagProcessingExists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(checkTagProcessingSql, cancellationToken: ct));

        if (!tagProcessingExists)
        {
            _logger.LogInformation("Creating tag_processing_config table...");

            const string createTagProcessingSql = @"
                CREATE TABLE tag_processing_config (
                    id SERIAL PRIMARY KEY,
                    edge_id VARCHAR(64) NOT NULL,
                    tag_id VARCHAR(128) NOT NULL,
                    deadband DOUBLE PRECISION,
                    deadband_percent DOUBLE PRECISION,
                    min_interval_ms INTEGER,
                    bypass BOOLEAN NOT NULL DEFAULT FALSE,
                    priority INTEGER NOT NULL DEFAULT 0,
                    description TEXT,
                    created_utc BIGINT NOT NULL,
                    updated_utc BIGINT,
                    UNIQUE(edge_id, tag_id)
                );
                CREATE INDEX idx_tag_processing_edge ON tag_processing_config(edge_id);
            ";

            await conn.ExecuteAsync(new CommandDefinition(createTagProcessingSql, cancellationToken: ct));
            _logger.LogInformation("tag_processing_config table created");
        }

        // 检查 edge_status 表是否存在
        const string checkEdgeStatusSql = @"
            SELECT EXISTS (
                SELECT FROM information_schema.tables
                WHERE table_schema = 'public'
                AND table_name = 'edge_status'
            )";

        var edgeStatusExists = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(checkEdgeStatusSql, cancellationToken: ct));

        if (!edgeStatusExists)
        {
            _logger.LogInformation("Creating edge_status table...");

            const string createEdgeStatusSql = @"
                CREATE TABLE edge_status (
                    edge_id VARCHAR(64) PRIMARY KEY,
                    is_online BOOLEAN NOT NULL DEFAULT FALSE,
                    pending_points INTEGER NOT NULL DEFAULT 0,
                    filter_rate DOUBLE PRECISION NOT NULL DEFAULT 0,
                    sent_count BIGINT NOT NULL DEFAULT 0,
                    stored_mb DOUBLE PRECISION NOT NULL DEFAULT 0,
                    last_heartbeat_utc BIGINT NOT NULL,
                    version VARCHAR(32)
                );
            ";

            await conn.ExecuteAsync(new CommandDefinition(createEdgeStatusSql, cancellationToken: ct));
            _logger.LogInformation("edge_status table created");
        }
    }
}
