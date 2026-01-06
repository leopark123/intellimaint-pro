-- IntelliMaint Pro - TimescaleDB Schema
-- Version: 1.0 (migrated from SQLite v15)

-- ==================== 设备表 ====================
CREATE TABLE device (
    device_id TEXT PRIMARY KEY,
    name TEXT,
    description TEXT,
    protocol TEXT NOT NULL,
    host TEXT,
    port INTEGER,
    connection_string TEXT,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    status TEXT NOT NULL DEFAULT 'Unknown',
    location TEXT,
    created_utc BIGINT NOT NULL,
    updated_utc BIGINT NOT NULL
);

COMMENT ON TABLE device IS '工业设备配置表';

-- ==================== 标签表 ====================
CREATE TABLE tag (
    tag_id TEXT PRIMARY KEY,
    device_id TEXT NOT NULL REFERENCES device(device_id) ON DELETE CASCADE,
    name TEXT,
    description TEXT,
    tag_group TEXT,
    data_type TEXT NOT NULL,
    unit TEXT,
    address TEXT,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    created_utc BIGINT NOT NULL,
    updated_utc BIGINT NOT NULL
);

CREATE INDEX idx_tag_device ON tag (device_id);
CREATE INDEX idx_tag_device_enabled ON tag (device_id) WHERE enabled = TRUE;

COMMENT ON TABLE tag IS '传感器标签配置表';

-- ==================== 遥测数据表（核心时序表）====================
CREATE TABLE telemetry (
    device_id TEXT NOT NULL,
    tag_id TEXT NOT NULL,
    ts BIGINT NOT NULL,
    seq INTEGER NOT NULL DEFAULT 0,
    value_type INTEGER NOT NULL,
    float64_value DOUBLE PRECISION,
    float32_value REAL,
    int64_value BIGINT,
    int32_value INTEGER,
    bool_value BOOLEAN,
    string_value TEXT,
    quality INTEGER NOT NULL DEFAULT 192,
    PRIMARY KEY (device_id, tag_id, ts, seq)
);

-- 转换为 Hypertable（按 ts 分区，7天一个分区）
-- 604800000 = 7天 * 24小时 * 60分钟 * 60秒 * 1000毫秒
SELECT create_hypertable('telemetry', by_range('ts', 604800000));

-- 创建复合索引用于常见查询
CREATE INDEX idx_tel_device_tag_ts ON telemetry (device_id, tag_id, ts DESC);
CREATE INDEX idx_tel_ts ON telemetry (ts DESC);

COMMENT ON TABLE telemetry IS '设备遥测数据时序表';

-- ==================== 1分钟聚合表 ====================
CREATE TABLE telemetry_1m (
    device_id TEXT NOT NULL,
    tag_id TEXT NOT NULL,
    ts_bucket BIGINT NOT NULL,
    min_value DOUBLE PRECISION,
    max_value DOUBLE PRECISION,
    avg_value DOUBLE PRECISION,
    sum_value DOUBLE PRECISION,
    count_value INTEGER,
    first_value DOUBLE PRECISION,
    last_value DOUBLE PRECISION,
    PRIMARY KEY (device_id, tag_id, ts_bucket)
);

-- 30天分区
SELECT create_hypertable('telemetry_1m', by_range('ts_bucket', 2592000000));

CREATE INDEX idx_tel1m_device_tag_ts ON telemetry_1m (device_id, tag_id, ts_bucket DESC);

COMMENT ON TABLE telemetry_1m IS '1分钟聚合数据表';

-- ==================== 1小时聚合表 ====================
CREATE TABLE telemetry_1h (
    device_id TEXT NOT NULL,
    tag_id TEXT NOT NULL,
    ts_bucket BIGINT NOT NULL,
    min_value DOUBLE PRECISION,
    max_value DOUBLE PRECISION,
    avg_value DOUBLE PRECISION,
    sum_value DOUBLE PRECISION,
    count_value INTEGER,
    first_value DOUBLE PRECISION,
    last_value DOUBLE PRECISION,
    PRIMARY KEY (device_id, tag_id, ts_bucket)
);

-- 90天分区
SELECT create_hypertable('telemetry_1h', by_range('ts_bucket', 7776000000));

CREATE INDEX idx_tel1h_device_tag_ts ON telemetry_1h (device_id, tag_id, ts_bucket DESC);

COMMENT ON TABLE telemetry_1h IS '1小时聚合数据表';

-- ==================== 告警规则表 ====================
CREATE TABLE alarm_rule (
    rule_id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    description TEXT,
    device_id TEXT REFERENCES device(device_id) ON DELETE CASCADE,
    tag_id TEXT REFERENCES tag(tag_id) ON DELETE CASCADE,
    rule_type INTEGER NOT NULL DEFAULT 0,
    condition_type INTEGER NOT NULL,
    threshold DOUBLE PRECISION,
    threshold_high DOUBLE PRECISION,
    severity INTEGER NOT NULL DEFAULT 2,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    debounce_ms INTEGER NOT NULL DEFAULT 5000,
    roc_window_ms INTEGER,
    roc_threshold DOUBLE PRECISION,
    created_utc BIGINT NOT NULL,
    updated_utc BIGINT NOT NULL
);

CREATE INDEX idx_alarm_rule_tag ON alarm_rule (tag_id) WHERE enabled = TRUE;
CREATE INDEX idx_alarm_rule_device ON alarm_rule (device_id) WHERE enabled = TRUE;

COMMENT ON TABLE alarm_rule IS '告警规则配置表';

-- ==================== 告警表 ====================
CREATE TABLE alarm (
    alarm_id TEXT NOT NULL,
    device_id TEXT NOT NULL,
    tag_id TEXT,
    ts BIGINT NOT NULL,
    severity INTEGER NOT NULL,
    code TEXT NOT NULL,
    message TEXT,
    status INTEGER NOT NULL DEFAULT 0,
    created_utc BIGINT NOT NULL,
    updated_utc BIGINT NOT NULL,
    group_id TEXT,
    PRIMARY KEY (alarm_id, ts)
);

-- 30天分区
SELECT create_hypertable('alarm', by_range('ts', 2592000000));

CREATE INDEX idx_alarm_device_status ON alarm (device_id, status, ts DESC);
CREATE INDEX idx_alarm_code ON alarm (code) WHERE status <> 2;
CREATE INDEX idx_alarm_severity ON alarm (severity, status);
CREATE INDEX idx_alarm_group ON alarm (group_id) WHERE group_id IS NOT NULL;

COMMENT ON TABLE alarm IS '告警事件表';

-- ==================== 告警确认表 ====================
CREATE TABLE alarm_ack (
    alarm_id TEXT PRIMARY KEY,
    acked_by TEXT,
    ack_note TEXT,
    acked_utc BIGINT
);

COMMENT ON TABLE alarm_ack IS '告警确认记录表';

-- ==================== 告警聚合组表 ====================
CREATE TABLE alarm_group (
    group_id TEXT PRIMARY KEY,
    device_id TEXT NOT NULL,
    rule_id TEXT,
    first_alarm_ts BIGINT NOT NULL,
    last_alarm_ts BIGINT NOT NULL,
    alarm_count INTEGER NOT NULL DEFAULT 1,
    aggregate_status INTEGER NOT NULL DEFAULT 0,
    acked_by TEXT,
    acked_utc BIGINT,
    ack_note TEXT,
    created_utc BIGINT NOT NULL,
    updated_utc BIGINT NOT NULL
);

CREATE INDEX idx_alarm_group_device ON alarm_group (device_id, aggregate_status);
CREATE INDEX idx_alarm_group_rule ON alarm_group (rule_id) WHERE aggregate_status <> 2;

COMMENT ON TABLE alarm_group IS '告警聚合组表';

-- ==================== 告警分组映射表 ====================
CREATE TABLE alarm_to_group (
    alarm_id TEXT PRIMARY KEY,
    group_id TEXT NOT NULL REFERENCES alarm_group(group_id) ON DELETE CASCADE
);

CREATE INDEX idx_alarm_to_group_group ON alarm_to_group (group_id);

-- ==================== 用户表 ====================
CREATE TABLE "user" (
    user_id TEXT PRIMARY KEY,
    username TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    role TEXT NOT NULL,
    display_name TEXT,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    created_utc BIGINT NOT NULL,
    last_login_utc BIGINT,
    failed_login_count INTEGER NOT NULL DEFAULT 0,
    lockout_until_utc BIGINT,
    refresh_token TEXT,
    refresh_token_expires_utc BIGINT
);

CREATE INDEX idx_user_username ON "user" (username);
CREATE INDEX idx_user_enabled ON "user" (enabled) WHERE enabled = TRUE;

COMMENT ON TABLE "user" IS '用户账户表';

-- 插入默认管理员账户 (密码: admin123)
INSERT INTO "user" (user_id, username, password_hash, role, enabled, created_utc)
VALUES (
    'admin0000000001',
    'admin',
    'JAvlGPq9JyTdtvBO6x2llnRI1+gxwIyPqCKAn3THIKk=',
    'Admin',
    TRUE,
    EXTRACT(EPOCH FROM NOW()) * 1000
);

-- ==================== 审计日志表 ====================
CREATE TABLE audit_log (
    id BIGSERIAL,
    ts BIGINT NOT NULL,
    user_id TEXT,
    action TEXT NOT NULL,
    resource_type TEXT NOT NULL,
    resource_id TEXT,
    details JSONB,
    ip_address TEXT,
    PRIMARY KEY (id, ts)
);

-- 90天分区
SELECT create_hypertable('audit_log', by_range('ts', 7776000000));

CREATE INDEX idx_audit_ts ON audit_log (ts DESC);
CREATE INDEX idx_audit_user ON audit_log (user_id, ts DESC);
CREATE INDEX idx_audit_action ON audit_log (action, ts DESC);

COMMENT ON TABLE audit_log IS '操作审计日志表';

-- ==================== 系统设置表 ====================
CREATE TABLE system_setting (
    key TEXT PRIMARY KEY,
    value TEXT,
    updated_utc BIGINT
);

COMMENT ON TABLE system_setting IS '系统配置表';

-- ==================== 健康基线表 ====================
CREATE TABLE health_baseline (
    device_id TEXT PRIMARY KEY REFERENCES device(device_id) ON DELETE CASCADE,
    baseline_data JSONB NOT NULL,
    created_utc BIGINT NOT NULL,
    updated_utc BIGINT NOT NULL
);

COMMENT ON TABLE health_baseline IS '设备健康基线表';

-- ==================== 设备健康快照表 ====================
CREATE TABLE device_health_snapshot (
    device_id TEXT NOT NULL,
    ts BIGINT NOT NULL,
    index_score INTEGER NOT NULL,
    level INTEGER NOT NULL,
    deviation_score INTEGER NOT NULL,
    trend_score INTEGER NOT NULL,
    stability_score INTEGER NOT NULL,
    alarm_score INTEGER NOT NULL,
    PRIMARY KEY (device_id, ts)
);

-- 7天分区
SELECT create_hypertable('device_health_snapshot', by_range('ts', 604800000));

CREATE INDEX idx_health_snapshot_device ON device_health_snapshot (device_id, ts DESC);

COMMENT ON TABLE device_health_snapshot IS '设备健康快照历史表';

-- ==================== 采集规则表 ====================
CREATE TABLE collection_rule (
    rule_id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    description TEXT,
    device_id TEXT NOT NULL REFERENCES device(device_id) ON DELETE CASCADE,
    tag_ids TEXT[] NOT NULL,
    trigger_condition TEXT NOT NULL,
    duration_ms INTEGER NOT NULL DEFAULT 60000,
    pre_trigger_ms INTEGER NOT NULL DEFAULT 5000,
    post_trigger_ms INTEGER NOT NULL DEFAULT 5000,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    trigger_count INTEGER NOT NULL DEFAULT 0,
    last_trigger_utc BIGINT,
    created_utc BIGINT NOT NULL,
    updated_utc BIGINT NOT NULL
);

CREATE INDEX idx_collection_rule_device ON collection_rule (device_id) WHERE enabled = TRUE;

COMMENT ON TABLE collection_rule IS '数据采集规则表';

-- ==================== 采集片段表 ====================
CREATE TABLE collection_segment (
    id BIGSERIAL PRIMARY KEY,
    rule_id TEXT NOT NULL REFERENCES collection_rule(rule_id) ON DELETE CASCADE,
    device_id TEXT NOT NULL,
    start_time_utc BIGINT NOT NULL,
    end_time_utc BIGINT,
    status INTEGER NOT NULL DEFAULT 0,
    data_points INTEGER NOT NULL DEFAULT 0,
    created_utc BIGINT NOT NULL
);

CREATE INDEX idx_collection_segment_rule ON collection_segment (rule_id, created_utc DESC);
CREATE INDEX idx_collection_segment_device ON collection_segment (device_id, created_utc DESC);

COMMENT ON TABLE collection_segment IS '采集数据片段表';

-- ==================== 工作周期表 ====================
CREATE TABLE work_cycle (
    id BIGSERIAL PRIMARY KEY,
    device_id TEXT NOT NULL,
    start_time_utc BIGINT NOT NULL,
    end_time_utc BIGINT NOT NULL,
    duration_ms BIGINT NOT NULL,
    cycle_type TEXT,
    is_anomaly BOOLEAN NOT NULL DEFAULT FALSE,
    anomaly_score DOUBLE PRECISION,
    features JSONB,
    created_utc BIGINT NOT NULL
);

CREATE INDEX idx_work_cycle_device ON work_cycle (device_id, start_time_utc DESC);
CREATE INDEX idx_work_cycle_anomaly ON work_cycle (is_anomaly, created_utc DESC) WHERE is_anomaly = TRUE;

COMMENT ON TABLE work_cycle IS '工作周期分析表';

-- ==================== 设备基线表 ====================
CREATE TABLE device_baseline (
    device_id TEXT NOT NULL,
    baseline_type TEXT NOT NULL,
    baseline_data JSONB NOT NULL,
    sample_count INTEGER NOT NULL DEFAULT 0,
    created_utc BIGINT NOT NULL,
    updated_utc BIGINT NOT NULL,
    PRIMARY KEY (device_id, baseline_type)
);

COMMENT ON TABLE device_baseline IS '设备周期基线表';

-- ==================== 标签最后数据时间表 ====================
CREATE TABLE tag_last_data (
    device_id TEXT NOT NULL,
    tag_id TEXT NOT NULL,
    last_ts BIGINT NOT NULL,
    PRIMARY KEY (device_id, tag_id)
);

COMMENT ON TABLE tag_last_data IS '标签最后数据时间跟踪表';

-- ==================== 聚合状态表 ====================
CREATE TABLE aggregate_state (
    table_name TEXT PRIMARY KEY,
    last_processed_ts BIGINT NOT NULL,
    updated_utc BIGINT NOT NULL
);

COMMENT ON TABLE aggregate_state IS '聚合任务状态表';

-- ==================== Schema 版本表 ====================
CREATE TABLE schema_version (
    version INTEGER PRIMARY KEY,
    applied_utc BIGINT NOT NULL
);

INSERT INTO schema_version (version, applied_utc)
VALUES (1, EXTRACT(EPOCH FROM NOW()) * 1000);

COMMENT ON TABLE schema_version IS 'Schema版本跟踪表';

-- ==================== 数据库统计表 ====================
CREATE TABLE db_stats (
    stat_time BIGINT PRIMARY KEY,
    telemetry_count BIGINT,
    alarm_count BIGINT,
    db_size_bytes BIGINT,
    writes_per_minute INTEGER
);

COMMENT ON TABLE db_stats IS '数据库统计信息表';

-- ==================== 迁移状态表 ====================
CREATE TABLE migration_state (
    migration_id TEXT PRIMARY KEY,
    source_db TEXT NOT NULL,
    target_db TEXT NOT NULL,
    status TEXT NOT NULL DEFAULT 'pending',
    started_utc BIGINT,
    completed_utc BIGINT,
    rows_migrated BIGINT DEFAULT 0,
    error_message TEXT
);

COMMENT ON TABLE migration_state IS '数据迁移状态跟踪表';

-- ==================== 完成日志 ====================
DO $$
BEGIN
    RAISE NOTICE 'IntelliMaint Pro TimescaleDB schema created successfully';
    RAISE NOTICE 'Tables created: 24';
    RAISE NOTICE 'Hypertables: telemetry, telemetry_1m, telemetry_1h, alarm, audit_log, device_health_snapshot';
END $$;
