using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// 数据库Schema管理器
/// </summary>
public interface ISchemaManager
{
    /// <summary>初始化数据库Schema</summary>
    Task InitializeAsync(CancellationToken ct = default);
    
    /// <summary>获取当前版本</summary>
    Task<int> GetVersionAsync(CancellationToken ct = default);
}

/// <summary>
/// Schema管理器实现
/// </summary>
public sealed class SchemaManager : ISchemaManager
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly ILogger<SchemaManager> _logger;
    
    private const int CurrentVersion = 11;  // v56.1: 性能优化索引
    
    public SchemaManager(ISqliteConnectionFactory factory, ILogger<SchemaManager> logger)
    {
        _factory = factory;
        _logger = logger;
    }
    
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Initializing database schema...");
        
        using var conn = _factory.CreateConnection();
        
        // 创建版本表
        await ExecuteAsync(conn, SchemaV1.CreateVersionTable, ct);
        
        // 检查当前版本
        var version = await GetVersionInternalAsync(conn, ct);
        
        if (version == 0)
        {
            // 全新数据库，先应用 v1 Schema
            _logger.LogInformation("Creating new database schema v{Version}", CurrentVersion);
            await ApplySchemaV1Async(conn, ct);
            version = 1; // 标记为 v1，继续执行后续迁移
        }
        
        if (version < CurrentVersion)
        {
            // 执行后续迁移
            _logger.LogInformation("Migrating database from v{From} to v{To}", version, CurrentVersion);
            await MigrateAsync(conn, version, ct);
        }
        else
        {
            _logger.LogInformation("Database schema is up to date (v{Version})", version);
        }
    }
    
    public async Task<int> GetVersionAsync(CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        return await GetVersionInternalAsync(conn, ct);
    }
    
    private async Task<int> GetVersionInternalAsync(SqliteConnection conn, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT MAX(version) FROM schema_version";
        
        try
        {
            var result = await cmd.ExecuteScalarAsync(ct);
            return result == DBNull.Value || result == null ? 0 : Convert.ToInt32(result);
        }
        catch
        {
            return 0;
        }
    }
    
    private async Task ApplySchemaV1Async(SqliteConnection conn, CancellationToken ct)
    {
        using var transaction = conn.BeginTransaction();
        
        try
        {
            // 创建所有表
            await ExecuteAsync(conn, SchemaV1.CreateDeviceTable, ct, transaction);
            await ExecuteAsync(conn, SchemaV1.CreateTagTable, ct, transaction);
            await ExecuteAsync(conn, SchemaV1.CreateTelemetryTable, ct, transaction);
            await ExecuteAsync(conn, SchemaV1.CreateAlarmTable, ct, transaction);
            await ExecuteAsync(conn, SchemaV1.CreateAlarmAckTable, ct, transaction);
            await ExecuteAsync(conn, SchemaV1.CreateHealthSnapshotTable, ct, transaction);
            await ExecuteAsync(conn, SchemaV1.CreateSystemSettingTable, ct, transaction);
            await ExecuteAsync(conn, SchemaV1.CreateAuditLogTable, ct, transaction);
            await ExecuteAsync(conn, SchemaV1.CreateAuditLogIndexes, ct, transaction);
            await ExecuteAsync(conn, SchemaV1.CreateAlarmRuleTable, ct, transaction);
            await ExecuteAsync(conn, SchemaV1.CreateAlarmRuleIndexes, ct, transaction);
            await ExecuteAsync(conn, SchemaV1.CreateMqttOutboxTable, ct, transaction);
            await ExecuteAsync(conn, SchemaV1.CreateApiKeyTable, ct, transaction);
            
            // 创建索引
            await ExecuteAsync(conn, SchemaV1.CreateIndexes, ct, transaction);
            
            // 记录版本
            await ExecuteAsync(conn, 
                $"INSERT INTO schema_version (version, applied_utc) VALUES (1, {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()})", 
                ct, transaction);
            
            await transaction.CommitAsync(ct);
            _logger.LogInformation("Schema v1 applied successfully");
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }
    
    private async Task MigrateAsync(SqliteConnection conn, int fromVersion, CancellationToken ct)
    {
        if (fromVersion < 2)
        {
            _logger.LogInformation("Applying migration: v1 -> v2 (device connection fields)");
            await ApplyMigrationV2Async(conn, ct);
        }
        
        if (fromVersion < 3)
        {
            _logger.LogInformation("Applying migration: v2 -> v3 (alarm dedup index)");
            await ApplyMigrationV3Async(conn, ct);
        }
        
        if (fromVersion < 4)
        {
            _logger.LogInformation("Applying migration: v3 -> v4 (user table)");
            await ApplyMigrationV4Async(conn, ct);
        }
        
        if (fromVersion < 5)
        {
            _logger.LogInformation("Applying migration: v4 -> v5 (refresh token)");
            await ApplyMigrationV5Async(conn, ct);
        }
        
        if (fromVersion < 6)
        {
            _logger.LogInformation("Applying migration: v5 -> v6 (health baseline table)");
            await ApplyMigrationV6Async(conn, ct);
        }
        
        if (fromVersion < 7)
        {
            _logger.LogInformation("Applying migration: v6 -> v7 (collection rule tables)");
            await ApplyMigrationV7Async(conn, ct);
        }
        
        if (fromVersion < 8)
        {
            _logger.LogInformation("Applying migration: v7 -> v8 (cycle analysis tables)");
            await ApplyMigrationV8Async(conn, ct);
        }
        
        if (fromVersion < 9)
        {
            _logger.LogInformation("Applying migration: v8 -> v9 (account lockout fields)");
            await ApplyMigrationV9Async(conn, ct);
        }
        
        if (fromVersion < 10)
        {
            _logger.LogInformation("Applying migration: v9 -> v10 (telemetry aggregation tables)");
            await ApplyMigrationV10Async(conn, ct);
        }

        if (fromVersion < 11)
        {
            _logger.LogInformation("Applying migration: v10 -> v11 (performance indexes)");
            await ApplyMigrationV11Async(conn, ct);
        }
        // 未来版本迁移逻辑继续在这里添加
    }
    
    private async Task ApplyMigrationV2Async(SqliteConnection conn, CancellationToken ct)
    {
        // 添加新列（SQLite ALTER TABLE 只支持 ADD COLUMN）
        var migrations = new[]
        {
            "ALTER TABLE device ADD COLUMN host TEXT;",
            "ALTER TABLE device ADD COLUMN port INTEGER;",
            "ALTER TABLE device ADD COLUMN connection_string TEXT;"
        };
        
        foreach (var sql in migrations)
        {
            try
            {
                await ExecuteAsync(conn, sql, ct);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1) // duplicate column name
            {
                _logger.LogDebug("Column already exists, skipping: {Sql}", sql);
            }
        }
        
        // 更新版本
        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn, 
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (2, {appliedUtc})", 
            ct);
        
        _logger.LogInformation("Migration v1 -> v2 completed");
    }
    
    private async Task ApplyMigrationV3Async(SqliteConnection conn, CancellationToken ct)
    {
        // Partial Index：只索引未关闭的告警 (status <> 2)
        // 比普通索引更小、查询更快
        // 优化查询: SELECT COUNT(*) FROM alarm WHERE code = @Code AND status <> 2
        const string createIndex = @"
CREATE INDEX IF NOT EXISTS idx_alarm_open_by_code
ON alarm(code)
WHERE status <> 2;
";
        
        await ExecuteAsync(conn, createIndex, ct);
        
        // 更新版本
        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn, 
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (3, {appliedUtc})", 
            ct);
        
        _logger.LogInformation("Migration v2 -> v3 completed (alarm dedup partial index added)");
    }
    
    private async Task ApplyMigrationV4Async(SqliteConnection conn, CancellationToken ct)
    {
        const string createTable = @"
CREATE TABLE IF NOT EXISTS user (
    user_id TEXT PRIMARY KEY,
    username TEXT NOT NULL UNIQUE COLLATE NOCASE,
    password_hash TEXT NOT NULL,
    display_name TEXT,
    role TEXT NOT NULL DEFAULT 'Viewer',
    enabled INTEGER NOT NULL DEFAULT 1,
    created_utc INTEGER NOT NULL,
    last_login_utc INTEGER
);

CREATE INDEX IF NOT EXISTS idx_user_username ON user(username);
";

        await ExecuteAsync(conn, createTable, ct);

        // 默认管理员 admin/admin123（password_hash 为 SHA256+Base64）
        const string defaultAdmin = @"
INSERT OR IGNORE INTO user (user_id, username, password_hash, display_name, role, enabled, created_utc)
VALUES ('admin0000000001', 'admin', 'JAvlGPq9JyTdtvBO6x2llnRI1+gxwIyPqCKAn3THIKk=', 'Administrator', 'Admin', 1, 0);
";

        await ExecuteAsync(conn, defaultAdmin, ct);

        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn,
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (4, {appliedUtc});",
            ct);

        _logger.LogInformation("Migration v3 -> v4 completed (user table added)");
    }
    
    private async Task ApplyMigrationV5Async(SqliteConnection conn, CancellationToken ct)
    {
        // 添加 refresh_token 字段
        var migrations = new[]
        {
            "ALTER TABLE user ADD COLUMN refresh_token TEXT;",
            "ALTER TABLE user ADD COLUMN refresh_token_expires_utc INTEGER;"
        };
        
        foreach (var sql in migrations)
        {
            try
            {
                await ExecuteAsync(conn, sql, ct);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1) // duplicate column name
            {
                _logger.LogDebug("Column already exists, skipping: {Sql}", sql);
            }
        }

        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn,
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (5, {appliedUtc});",
            ct);

        _logger.LogInformation("Migration v4 -> v5 completed (refresh token fields added)");
    }
    
    private async Task ApplyMigrationV6Async(SqliteConnection conn, CancellationToken ct)
    {
        // v45: 创建健康基线表
        const string createTable = @"
CREATE TABLE IF NOT EXISTS health_baseline (
    device_id TEXT PRIMARY KEY,
    created_utc INTEGER NOT NULL,
    updated_utc INTEGER NOT NULL,
    sample_count INTEGER NOT NULL DEFAULT 0,
    learning_hours INTEGER NOT NULL DEFAULT 0,
    tag_baselines_json TEXT NOT NULL DEFAULT '{}'
);
";
        
        await ExecuteAsync(conn, createTable, ct);

        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn,
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (6, {appliedUtc});",
            ct);

        _logger.LogInformation("Migration v5 -> v6 completed (health baseline table added)");
    }
    
    private async Task ApplyMigrationV7Async(SqliteConnection conn, CancellationToken ct)
    {
        // v46: 创建采集规则表
        const string createRuleTable = @"
CREATE TABLE IF NOT EXISTS collection_rule (
    rule_id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    description TEXT,
    device_id TEXT NOT NULL,
    enabled INTEGER NOT NULL DEFAULT 1,
    start_condition_json TEXT NOT NULL,
    stop_condition_json TEXT NOT NULL,
    collection_config_json TEXT NOT NULL,
    post_actions_json TEXT,
    trigger_count INTEGER NOT NULL DEFAULT 0,
    last_trigger_utc INTEGER,
    created_utc INTEGER NOT NULL,
    updated_utc INTEGER NOT NULL
) WITHOUT ROWID;

CREATE INDEX IF NOT EXISTS idx_collection_rule_device ON collection_rule(device_id);
CREATE INDEX IF NOT EXISTS idx_collection_rule_enabled ON collection_rule(enabled);
";

        // v46: 创建采集片段表
        const string createSegmentTable = @"
CREATE TABLE IF NOT EXISTS collection_segment (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    rule_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    start_time_utc INTEGER NOT NULL,
    end_time_utc INTEGER,
    status INTEGER NOT NULL DEFAULT 0,
    data_point_count INTEGER NOT NULL DEFAULT 0,
    metadata_json TEXT,
    created_utc INTEGER NOT NULL,
    FOREIGN KEY (rule_id) REFERENCES collection_rule(rule_id)
);

CREATE INDEX IF NOT EXISTS idx_segment_rule ON collection_segment(rule_id, start_time_utc DESC);
CREATE INDEX IF NOT EXISTS idx_segment_device ON collection_segment(device_id, start_time_utc DESC);
CREATE INDEX IF NOT EXISTS idx_segment_status ON collection_segment(status);
";

        await ExecuteAsync(conn, createRuleTable, ct);
        await ExecuteAsync(conn, createSegmentTable, ct);

        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn,
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (7, {appliedUtc});",
            ct);

        _logger.LogInformation("Migration v6 -> v7 completed (collection rule tables added)");
    }
    
    private async Task ApplyMigrationV8Async(SqliteConnection conn, CancellationToken ct)
    {
        // v47: 创建工作周期表
        const string createCycleTable = @"
CREATE TABLE IF NOT EXISTS work_cycle (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    device_id TEXT NOT NULL,
    segment_id INTEGER,
    start_time_utc INTEGER NOT NULL,
    end_time_utc INTEGER NOT NULL,
    duration_seconds REAL NOT NULL,
    max_angle REAL NOT NULL DEFAULT 0,
    motor1_peak_current REAL NOT NULL DEFAULT 0,
    motor2_peak_current REAL NOT NULL DEFAULT 0,
    motor1_avg_current REAL NOT NULL DEFAULT 0,
    motor2_avg_current REAL NOT NULL DEFAULT 0,
    motor1_energy REAL NOT NULL DEFAULT 0,
    motor2_energy REAL NOT NULL DEFAULT 0,
    motor_balance_ratio REAL NOT NULL DEFAULT 1,
    baseline_deviation_percent REAL NOT NULL DEFAULT 0,
    anomaly_score REAL NOT NULL DEFAULT 0,
    is_anomaly INTEGER NOT NULL DEFAULT 0,
    anomaly_type TEXT,
    details_json TEXT,
    created_utc INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_cycle_device_time ON work_cycle(device_id, start_time_utc DESC);
CREATE INDEX IF NOT EXISTS idx_cycle_anomaly ON work_cycle(device_id, is_anomaly, start_time_utc DESC);
CREATE INDEX IF NOT EXISTS idx_cycle_segment ON work_cycle(segment_id);
";

        // v47: 创建设备基线表
        const string createBaselineTable = @"
CREATE TABLE IF NOT EXISTS device_baseline (
    device_id TEXT NOT NULL,
    baseline_type TEXT NOT NULL,
    sample_count INTEGER NOT NULL DEFAULT 0,
    updated_utc INTEGER NOT NULL,
    model_json TEXT NOT NULL,
    stats_json TEXT,
    PRIMARY KEY (device_id, baseline_type)
) WITHOUT ROWID;
";

        await ExecuteAsync(conn, createCycleTable, ct);
        await ExecuteAsync(conn, createBaselineTable, ct);

        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn,
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (8, {appliedUtc});",
            ct);

        _logger.LogInformation("Migration v7 -> v8 completed (cycle analysis tables added)");
    }
    
    private async Task ApplyMigrationV9Async(SqliteConnection conn, CancellationToken ct)
    {
        // v48: 添加账号锁定字段
        var migrations = new[]
        {
            "ALTER TABLE user ADD COLUMN failed_login_count INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE user ADD COLUMN lockout_until_utc INTEGER;"
        };
        
        foreach (var sql in migrations)
        {
            try
            {
                await ExecuteAsync(conn, sql, ct);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1) // duplicate column name
            {
                _logger.LogDebug("Column already exists, skipping: {Sql}", sql);
            }
        }
        
        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn,
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (9, {appliedUtc});",
            ct);

        _logger.LogInformation("Migration v8 -> v9 completed (account lockout fields added)");
    }
    
    private async Task ApplyMigrationV10Async(SqliteConnection conn, CancellationToken ct)
    {
        // v56: 创建遥测数据聚合表
        const string createTelemetry1m = @"
            CREATE TABLE IF NOT EXISTS telemetry_1m (
                device_id TEXT NOT NULL,
                tag_id TEXT NOT NULL,
                ts_bucket INTEGER NOT NULL,
                min_value REAL,
                max_value REAL,
                avg_value REAL,
                first_value REAL,
                last_value REAL,
                count INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (device_id, tag_id, ts_bucket)
            ) WITHOUT ROWID;
            
            CREATE INDEX IF NOT EXISTS idx_tel1m_device_ts 
            ON telemetry_1m(device_id, ts_bucket DESC);
        ";
        
        const string createTelemetry1h = @"
            CREATE TABLE IF NOT EXISTS telemetry_1h (
                device_id TEXT NOT NULL,
                tag_id TEXT NOT NULL,
                ts_bucket INTEGER NOT NULL,
                min_value REAL,
                max_value REAL,
                avg_value REAL,
                first_value REAL,
                last_value REAL,
                count INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (device_id, tag_id, ts_bucket)
            ) WITHOUT ROWID;
            
            CREATE INDEX IF NOT EXISTS idx_tel1h_device_ts 
            ON telemetry_1h(device_id, ts_bucket DESC);
        ";
        
        // 创建聚合状态跟踪表
        const string createAggregateState = @"
            CREATE TABLE IF NOT EXISTS aggregate_state (
                table_name TEXT PRIMARY KEY,
                last_processed_ts INTEGER NOT NULL DEFAULT 0
            ) WITHOUT ROWID;
            
            INSERT OR IGNORE INTO aggregate_state (table_name, last_processed_ts) VALUES ('telemetry_1m', 0);
            INSERT OR IGNORE INTO aggregate_state (table_name, last_processed_ts) VALUES ('telemetry_1h', 0);
        ";
        
        await ExecuteAsync(conn, createTelemetry1m, ct);
        await ExecuteAsync(conn, createTelemetry1h, ct);
        await ExecuteAsync(conn, createAggregateState, ct);
        
        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn,
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (10, {appliedUtc});",
            ct);

        _logger.LogInformation("Migration v9 -> v10 completed (telemetry aggregation tables created)");
    }

    private async Task ApplyMigrationV11Async(SqliteConnection conn, CancellationToken ct)
    {
        // v56.1: 性能优化索引
        // 这些索引可以显著提升高频查询的性能

        var indexes = new[]
        {
            // Tag 表 - 启用标签的部分索引
            @"CREATE INDEX IF NOT EXISTS idx_tag_device_enabled
              ON tag(device_id) WHERE enabled = 1;",

            // Alarm 表 - 按设备和状态查询优化
            @"CREATE INDEX IF NOT EXISTS idx_alarm_device_status_ts
              ON alarm(device_id, status, ts DESC);",

            // Alarm 表 - 按严重级别查询优化
            @"CREATE INDEX IF NOT EXISTS idx_alarm_severity_status
              ON alarm(severity, status);",

            // AlarmRule 表 - 按标签查询启用规则（高频查询）
            @"CREATE INDEX IF NOT EXISTS idx_alarm_rule_tag_enabled
              ON alarm_rule(tag_id) WHERE enabled = 1;",

            // AuditLog 表 - 按资源类型和ID查询
            @"CREATE INDEX IF NOT EXISTS idx_audit_log_entity
              ON audit_log(resource_type, resource_id);",

            // User 表 - 启用用户索引
            @"CREATE INDEX IF NOT EXISTS idx_user_enabled
              ON user(enabled) WHERE enabled = 1;",

            // Telemetry 表 - 设备最新数据查询优化（覆盖索引）
            @"CREATE INDEX IF NOT EXISTS idx_tel_device_tag_ts_desc
              ON telemetry(device_id, tag_id, ts DESC);",
        };

        foreach (var indexSql in indexes)
        {
            try
            {
                await ExecuteAsync(conn, indexSql, ct);
            }
            catch (SqliteException ex)
            {
                // 索引可能已存在，忽略
                _logger.LogDebug("Index creation skipped or failed: {Message}", ex.Message);
            }
        }

        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn,
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (11, {appliedUtc});",
            ct);

        _logger.LogInformation("Migration v10 -> v11 completed (performance indexes added)");
    }

    private static async Task ExecuteAsync(SqliteConnection conn, string sql, CancellationToken ct, SqliteTransaction? transaction = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = transaction;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>
/// Schema V1 定义
/// </summary>
public static class SchemaV1
{
    public const string CreateVersionTable = @"
        CREATE TABLE IF NOT EXISTS schema_version (
            version INTEGER NOT NULL,
            applied_utc INTEGER NOT NULL,
            PRIMARY KEY (version)
        ) WITHOUT ROWID;
    ";
    
    public const string CreateDeviceTable = @"
        CREATE TABLE IF NOT EXISTS device (
            device_id TEXT PRIMARY KEY,
            name TEXT,
            location TEXT,
            model TEXT,
            protocol TEXT,
            host TEXT,
            port INTEGER,
            connection_string TEXT,
            enabled INTEGER NOT NULL DEFAULT 1,
            metadata TEXT,
            created_utc INTEGER NOT NULL,
            updated_utc INTEGER NOT NULL
        );
    ";
    
    public const string CreateTagTable = @"
        CREATE TABLE IF NOT EXISTS tag (
            tag_id TEXT PRIMARY KEY,
            device_id TEXT NOT NULL,
            name TEXT,
            description TEXT,
            unit TEXT,
            data_type INTEGER NOT NULL,
            enabled INTEGER NOT NULL DEFAULT 1,
            address TEXT,
            scan_interval_ms INTEGER,
            tag_group TEXT,
            metadata TEXT,
            created_utc INTEGER NOT NULL,
            updated_utc INTEGER NOT NULL,
            FOREIGN KEY(device_id) REFERENCES device(device_id)
        );
    ";
    
    public const string CreateTelemetryTable = @"
        CREATE TABLE IF NOT EXISTS telemetry (
            device_id TEXT NOT NULL,
            tag_id TEXT NOT NULL,
            ts INTEGER NOT NULL,
            seq INTEGER NOT NULL,
            value_type INTEGER NOT NULL,
            
            bool_value INTEGER,
            int8_value INTEGER,
            uint8_value INTEGER,
            int16_value INTEGER,
            uint16_value INTEGER,
            int32_value INTEGER,
            uint32_value INTEGER,
            int64_value INTEGER,
            uint64_value INTEGER,
            float32_value REAL,
            float64_value REAL,
            string_value TEXT,
            byte_array_value BLOB,
            
            quality INTEGER NOT NULL,
            unit TEXT,
            source TEXT NOT NULL,
            protocol TEXT,
            
            PRIMARY KEY (device_id, tag_id, ts, seq)
        ) WITHOUT ROWID;
    ";
    
    public const string CreateAlarmTable = @"
        CREATE TABLE IF NOT EXISTS alarm (
            alarm_id TEXT PRIMARY KEY,
            device_id TEXT NOT NULL,
            tag_id TEXT,
            ts INTEGER NOT NULL,
            severity INTEGER NOT NULL,
            code TEXT NOT NULL,
            message TEXT NOT NULL,
            status INTEGER NOT NULL DEFAULT 0,
            created_utc INTEGER NOT NULL,
            updated_utc INTEGER NOT NULL
        );
    ";
    
    public const string CreateAlarmAckTable = @"
        CREATE TABLE IF NOT EXISTS alarm_ack (
            alarm_id TEXT NOT NULL,
            acked_by TEXT NOT NULL,
            ack_note TEXT,
            acked_utc INTEGER NOT NULL,
            PRIMARY KEY (alarm_id),
            FOREIGN KEY (alarm_id) REFERENCES alarm(alarm_id)
        ) WITHOUT ROWID;
    ";
    
    public const string CreateHealthSnapshotTable = @"
        CREATE TABLE IF NOT EXISTS health_snapshot (
            ts_utc INTEGER NOT NULL,
            overall_state INTEGER NOT NULL,
            database_state INTEGER NOT NULL,
            queue_state INTEGER NOT NULL,
            queue_depth INTEGER NOT NULL,
            dropped_points INTEGER NOT NULL,
            write_p95_ms REAL NOT NULL,
            mqtt_connected INTEGER NOT NULL,
            outbox_depth INTEGER NOT NULL,
            memory_used_mb INTEGER NOT NULL,
            collectors_json TEXT,
            PRIMARY KEY (ts_utc)
        ) WITHOUT ROWID;
    ";
    
    public const string CreateSystemSettingTable = @"
        CREATE TABLE IF NOT EXISTS system_setting (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL,
            updated_utc INTEGER NOT NULL
        ) WITHOUT ROWID;
    ";
    
    public const string CreateAuditLogTable = @"
        CREATE TABLE IF NOT EXISTS audit_log (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            ts INTEGER NOT NULL,
            user_id TEXT NOT NULL,
            user_name TEXT NOT NULL,
            action TEXT NOT NULL,
            resource_type TEXT NOT NULL,
            resource_id TEXT,
            details TEXT,
            ip_address TEXT
        );
    ";
    
    public const string CreateAuditLogIndexes = @"
        CREATE INDEX IF NOT EXISTS idx_audit_log_ts ON audit_log(ts DESC);
        CREATE INDEX IF NOT EXISTS idx_audit_log_action ON audit_log(action);
        CREATE INDEX IF NOT EXISTS idx_audit_log_resource ON audit_log(resource_type, resource_id);
    ";
    
    public const string CreateAlarmRuleTable = @"
        CREATE TABLE IF NOT EXISTS alarm_rule (
            rule_id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            description TEXT,
            tag_id TEXT NOT NULL,
            device_id TEXT,
            condition_type TEXT NOT NULL,
            threshold REAL NOT NULL,
            duration_ms INTEGER NOT NULL DEFAULT 0,
            severity INTEGER NOT NULL DEFAULT 3,
            message_template TEXT,
            enabled INTEGER NOT NULL DEFAULT 1,
            created_utc INTEGER NOT NULL,
            updated_utc INTEGER NOT NULL
        ) WITHOUT ROWID;
    ";
    
    public const string CreateAlarmRuleIndexes = @"
        CREATE INDEX IF NOT EXISTS idx_alarm_rule_tag ON alarm_rule(tag_id);
        CREATE INDEX IF NOT EXISTS idx_alarm_rule_enabled ON alarm_rule(enabled);
    ";
    
    public const string CreateMqttOutboxTable = @"
        CREATE TABLE IF NOT EXISTS mqtt_outbox (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            topic TEXT NOT NULL,
            payload BLOB NOT NULL,
            qos INTEGER NOT NULL DEFAULT 0,
            created_utc INTEGER NOT NULL,
            retry_count INTEGER NOT NULL DEFAULT 0,
            last_retry_utc INTEGER,
            status INTEGER NOT NULL DEFAULT 0,
            error_message TEXT
        );
    ";
    
    public const string CreateApiKeyTable = @"
        CREATE TABLE IF NOT EXISTS api_key (
            key_id TEXT PRIMARY KEY,
            key_hash TEXT NOT NULL,
            name TEXT,
            created_utc INTEGER NOT NULL,
            revoked_utc INTEGER
        );
    ";
    
    public const string CreateIndexes = @"
        -- Tag 索引
        CREATE INDEX IF NOT EXISTS idx_tag_device ON tag(device_id);
        CREATE INDEX IF NOT EXISTS idx_tag_group ON tag(device_id, tag_group);
        
        -- Telemetry 索引（核心性能）
        CREATE INDEX IF NOT EXISTS idx_tel_device_tag_ts ON telemetry(device_id, tag_id, ts, seq);
        CREATE INDEX IF NOT EXISTS idx_tel_device_ts ON telemetry(device_id, ts, seq);
        CREATE INDEX IF NOT EXISTS idx_tel_bad_quality ON telemetry(device_id, tag_id, ts) WHERE quality <> 192;
        
        -- Alarm 索引
        CREATE INDEX IF NOT EXISTS idx_alarm_device_ts ON alarm(device_id, ts DESC);
        CREATE INDEX IF NOT EXISTS idx_alarm_status ON alarm(status, ts DESC);
        
        -- MQTT Outbox 索引
        CREATE INDEX IF NOT EXISTS idx_outbox_status_created ON mqtt_outbox(status, created_utc);
        
        -- Health Snapshot 索引
        CREATE INDEX IF NOT EXISTS idx_health_ts ON health_snapshot(ts_utc DESC);
    ";
}
