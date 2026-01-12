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
    
    private const int CurrentVersion = 21;  // v65: Edge 配置管理
    
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

        if (fromVersion < 12)
        {
            _logger.LogInformation("Applying migration: v11 -> v12 (advanced alarm types)");
            await ApplyMigrationV12Async(conn, ct);
        }

        if (fromVersion < 13)
        {
            _logger.LogInformation("Applying migration: v12 -> v13 (alarm aggregation)");
            await ApplyMigrationV13Async(conn, ct);
        }

        if (fromVersion < 14)
        {
            _logger.LogInformation("Applying migration: v13 -> v14 (device health snapshot)");
            await ApplyMigrationV14Async(conn, ct);
        }

        if (fromVersion < 15)
        {
            _logger.LogInformation("Applying migration: v14 -> v15 (database optimization)");
            await ApplyMigrationV15Async(conn, ct);
        }

        if (fromVersion < 16)
        {
            _logger.LogInformation("Applying migration: v15 -> v16 (must change password)");
            await ApplyMigrationV16Async(conn, ct);
        }

        if (fromVersion < 17)
        {
            _logger.LogInformation("Applying migration: v16 -> v17 (tag importance config)");
            await ApplyMigrationV17Async(conn, ct);
        }

        if (fromVersion < 18)
        {
            _logger.LogInformation("Applying migration: v17 -> v18 (tag correlation rules)");
            await ApplyMigrationV18Async(conn, ct);
        }

        if (fromVersion < 19)
        {
            _logger.LogInformation("Applying migration: v18 -> v19 (schema sync with TimescaleDB)");
            await ApplyMigrationV19Async(conn, ct);
        }

        if (fromVersion < 20)
        {
            _logger.LogInformation("Applying migration: v19 -> v20 (motor fault prediction)");
            await ApplyMigrationV20Async(conn, ct);
        }

        if (fromVersion < 21)
        {
            _logger.LogInformation("Applying migration: v20 -> v21 (edge config management)");
            await ApplyMigrationV21Async(conn, ct);
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

    private async Task ApplyMigrationV12Async(SqliteConnection conn, CancellationToken ct)
    {
        // v56: 高级告警类型支持（离线检测 + 变化率告警）

        // 1. 添加 alarm_rule 新字段
        var alterTableSqls = new[]
        {
            "ALTER TABLE alarm_rule ADD COLUMN roc_window_ms INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE alarm_rule ADD COLUMN rule_type TEXT NOT NULL DEFAULT 'threshold';"
        };

        foreach (var sql in alterTableSqls)
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

        // 2. 创建规则类型索引
        const string createRuleTypeIndex = @"
            CREATE INDEX IF NOT EXISTS idx_alarm_rule_type ON alarm_rule(rule_type);
        ";
        await ExecuteAsync(conn, createRuleTypeIndex, ct);

        // 3. 创建离线检测用的标签最后数据时间表
        const string createTagLastDataTable = @"
            CREATE TABLE IF NOT EXISTS tag_last_data (
                tag_id TEXT NOT NULL,
                device_id TEXT NOT NULL,
                last_ts INTEGER NOT NULL,
                updated_utc INTEGER NOT NULL,
                PRIMARY KEY (device_id, tag_id)
            ) WITHOUT ROWID;

            CREATE INDEX IF NOT EXISTS idx_tag_last_data_ts ON tag_last_data(last_ts);
        ";
        await ExecuteAsync(conn, createTagLastDataTable, ct);

        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn,
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (12, {appliedUtc});",
            ct);

        _logger.LogInformation("Migration v11 -> v12 completed (advanced alarm types: offline + roc)");
    }

    private async Task ApplyMigrationV13Async(SqliteConnection conn, CancellationToken ct)
    {
        // v59: 告警聚合支持

        // 1. 创建告警聚合组表
        const string createAlarmGroupTable = @"
            CREATE TABLE IF NOT EXISTS alarm_group (
                group_id TEXT PRIMARY KEY,
                device_id TEXT NOT NULL,
                tag_id TEXT,
                rule_id TEXT NOT NULL,
                severity INTEGER NOT NULL,
                code TEXT,
                message TEXT,
                alarm_count INTEGER NOT NULL DEFAULT 1,
                first_occurred_utc INTEGER NOT NULL,
                last_occurred_utc INTEGER NOT NULL,
                aggregate_status INTEGER NOT NULL DEFAULT 0,
                created_utc INTEGER NOT NULL,
                updated_utc INTEGER NOT NULL
            ) WITHOUT ROWID;
        ";
        await ExecuteAsync(conn, createAlarmGroupTable, ct);

        // 2. 创建告警到聚合组的映射表
        const string createAlarmToGroupTable = @"
            CREATE TABLE IF NOT EXISTS alarm_to_group (
                alarm_id TEXT NOT NULL PRIMARY KEY,
                group_id TEXT NOT NULL,
                added_utc INTEGER NOT NULL,
                FOREIGN KEY (group_id) REFERENCES alarm_group(group_id)
            ) WITHOUT ROWID;
        ";
        await ExecuteAsync(conn, createAlarmToGroupTable, ct);

        // 3. 创建索引
        var indexes = new[]
        {
            // 按设备和状态查询聚合组
            @"CREATE INDEX IF NOT EXISTS idx_alarm_group_device_status
              ON alarm_group(device_id, aggregate_status, last_occurred_utc DESC);",

            // 按规则查询聚合组
            @"CREATE INDEX IF NOT EXISTS idx_alarm_group_rule
              ON alarm_group(rule_id, aggregate_status);",

            // 查找设备+规则的活跃聚合组
            @"CREATE INDEX IF NOT EXISTS idx_alarm_group_device_rule_active
              ON alarm_group(device_id, rule_id, aggregate_status)
              WHERE aggregate_status <> 2;",

            // 按聚合组查找子告警
            @"CREATE INDEX IF NOT EXISTS idx_alarm_to_group_group
              ON alarm_to_group(group_id);"
        };

        foreach (var indexSql in indexes)
        {
            try
            {
                await ExecuteAsync(conn, indexSql, ct);
            }
            catch (SqliteException ex)
            {
                _logger.LogDebug("Index creation skipped or failed: {Message}", ex.Message);
            }
        }

        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn,
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (13, {appliedUtc});",
            ct);

        _logger.LogInformation("Migration v12 -> v13 completed (alarm aggregation tables created)");
    }

    private async Task ApplyMigrationV14Async(SqliteConnection conn, CancellationToken ct)
    {
        // v60: 设备健康快照表 - 用于存储历史健康评分

        const string createDeviceHealthSnapshotTable = @"
            CREATE TABLE IF NOT EXISTS device_health_snapshot (
                device_id TEXT NOT NULL,
                ts INTEGER NOT NULL,
                index_score INTEGER NOT NULL,
                level INTEGER NOT NULL,
                deviation_score INTEGER NOT NULL,
                trend_score INTEGER NOT NULL,
                stability_score INTEGER NOT NULL,
                alarm_score INTEGER NOT NULL,
                PRIMARY KEY (device_id, ts)
            ) WITHOUT ROWID;
        ";
        await ExecuteAsync(conn, createDeviceHealthSnapshotTable, ct);

        // 创建索引：按设备和时间查询
        const string createIndex = @"
            CREATE INDEX IF NOT EXISTS idx_device_health_snapshot_device_ts
            ON device_health_snapshot(device_id, ts DESC);
        ";
        await ExecuteAsync(conn, createIndex, ct);

        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn,
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (14, {appliedUtc});",
            ct);

        _logger.LogInformation("Migration v13 -> v14 completed (device health snapshot table created)");
    }

    private async Task ApplyMigrationV15Async(SqliteConnection conn, CancellationToken ct)
    {
        // v56.2: 数据库优化和迁移准备

        // 1. 创建数据库统计表（用于监控和性能分析）
        const string createDbStatsTable = @"
            CREATE TABLE IF NOT EXISTS db_stats (
                stat_time INTEGER PRIMARY KEY,
                telemetry_count INTEGER NOT NULL DEFAULT 0,
                telemetry_1m_count INTEGER NOT NULL DEFAULT 0,
                telemetry_1h_count INTEGER NOT NULL DEFAULT 0,
                alarm_count INTEGER NOT NULL DEFAULT 0,
                db_size_bytes INTEGER NOT NULL DEFAULT 0,
                writes_per_minute INTEGER NOT NULL DEFAULT 0
            ) WITHOUT ROWID;
        ";
        await ExecuteAsync(conn, createDbStatsTable, ct);

        // 2. 创建迁移准备表（记录迁移状态）
        const string createMigrationStateTable = @"
            CREATE TABLE IF NOT EXISTS migration_state (
                migration_id TEXT PRIMARY KEY,
                source_db TEXT NOT NULL,
                target_db TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'pending',
                started_utc INTEGER,
                completed_utc INTEGER,
                rows_migrated INTEGER NOT NULL DEFAULT 0,
                error_message TEXT
            ) WITHOUT ROWID;
        ";
        await ExecuteAsync(conn, createMigrationStateTable, ct);

        // 3. 优化告警表索引 - 添加复合索引用于仪表盘查询
        var optimizationIndexes = new[]
        {
            // 仪表盘：按严重级别统计未关闭告警
            @"CREATE INDEX IF NOT EXISTS idx_alarm_severity_open
              ON alarm(severity, created_utc DESC)
              WHERE status <> 2;",

            // 告警聚合组：快速查找活跃组
            @"CREATE INDEX IF NOT EXISTS idx_alarm_group_active_last
              ON alarm_group(aggregate_status, last_occurred_utc DESC)
              WHERE aggregate_status <> 2;",

            // 遥测 1m 聚合：设备+标签组合查询
            @"CREATE INDEX IF NOT EXISTS idx_tel1m_device_tag_ts
              ON telemetry_1m(device_id, tag_id, ts_bucket DESC);",

            // 遥测 1h 聚合：设备+标签组合查询
            @"CREATE INDEX IF NOT EXISTS idx_tel1h_device_tag_ts
              ON telemetry_1h(device_id, tag_id, ts_bucket DESC);"
        };

        foreach (var indexSql in optimizationIndexes)
        {
            try
            {
                await ExecuteAsync(conn, indexSql, ct);
            }
            catch (SqliteException ex)
            {
                _logger.LogDebug("Index creation skipped: {Message}", ex.Message);
            }
        }

        // 4. 更新统计信息
        await ExecuteAsync(conn, "ANALYZE;", ct);

        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn,
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (15, {appliedUtc});",
            ct);

        _logger.LogInformation("Migration v14 -> v15 completed (database optimization and migration preparation)");
    }

    private async Task ApplyMigrationV16Async(SqliteConnection conn, CancellationToken ct)
    {
        // v56.3: 添加强制修改密码字段
        var migrations = new[]
        {
            "ALTER TABLE user ADD COLUMN must_change_password INTEGER NOT NULL DEFAULT 0;"
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

        // 为默认管理员账户设置强制修改密码标志
        const string updateDefaultAdmin = @"
            UPDATE user
            SET must_change_password = 1
            WHERE user_id = 'admin0000000001'
              AND password_hash = 'JAvlGPq9JyTdtvBO6x2llnRI1+gxwIyPqCKAn3THIKk=';
        ";
        await ExecuteAsync(conn, updateDefaultAdmin, ct);

        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn,
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (16, {appliedUtc});",
            ct);

        _logger.LogInformation("Migration v15 -> v16 completed (must change password field added)");
    }

    private async Task ApplyMigrationV17Async(SqliteConnection conn, CancellationToken ct)
    {
        // v61: 标签重要性配置表 - 用于健康评估加权计算
        const string createTagImportanceTable = @"
            CREATE TABLE IF NOT EXISTS tag_importance_config (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                pattern TEXT NOT NULL,
                importance INTEGER NOT NULL DEFAULT 40,
                description TEXT,
                priority INTEGER NOT NULL DEFAULT 0,
                enabled INTEGER NOT NULL DEFAULT 1,
                created_utc INTEGER NOT NULL,
                updated_utc INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_tag_importance_enabled
            ON tag_importance_config(enabled, priority DESC);
        ";
        await ExecuteAsync(conn, createTagImportanceTable, ct);

        // 插入默认配置规则
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var defaultRules = $@"
            INSERT OR IGNORE INTO tag_importance_config (pattern, importance, description, priority, enabled, created_utc, updated_utc)
            VALUES
            ('*Temperature*', 100, '温度指标 - 关键', 100, 1, {nowUtc}, {nowUtc}),
            ('*_Temp', 100, '温度指标 - 关键', 99, 1, {nowUtc}, {nowUtc}),
            ('*Current*', 100, '电流指标 - 关键', 98, 1, {nowUtc}, {nowUtc}),
            ('*Vibration*', 100, '振动指标 - 关键', 97, 1, {nowUtc}, {nowUtc}),
            ('*Pressure*', 70, '压力指标 - 重要', 80, 1, {nowUtc}, {nowUtc}),
            ('*Speed*', 70, '速度指标 - 重要', 79, 1, {nowUtc}, {nowUtc}),
            ('*Flow*', 70, '流量指标 - 重要', 78, 1, {nowUtc}, {nowUtc}),
            ('*Power*', 70, '功率指标 - 重要', 77, 1, {nowUtc}, {nowUtc}),
            ('*Level*', 40, '液位指标 - 次要', 60, 1, {nowUtc}, {nowUtc}),
            ('*Humidity*', 40, '湿度指标 - 次要', 59, 1, {nowUtc}, {nowUtc}),
            ('*Ambient*', 20, '环境指标 - 辅助', 40, 1, {nowUtc}, {nowUtc}),
            ('*Status*', 20, '状态指标 - 辅助', 39, 1, {nowUtc}, {nowUtc});
        ";

        try
        {
            await ExecuteAsync(conn, defaultRules, ct);
        }
        catch (SqliteException ex)
        {
            _logger.LogDebug("Default rules insert skipped: {Message}", ex.Message);
        }

        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn,
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (17, {appliedUtc});",
            ct);

        _logger.LogInformation("Migration v16 -> v17 completed (tag importance config table created)");
    }

    private async Task ApplyMigrationV18Async(SqliteConnection conn, CancellationToken ct)
    {
        // v62: 标签关联规则表 - 用于多标签关联分析
        const string createTagCorrelationRuleTable = @"
            CREATE TABLE IF NOT EXISTS tag_correlation_rule (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                device_pattern TEXT,
                tag1_pattern TEXT NOT NULL,
                tag2_pattern TEXT NOT NULL,
                correlation_type INTEGER NOT NULL DEFAULT 0,
                threshold REAL NOT NULL DEFAULT 0.7,
                risk_description TEXT,
                penalty_score INTEGER NOT NULL DEFAULT 15,
                enabled INTEGER NOT NULL DEFAULT 1,
                priority INTEGER NOT NULL DEFAULT 0,
                created_utc INTEGER NOT NULL,
                updated_utc INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_tag_correlation_enabled
            ON tag_correlation_rule(enabled, priority DESC);
        ";
        await ExecuteAsync(conn, createTagCorrelationRuleTable, ct);

        // 插入默认关联规则
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var defaultRules = $@"
            INSERT OR IGNORE INTO tag_correlation_rule (name, device_pattern, tag1_pattern, tag2_pattern, correlation_type, threshold, risk_description, penalty_score, enabled, priority, created_utc, updated_utc)
            VALUES
            ('温度电流同升', '*', '*Temperature*', '*Current*', 0, 0.7, '温度和电流同时升高，可能过载', 20, 1, 100, {nowUtc}, {nowUtc}),
            ('振动温度同升', '*', '*Vibration*', '*Temperature*', 0, 0.7, '振动和温度同时升高，可能轴承故障', 25, 1, 99, {nowUtc}, {nowUtc}),
            ('压力流量反向', '*', '*Pressure*', '*Flow*', 1, 0.6, '压力升高流量降低，可能管路堵塞', 20, 1, 98, {nowUtc}, {nowUtc});
        ";

        try
        {
            await ExecuteAsync(conn, defaultRules, ct);
        }
        catch (SqliteException ex)
        {
            _logger.LogDebug("Default correlation rules insert skipped: {Message}", ex.Message);
        }

        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn,
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (18, {appliedUtc});",
            ct);

        _logger.LogInformation("Migration v17 -> v18 completed (tag correlation rule table created)");
    }

    private async Task ApplyMigrationV19Async(SqliteConnection conn, CancellationToken ct)
    {
        // v63: Schema 同步 - 与 TimescaleDB 对齐，确保无缝切换

        // 1. device 表补充字段
        var deviceMigrations = new[]
        {
            "ALTER TABLE device ADD COLUMN description TEXT;",
            "ALTER TABLE device ADD COLUMN status TEXT NOT NULL DEFAULT 'Unknown';"
        };

        foreach (var sql in deviceMigrations)
        {
            try
            {
                await ExecuteAsync(conn, sql, ct);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
            {
                _logger.LogDebug("Column already exists, skipping: {Sql}", sql);
            }
        }

        // 2. alarm 表补充 group_id 字段
        try
        {
            await ExecuteAsync(conn, "ALTER TABLE alarm ADD COLUMN group_id TEXT;", ct);
            await ExecuteAsync(conn, "CREATE INDEX IF NOT EXISTS idx_alarm_group ON alarm(group_id) WHERE group_id IS NOT NULL;", ct);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
        {
            _logger.LogDebug("alarm.group_id already exists, skipping");
        }

        // 3. alarm_rule 表补充字段 (与 TimescaleDB 对齐)
        var alarmRuleMigrations = new[]
        {
            "ALTER TABLE alarm_rule ADD COLUMN rule_type INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE alarm_rule ADD COLUMN threshold_high REAL;",
            "ALTER TABLE alarm_rule ADD COLUMN roc_window_ms INTEGER;",
            "ALTER TABLE alarm_rule ADD COLUMN roc_threshold REAL;"
        };

        foreach (var sql in alarmRuleMigrations)
        {
            try
            {
                await ExecuteAsync(conn, sql, ct);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
            {
                _logger.LogDebug("Column already exists, skipping: {Sql}", sql);
            }
        }

        // 4. api_key 表补充字段 (增强版)
        var apiKeyMigrations = new[]
        {
            "ALTER TABLE api_key ADD COLUMN role TEXT NOT NULL DEFAULT 'Viewer';",
            "ALTER TABLE api_key ADD COLUMN expires_utc INTEGER;",
            "ALTER TABLE api_key ADD COLUMN last_used_utc INTEGER;",
            "ALTER TABLE api_key ADD COLUMN allowed_ips TEXT;",
            "ALTER TABLE api_key ADD COLUMN rate_limit INTEGER DEFAULT 1000;"
        };

        foreach (var sql in apiKeyMigrations)
        {
            try
            {
                await ExecuteAsync(conn, sql, ct);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
            {
                _logger.LogDebug("Column already exists, skipping: {Sql}", sql);
            }
        }

        // 5. 创建 api_key 活跃索引
        try
        {
            await ExecuteAsync(conn, "CREATE INDEX IF NOT EXISTS idx_api_key_active ON api_key(key_id) WHERE revoked_utc IS NULL;", ct);
        }
        catch (SqliteException ex)
        {
            _logger.LogDebug("Index creation skipped: {Message}", ex.Message);
        }

        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn,
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (19, {appliedUtc});",
            ct);

        _logger.LogInformation("Migration v18 -> v19 completed (schema synchronized with TimescaleDB)");
    }

    private async Task ApplyMigrationV20Async(SqliteConnection conn, CancellationToken ct)
    {
        // v64: 电机故障预测模块 - 创建电机相关表

        // 1. 电机模型表
        const string createMotorModel = @"
            CREATE TABLE IF NOT EXISTS motor_model (
                model_id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                description TEXT,
                motor_type INTEGER NOT NULL DEFAULT 0,
                rated_power REAL,
                rated_voltage REAL,
                rated_current REAL,
                rated_speed REAL,
                rated_frequency REAL,
                pole_pairs INTEGER,
                vfd_model TEXT,
                bearing_model TEXT,
                bearing_rolling_elements INTEGER,
                bearing_ball_diameter REAL,
                bearing_pitch_diameter REAL,
                bearing_contact_angle REAL,
                created_utc INTEGER NOT NULL,
                updated_utc INTEGER,
                created_by TEXT
            );
        ";
        await ExecuteAsync(conn, createMotorModel, ct);

        // 2. 电机实例表
        const string createMotorInstance = @"
            CREATE TABLE IF NOT EXISTS motor_instance (
                instance_id TEXT PRIMARY KEY,
                model_id TEXT NOT NULL,
                device_id TEXT NOT NULL,
                name TEXT NOT NULL,
                location TEXT,
                install_date TEXT,
                operating_hours REAL,
                asset_number TEXT,
                diagnosis_enabled INTEGER NOT NULL DEFAULT 1,
                created_utc INTEGER NOT NULL,
                updated_utc INTEGER,
                FOREIGN KEY (model_id) REFERENCES motor_model(model_id) ON DELETE CASCADE,
                FOREIGN KEY (device_id) REFERENCES device(device_id) ON DELETE CASCADE
            );
        ";
        await ExecuteAsync(conn, createMotorInstance, ct);

        // 3. 电机实例索引
        await ExecuteAsync(conn, "CREATE INDEX IF NOT EXISTS idx_motor_instance_model ON motor_instance(model_id);", ct);
        await ExecuteAsync(conn, "CREATE INDEX IF NOT EXISTS idx_motor_instance_device ON motor_instance(device_id);", ct);

        // 4. 参数映射表
        const string createParameterMapping = @"
            CREATE TABLE IF NOT EXISTS motor_parameter_mapping (
                mapping_id TEXT PRIMARY KEY,
                instance_id TEXT NOT NULL,
                parameter INTEGER NOT NULL,
                tag_id TEXT NOT NULL,
                scale_factor REAL NOT NULL DEFAULT 1.0,
                offset REAL NOT NULL DEFAULT 0.0,
                used_for_diagnosis INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (instance_id) REFERENCES motor_instance(instance_id) ON DELETE CASCADE,
                FOREIGN KEY (tag_id) REFERENCES tag(tag_id) ON DELETE CASCADE
            );
        ";
        await ExecuteAsync(conn, createParameterMapping, ct);

        // 5. 参数映射索引
        await ExecuteAsync(conn, "CREATE INDEX IF NOT EXISTS idx_motor_param_mapping_instance ON motor_parameter_mapping(instance_id);", ct);
        await ExecuteAsync(conn, "CREATE UNIQUE INDEX IF NOT EXISTS idx_motor_param_mapping_unique ON motor_parameter_mapping(instance_id, parameter);", ct);

        // 6. 操作模式表
        const string createOperationMode = @"
            CREATE TABLE IF NOT EXISTS operation_mode (
                mode_id TEXT PRIMARY KEY,
                instance_id TEXT NOT NULL,
                name TEXT NOT NULL,
                description TEXT,
                trigger_tag_id TEXT,
                trigger_min_value REAL,
                trigger_max_value REAL,
                min_duration_ms INTEGER NOT NULL DEFAULT 0,
                max_duration_ms INTEGER NOT NULL DEFAULT 0,
                priority INTEGER NOT NULL DEFAULT 0,
                enabled INTEGER NOT NULL DEFAULT 1,
                created_utc INTEGER NOT NULL,
                updated_utc INTEGER,
                FOREIGN KEY (instance_id) REFERENCES motor_instance(instance_id) ON DELETE CASCADE
            );
        ";
        await ExecuteAsync(conn, createOperationMode, ct);

        // 7. 操作模式索引
        await ExecuteAsync(conn, "CREATE INDEX IF NOT EXISTS idx_operation_mode_instance ON operation_mode(instance_id);", ct);
        await ExecuteAsync(conn, "CREATE INDEX IF NOT EXISTS idx_operation_mode_enabled ON operation_mode(instance_id, enabled) WHERE enabled = 1;", ct);

        // 8. 基线配置表
        const string createBaselineProfile = @"
            CREATE TABLE IF NOT EXISTS baseline_profile (
                baseline_id TEXT PRIMARY KEY,
                mode_id TEXT NOT NULL,
                parameter INTEGER NOT NULL,
                mean REAL NOT NULL,
                std_dev REAL NOT NULL,
                min_value REAL NOT NULL,
                max_value REAL NOT NULL,
                percentile_05 REAL,
                percentile_95 REAL,
                median REAL,
                frequency_profile_json TEXT,
                sample_count INTEGER NOT NULL,
                learned_from_utc INTEGER NOT NULL,
                learned_to_utc INTEGER NOT NULL,
                confidence_level REAL,
                version INTEGER NOT NULL DEFAULT 1,
                created_utc INTEGER NOT NULL,
                updated_utc INTEGER,
                FOREIGN KEY (mode_id) REFERENCES operation_mode(mode_id) ON DELETE CASCADE
            );
        ";
        await ExecuteAsync(conn, createBaselineProfile, ct);

        // 9. 基线索引
        await ExecuteAsync(conn, "CREATE INDEX IF NOT EXISTS idx_baseline_mode ON baseline_profile(mode_id);", ct);
        await ExecuteAsync(conn, "CREATE UNIQUE INDEX IF NOT EXISTS idx_baseline_mode_param ON baseline_profile(mode_id, parameter);", ct);

        // 10. 诊断记录表 (用于存储诊断历史)
        const string createDiagnosisRecord = @"
            CREATE TABLE IF NOT EXISTS motor_diagnosis_record (
                record_id TEXT PRIMARY KEY,
                instance_id TEXT NOT NULL,
                mode_id TEXT,
                diagnosis_type INTEGER NOT NULL,
                status INTEGER NOT NULL,
                health_score REAL,
                fault_type INTEGER,
                confidence REAL,
                deviations_json TEXT,
                details_json TEXT,
                created_utc INTEGER NOT NULL,
                FOREIGN KEY (instance_id) REFERENCES motor_instance(instance_id) ON DELETE CASCADE
            );
        ";
        await ExecuteAsync(conn, createDiagnosisRecord, ct);

        // 11. 诊断记录索引
        await ExecuteAsync(conn, "CREATE INDEX IF NOT EXISTS idx_diagnosis_instance_ts ON motor_diagnosis_record(instance_id, created_utc DESC);", ct);
        await ExecuteAsync(conn, "CREATE INDEX IF NOT EXISTS idx_diagnosis_fault_type ON motor_diagnosis_record(instance_id, fault_type) WHERE fault_type IS NOT NULL;", ct);

        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn,
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (20, {appliedUtc});",
            ct);

        _logger.LogInformation("Migration v19 -> v20 completed (motor fault prediction tables created)");
    }

    private async Task ApplyMigrationV21Async(SqliteConnection conn, CancellationToken ct)
    {
        // v65: Edge 配置管理模块

        // 1. Edge 配置表
        const string createEdgeConfig = @"
            CREATE TABLE IF NOT EXISTS edge_config (
                edge_id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                description TEXT,
                processing_enabled INTEGER NOT NULL DEFAULT 1,
                default_deadband REAL NOT NULL DEFAULT 0.01,
                default_deadband_percent REAL NOT NULL DEFAULT 0.5,
                default_min_interval_ms INTEGER NOT NULL DEFAULT 1000,
                force_upload_interval_ms INTEGER NOT NULL DEFAULT 60000,
                outlier_enabled INTEGER NOT NULL DEFAULT 1,
                outlier_sigma_threshold REAL NOT NULL DEFAULT 4.0,
                outlier_action TEXT NOT NULL DEFAULT 'Mark',
                store_forward_enabled INTEGER NOT NULL DEFAULT 1,
                max_store_size_mb INTEGER NOT NULL DEFAULT 1000,
                retention_days INTEGER NOT NULL DEFAULT 7,
                compression_enabled INTEGER NOT NULL DEFAULT 1,
                compression_algorithm TEXT NOT NULL DEFAULT 'Gzip',
                health_check_interval_ms INTEGER NOT NULL DEFAULT 5000,
                health_check_timeout_ms INTEGER NOT NULL DEFAULT 3000,
                offline_threshold INTEGER NOT NULL DEFAULT 3,
                send_batch_size INTEGER NOT NULL DEFAULT 500,
                send_interval_ms INTEGER NOT NULL DEFAULT 500,
                created_utc INTEGER NOT NULL,
                updated_utc INTEGER,
                updated_by TEXT
            );
        ";
        await ExecuteAsync(conn, createEdgeConfig, ct);

        // 2. 标签处理配置表
        const string createTagProcessingConfig = @"
            CREATE TABLE IF NOT EXISTS tag_processing_config (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                edge_id TEXT NOT NULL,
                tag_id TEXT NOT NULL,
                deadband REAL,
                deadband_percent REAL,
                min_interval_ms INTEGER,
                bypass INTEGER NOT NULL DEFAULT 0,
                priority INTEGER NOT NULL DEFAULT 0,
                description TEXT,
                created_utc INTEGER NOT NULL,
                updated_utc INTEGER,
                UNIQUE(edge_id, tag_id)
            );

            CREATE INDEX IF NOT EXISTS idx_tag_processing_edge ON tag_processing_config(edge_id);
        ";
        await ExecuteAsync(conn, createTagProcessingConfig, ct);

        // 3. Edge 状态表（用于心跳和监控）
        const string createEdgeStatus = @"
            CREATE TABLE IF NOT EXISTS edge_status (
                edge_id TEXT PRIMARY KEY,
                is_online INTEGER NOT NULL DEFAULT 0,
                pending_points INTEGER NOT NULL DEFAULT 0,
                filter_rate REAL NOT NULL DEFAULT 0,
                sent_count INTEGER NOT NULL DEFAULT 0,
                stored_mb REAL NOT NULL DEFAULT 0,
                last_heartbeat_utc INTEGER NOT NULL,
                version TEXT
            );
        ";
        await ExecuteAsync(conn, createEdgeStatus, ct);

        var appliedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ExecuteAsync(conn,
            $"INSERT OR REPLACE INTO schema_version (version, applied_utc) VALUES (21, {appliedUtc});",
            ct);

        _logger.LogInformation("Migration v20 -> v21 completed (edge config management tables created)");
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
