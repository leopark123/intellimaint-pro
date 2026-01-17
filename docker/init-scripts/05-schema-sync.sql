-- IntelliMaint Pro - Schema Synchronization Patch
-- Version: 1.0
-- Purpose: Ensure TimescaleDB schema matches SQLite for seamless switching
-- Applied after: 02-schema.sql, 03-alarm-optimization.sql, 04-timescaledb-optimization.sql

-- ==================== 1. 补充缺失的表 ====================

-- 1.1 MQTT Outbox 表 (与 SQLite 一致)
CREATE TABLE IF NOT EXISTS mqtt_outbox (
    id BIGSERIAL PRIMARY KEY,
    topic TEXT NOT NULL,
    payload BYTEA NOT NULL,
    qos INTEGER NOT NULL DEFAULT 0,
    created_utc BIGINT NOT NULL,
    retry_count INTEGER NOT NULL DEFAULT 0,
    last_retry_utc BIGINT,
    status INTEGER NOT NULL DEFAULT 0,  -- 0=pending, 1=processing, 2=completed, 3=failed
    error_message TEXT
);

CREATE INDEX IF NOT EXISTS idx_outbox_status_created ON mqtt_outbox (status, created_utc);

COMMENT ON TABLE mqtt_outbox IS 'MQTT 消息发件箱 (Outbox 模式，用于可靠消息投递)';

-- 1.2 API Key 表 (与 SQLite 一致，增强版)
CREATE TABLE IF NOT EXISTS api_key (
    key_id TEXT PRIMARY KEY,
    key_hash TEXT NOT NULL,
    name TEXT,
    role TEXT NOT NULL DEFAULT 'Viewer',
    expires_utc BIGINT,
    last_used_utc BIGINT,
    allowed_ips JSONB,
    rate_limit INTEGER DEFAULT 1000,
    created_utc BIGINT NOT NULL,
    revoked_utc BIGINT
);

CREATE INDEX IF NOT EXISTS idx_api_key_active ON api_key (key_id) WHERE revoked_utc IS NULL;

COMMENT ON TABLE api_key IS 'API 密钥管理表 (用于第三方系统集成)';

-- ==================== 2. 补充 device 表缺失字段 ====================

DO $$
BEGIN
    -- 添加 model 字段
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'device' AND column_name = 'model') THEN
        ALTER TABLE device ADD COLUMN model TEXT;
        RAISE NOTICE 'Added column: device.model';
    END IF;

    -- 添加 metadata 字段
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'device' AND column_name = 'metadata') THEN
        ALTER TABLE device ADD COLUMN metadata JSONB;
        RAISE NOTICE 'Added column: device.metadata';
    END IF;
END $$;

-- ==================== 3. 补充 tag 表缺失字段 ====================

DO $$
BEGIN
    -- 添加 scan_interval_ms 字段
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'tag' AND column_name = 'scan_interval_ms') THEN
        ALTER TABLE tag ADD COLUMN scan_interval_ms INTEGER;
        RAISE NOTICE 'Added column: tag.scan_interval_ms';
    END IF;

    -- 添加 metadata 字段
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'tag' AND column_name = 'metadata') THEN
        ALTER TABLE tag ADD COLUMN metadata JSONB;
        RAISE NOTICE 'Added column: tag.metadata';
    END IF;
END $$;

-- ==================== 4. 补充 user 表缺失字段 ====================

DO $$
BEGIN
    -- 添加 display_name 字段
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'user' AND column_name = 'display_name') THEN
        ALTER TABLE "user" ADD COLUMN display_name TEXT;
        RAISE NOTICE 'Added column: user.display_name';

        -- 为现有用户设置默认 display_name
        UPDATE "user" SET display_name = username WHERE display_name IS NULL;
    END IF;
END $$;

-- ==================== 5. 补充 alarm 表缺失字段 ====================

DO $$
BEGIN
    -- 添加 group_id 字段 (如果不存在)
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'alarm' AND column_name = 'group_id') THEN
        ALTER TABLE alarm ADD COLUMN group_id TEXT;
        CREATE INDEX IF NOT EXISTS idx_alarm_group ON alarm (group_id) WHERE group_id IS NOT NULL;
        RAISE NOTICE 'Added column: alarm.group_id';
    END IF;
END $$;

-- ==================== 6. 补充 audit_log 表缺失字段 ====================

DO $$
BEGIN
    -- 添加 user_name 字段 (保持与 SQLite 一致)
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'audit_log' AND column_name = 'user_name') THEN
        ALTER TABLE audit_log ADD COLUMN user_name TEXT;
        RAISE NOTICE 'Added column: audit_log.user_name';
    END IF;

    -- v65: 将 details 列从 JSONB 转换为 TEXT
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_name = 'audit_log' AND column_name = 'details' AND data_type = 'jsonb'
    ) THEN
        ALTER TABLE audit_log ALTER COLUMN details TYPE TEXT;
        RAISE NOTICE 'Changed column type: audit_log.details from JSONB to TEXT';
    END IF;
END $$;

-- ==================== 7. 补充 alarm_rule 表缺失字段 ====================

DO $$
BEGIN
    -- 添加 rule_type 字段
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'alarm_rule' AND column_name = 'rule_type') THEN
        ALTER TABLE alarm_rule ADD COLUMN rule_type INTEGER NOT NULL DEFAULT 0;
        RAISE NOTICE 'Added column: alarm_rule.rule_type';
    END IF;

    -- 添加 threshold_high 字段 (用于范围告警)
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'alarm_rule' AND column_name = 'threshold_high') THEN
        ALTER TABLE alarm_rule ADD COLUMN threshold_high DOUBLE PRECISION;
        RAISE NOTICE 'Added column: alarm_rule.threshold_high';
    END IF;

    -- v65: 添加 duration_ms 字段
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'alarm_rule' AND column_name = 'duration_ms') THEN
        ALTER TABLE alarm_rule ADD COLUMN duration_ms BIGINT DEFAULT 0;
        RAISE NOTICE 'Added column: alarm_rule.duration_ms';
    END IF;

    -- v65: 添加 message_template 字段
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'alarm_rule' AND column_name = 'message_template') THEN
        ALTER TABLE alarm_rule ADD COLUMN message_template TEXT;
        RAISE NOTICE 'Added column: alarm_rule.message_template';
    END IF;
END $$;

-- ==================== 8. 补充 health_baseline 表缺失字段 (v65) ====================

DO $$
BEGIN
    -- 添加 sample_count 字段
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'health_baseline' AND column_name = 'sample_count') THEN
        ALTER TABLE health_baseline ADD COLUMN sample_count INTEGER DEFAULT 0;
        RAISE NOTICE 'Added column: health_baseline.sample_count';
    END IF;

    -- 添加 learning_hours 字段
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'health_baseline' AND column_name = 'learning_hours') THEN
        ALTER TABLE health_baseline ADD COLUMN learning_hours INTEGER DEFAULT 0;
        RAISE NOTICE 'Added column: health_baseline.learning_hours';
    END IF;

    -- 添加 tag_baselines_json 字段
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'health_baseline' AND column_name = 'tag_baselines_json') THEN
        ALTER TABLE health_baseline ADD COLUMN tag_baselines_json JSONB;
        RAISE NOTICE 'Added column: health_baseline.tag_baselines_json';
    END IF;
END $$;

-- ==================== 9. 补充 collection_rule 表缺失字段 (v65) ====================

DO $$
BEGIN
    -- 添加 start_condition_json 字段
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'collection_rule' AND column_name = 'start_condition_json') THEN
        ALTER TABLE collection_rule ADD COLUMN start_condition_json TEXT;
        RAISE NOTICE 'Added column: collection_rule.start_condition_json';
    END IF;

    -- 添加 stop_condition_json 字段
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'collection_rule' AND column_name = 'stop_condition_json') THEN
        ALTER TABLE collection_rule ADD COLUMN stop_condition_json TEXT;
        RAISE NOTICE 'Added column: collection_rule.stop_condition_json';
    END IF;
END $$;

-- ==================== 10. 数据类型统一说明 ====================

-- tag.data_type 字段类型差异说明:
-- - SQLite: INTEGER (枚举值)
-- - TimescaleDB: TEXT (字符串)
--
-- 映射关系:
--   SQLite INTEGER | TimescaleDB TEXT | 说明
--   --------------|------------------|--------
--   1             | 'Bool'           | 布尔
--   4             | 'Int16'          | 短整数
--   6             | 'Int32'          | 整数
--   8             | 'Int64'          | 长整数
--   10            | 'Float32'        | 单精度浮点
--   11            | 'Float64'        | 双精度浮点
--   12            | 'String'         | 字符串
--
-- 代码层已处理此差异，无需数据库层统一

-- ==================== 9. telemetry 表字段差异说明 ====================

-- SQLite telemetry 表有 17 个值字段 (完整类型支持):
--   bool_value, int8_value, uint8_value, int16_value, uint16_value,
--   int32_value, uint32_value, int64_value, uint64_value,
--   float32_value, float64_value, string_value, byte_array_value,
--   unit, source, protocol
--
-- TimescaleDB telemetry 表有 6 个值字段 (精简优化):
--   bool_value, int32_value, int64_value, float32_value, float64_value, string_value
--
-- 差异原因:
-- - TimescaleDB 侧重生产环境性能，精简字段减少存储
-- - SQLite 侧重完整性，支持所有 PLC 数据类型
--
-- 代码层已处理此差异:
-- - TelemetryRepository 两边实现独立
-- - 写入时自动类型转换 (如 int16 -> int32)
-- - 读取时根据 value_type 字段解析

-- ==================== 10. 记录同步版本 ====================

INSERT INTO schema_version (version, applied_utc)
VALUES (4, EXTRACT(EPOCH FROM NOW()) * 1000)
ON CONFLICT (version) DO UPDATE SET applied_utc = EXTRACT(EPOCH FROM NOW()) * 1000;

-- ==================== 11. 完成日志 ====================

DO $$
BEGIN
    RAISE NOTICE '========================================';
    RAISE NOTICE 'Schema Synchronization Complete (v65)';
    RAISE NOTICE '========================================';
    RAISE NOTICE 'Added tables: mqtt_outbox, api_key';
    RAISE NOTICE 'Added columns: device.model, device.metadata';
    RAISE NOTICE 'Added columns: tag.scan_interval_ms, tag.metadata';
    RAISE NOTICE 'Added columns: user.display_name';
    RAISE NOTICE 'Added columns: alarm.group_id';
    RAISE NOTICE 'Added columns: audit_log.user_name';
    RAISE NOTICE 'Changed type: audit_log.details (JSONB -> TEXT)';
    RAISE NOTICE 'Added columns: alarm_rule.rule_type, threshold_high, duration_ms, message_template';
    RAISE NOTICE 'Added columns: health_baseline.sample_count, learning_hours, tag_baselines_json';
    RAISE NOTICE 'Added columns: collection_rule.start_condition_json, stop_condition_json';
    RAISE NOTICE '';
    RAISE NOTICE 'Schema version: 4';
    RAISE NOTICE '========================================';
END $$;
