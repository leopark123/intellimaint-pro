-- IntelliMaint Pro - Alarm Center Optimization with TimescaleDB
-- Version: 2.0
-- Leverages TimescaleDB native features: time_bucket, Continuous Aggregates, Compression

-- ==================== 1. Schema Migration (if needed) ====================
-- Add missing columns to alarm_group if upgrading from old schema
DO $$
BEGIN
    -- Add tag_id if not exists
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'alarm_group' AND column_name = 'tag_id') THEN
        ALTER TABLE alarm_group ADD COLUMN tag_id TEXT;
    END IF;

    -- Add severity if not exists
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'alarm_group' AND column_name = 'severity') THEN
        ALTER TABLE alarm_group ADD COLUMN severity INTEGER NOT NULL DEFAULT 2;
    END IF;

    -- Add code if not exists
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'alarm_group' AND column_name = 'code') THEN
        ALTER TABLE alarm_group ADD COLUMN code TEXT;
    END IF;

    -- Add message if not exists
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'alarm_group' AND column_name = 'message') THEN
        ALTER TABLE alarm_group ADD COLUMN message TEXT;
    END IF;

    -- Rename columns if old schema (first_alarm_ts -> first_occurred_utc)
    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name = 'alarm_group' AND column_name = 'first_alarm_ts') THEN
        ALTER TABLE alarm_group RENAME COLUMN first_alarm_ts TO first_occurred_utc;
    END IF;

    IF EXISTS (SELECT 1 FROM information_schema.columns
               WHERE table_name = 'alarm_group' AND column_name = 'last_alarm_ts') THEN
        ALTER TABLE alarm_group RENAME COLUMN last_alarm_ts TO last_occurred_utc;
    END IF;

    -- Add added_utc to alarm_to_group if not exists
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name = 'alarm_to_group' AND column_name = 'added_utc') THEN
        ALTER TABLE alarm_to_group ADD COLUMN added_utc BIGINT NOT NULL DEFAULT 0;
    END IF;

    RAISE NOTICE 'Schema migration completed';
END $$;

-- ==================== 2. Additional Indexes for alarm table ====================
CREATE INDEX IF NOT EXISTS idx_alarm_device_status_ts ON alarm(device_id, status, ts DESC);
CREATE INDEX IF NOT EXISTS idx_alarm_code_ts ON alarm(code, ts DESC);
CREATE INDEX IF NOT EXISTS idx_alarm_severity_status ON alarm(severity, status);

-- ==================== 3. Integer Time Function for CAGG Support ====================
-- Create function to return current millisecond timestamp (required for integer-based hypertables)
CREATE OR REPLACE FUNCTION unix_now_ms() RETURNS BIGINT LANGUAGE SQL STABLE AS $$
    SELECT (EXTRACT(EPOCH FROM NOW()) * 1000)::BIGINT;
$$;

-- Set integer_now function for alarm hypertable (required for CAGG with integer time)
DO $$
BEGIN
    PERFORM set_integer_now_func('alarm', 'unix_now_ms');
    RAISE NOTICE 'integer_now_func set for alarm table';
EXCEPTION WHEN OTHERS THEN
    RAISE NOTICE 'integer_now_func already set or error: %', SQLERRM;
END $$;

-- ==================== 4. Continuous Aggregates ====================
-- Drop existing views if they exist (for clean recreation)
DROP MATERIALIZED VIEW IF EXISTS alarm_hourly_cagg CASCADE;
DROP MATERIALIZED VIEW IF EXISTS alarm_daily_cagg CASCADE;

-- 3.1 Hourly Alarm Statistics (Continuous Aggregate)
-- Uses time_bucket for efficient time-series aggregation
CREATE MATERIALIZED VIEW alarm_hourly_cagg
WITH (timescaledb.continuous) AS
SELECT
    time_bucket(3600000::bigint, ts) AS bucket,
    device_id,
    COUNT(*)::int AS total_count,
    SUM(CASE WHEN status = 0 THEN 1 ELSE 0 END)::int AS open_count,
    SUM(CASE WHEN status = 1 THEN 1 ELSE 0 END)::int AS acked_count,
    SUM(CASE WHEN status = 2 THEN 1 ELSE 0 END)::int AS closed_count,
    SUM(CASE WHEN severity >= 4 THEN 1 ELSE 0 END)::int AS critical_count,
    SUM(CASE WHEN severity = 3 THEN 1 ELSE 0 END)::int AS alarm_count,
    SUM(CASE WHEN severity = 2 THEN 1 ELSE 0 END)::int AS warning_count,
    SUM(CASE WHEN severity = 1 THEN 1 ELSE 0 END)::int AS info_count,
    MAX(severity) AS max_severity,
    MIN(ts) AS first_ts,
    MAX(ts) AS last_ts
FROM alarm
GROUP BY time_bucket(3600000::bigint, ts), device_id
WITH NO DATA;

-- Add refresh policy: refresh every 5 minutes, covering last 24 hours
SELECT add_continuous_aggregate_policy('alarm_hourly_cagg',
    start_offset => 86400000::bigint,   -- 24 hours ago
    end_offset => 300000::bigint,        -- 5 minutes ago (allow for data lag)
    schedule_interval => INTERVAL '5 minutes');

-- Index on hourly aggregate
CREATE INDEX IF NOT EXISTS idx_alarm_hourly_bucket ON alarm_hourly_cagg (bucket DESC);
CREATE INDEX IF NOT EXISTS idx_alarm_hourly_device_bucket ON alarm_hourly_cagg (device_id, bucket DESC);

COMMENT ON MATERIALIZED VIEW alarm_hourly_cagg IS 'Hourly alarm statistics - auto-refreshed every 5 minutes';

-- 3.2 Daily Alarm Statistics (Continuous Aggregate)
CREATE MATERIALIZED VIEW alarm_daily_cagg
WITH (timescaledb.continuous) AS
SELECT
    time_bucket(86400000::bigint, ts) AS bucket,
    device_id,
    COUNT(*)::int AS total_count,
    SUM(CASE WHEN status = 0 THEN 1 ELSE 0 END)::int AS open_count,
    SUM(CASE WHEN severity >= 4 THEN 1 ELSE 0 END)::int AS critical_count,
    SUM(CASE WHEN severity IN (2,3) THEN 1 ELSE 0 END)::int AS warning_count,
    COUNT(DISTINCT code) AS distinct_rules,
    MAX(severity) AS max_severity
FROM alarm
GROUP BY time_bucket(86400000::bigint, ts), device_id
WITH NO DATA;

-- Add refresh policy: refresh every hour, covering last 7 days
SELECT add_continuous_aggregate_policy('alarm_daily_cagg',
    start_offset => 604800000::bigint,  -- 7 days ago
    end_offset => 3600000::bigint,       -- 1 hour ago
    schedule_interval => INTERVAL '1 hour');

-- Index on daily aggregate
CREATE INDEX IF NOT EXISTS idx_alarm_daily_bucket ON alarm_daily_cagg (bucket DESC);
CREATE INDEX IF NOT EXISTS idx_alarm_daily_device_bucket ON alarm_daily_cagg (device_id, bucket DESC);

COMMENT ON MATERIALIZED VIEW alarm_daily_cagg IS 'Daily alarm statistics - auto-refreshed every hour';

-- ==================== 5. Compression Policy ====================
-- Enable compression on alarm hypertable (compress data older than 7 days)
ALTER TABLE alarm SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'device_id',
    timescaledb.compress_orderby = 'ts DESC'
);

-- Add compression policy: compress chunks older than 7 days (604800000 ms)
SELECT add_compression_policy('alarm', 604800000::bigint);

COMMENT ON TABLE alarm IS 'Alarm events - compressed after 7 days';

-- ==================== 6. Helper Functions ====================

-- 6.1 Get alarm trend using Continuous Aggregate (fast path)
CREATE OR REPLACE FUNCTION get_alarm_trend_fast(
    p_device_id TEXT DEFAULT NULL,
    p_start_ts BIGINT DEFAULT NULL,
    p_end_ts BIGINT DEFAULT NULL,
    p_bucket_size_ms BIGINT DEFAULT 3600000
)
RETURNS TABLE (
    bucket BIGINT,
    device_id TEXT,
    total_count INT,
    open_count INT,
    critical_count INT,
    warning_count INT
) AS $$
BEGIN
    -- Use hourly CAGG for hourly buckets
    IF p_bucket_size_ms = 3600000 THEN
        RETURN QUERY
        SELECT
            h.bucket,
            h.device_id,
            h.total_count,
            h.open_count,
            h.critical_count,
            h.warning_count + h.alarm_count AS warning_count
        FROM alarm_hourly_cagg h
        WHERE (p_device_id IS NULL OR h.device_id = p_device_id)
          AND (p_start_ts IS NULL OR h.bucket >= p_start_ts)
          AND (p_end_ts IS NULL OR h.bucket <= p_end_ts)
        ORDER BY h.bucket DESC;
    -- Use daily CAGG for daily buckets
    ELSIF p_bucket_size_ms = 86400000 THEN
        RETURN QUERY
        SELECT
            d.bucket,
            d.device_id,
            d.total_count,
            d.open_count,
            d.critical_count,
            d.warning_count
        FROM alarm_daily_cagg d
        WHERE (p_device_id IS NULL OR d.device_id = p_device_id)
          AND (p_start_ts IS NULL OR d.bucket >= p_start_ts)
          AND (p_end_ts IS NULL OR d.bucket <= p_end_ts)
        ORDER BY d.bucket DESC;
    -- Fallback to raw table with time_bucket for custom bucket sizes
    ELSE
        RETURN QUERY
        SELECT
            time_bucket(p_bucket_size_ms, a.ts) AS bucket,
            a.device_id,
            COUNT(*)::int AS total_count,
            SUM(CASE WHEN a.status = 0 THEN 1 ELSE 0 END)::int AS open_count,
            SUM(CASE WHEN a.severity >= 4 THEN 1 ELSE 0 END)::int AS critical_count,
            SUM(CASE WHEN a.severity IN (2,3) THEN 1 ELSE 0 END)::int AS warning_count
        FROM alarm a
        WHERE (p_device_id IS NULL OR a.device_id = p_device_id)
          AND (p_start_ts IS NULL OR a.ts >= p_start_ts)
          AND (p_end_ts IS NULL OR a.ts <= p_end_ts)
        GROUP BY time_bucket(p_bucket_size_ms, a.ts), a.device_id
        ORDER BY bucket DESC;
    END IF;
END;
$$ LANGUAGE plpgsql STABLE;

COMMENT ON FUNCTION get_alarm_trend_fast IS 'Get alarm trend - uses CAGG for hourly/daily, falls back to time_bucket for custom intervals';

-- 6.2 Get alarm summary for dashboard
CREATE OR REPLACE FUNCTION get_alarm_summary(p_device_id TEXT DEFAULT NULL)
RETURNS TABLE (
    total_open INT,
    total_acked INT,
    total_closed INT,
    critical_open INT,
    warning_open INT,
    last_24h_count INT,
    last_7d_count INT
) AS $$
DECLARE
    v_now BIGINT := EXTRACT(EPOCH FROM NOW()) * 1000;
    v_24h_ago BIGINT := v_now - 86400000;
    v_7d_ago BIGINT := v_now - 604800000;
BEGIN
    RETURN QUERY
    SELECT
        SUM(CASE WHEN status = 0 THEN 1 ELSE 0 END)::int AS total_open,
        SUM(CASE WHEN status = 1 THEN 1 ELSE 0 END)::int AS total_acked,
        SUM(CASE WHEN status = 2 THEN 1 ELSE 0 END)::int AS total_closed,
        SUM(CASE WHEN status = 0 AND severity >= 4 THEN 1 ELSE 0 END)::int AS critical_open,
        SUM(CASE WHEN status = 0 AND severity IN (2,3) THEN 1 ELSE 0 END)::int AS warning_open,
        SUM(CASE WHEN ts >= v_24h_ago THEN 1 ELSE 0 END)::int AS last_24h_count,
        SUM(CASE WHEN ts >= v_7d_ago THEN 1 ELSE 0 END)::int AS last_7d_count
    FROM alarm
    WHERE (p_device_id IS NULL OR device_id = p_device_id);
END;
$$ LANGUAGE plpgsql STABLE;

-- 6.3 Get alarm group summary with aggregation
CREATE OR REPLACE FUNCTION get_alarm_group_stats(p_device_id TEXT DEFAULT NULL)
RETURNS TABLE (
    total_groups INT,
    open_groups INT,
    total_alarms INT,
    avg_alarms_per_group NUMERIC
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        COUNT(*)::int AS total_groups,
        SUM(CASE WHEN aggregate_status = 0 THEN 1 ELSE 0 END)::int AS open_groups,
        SUM(alarm_count)::int AS total_alarms,
        ROUND(AVG(alarm_count), 2) AS avg_alarms_per_group
    FROM alarm_group
    WHERE (p_device_id IS NULL OR device_id = p_device_id);
END;
$$ LANGUAGE plpgsql STABLE;

-- ==================== 7. Data Retention Policy ====================
-- Automatically drop chunks older than 90 days (7776000000 ms)
SELECT add_retention_policy('alarm', 7776000000::bigint);

-- ==================== 8. Initial Data Refresh ====================
-- Manually refresh continuous aggregates to populate initial data
CALL refresh_continuous_aggregate('alarm_hourly_cagg', NULL, NULL);
CALL refresh_continuous_aggregate('alarm_daily_cagg', NULL, NULL);

-- ==================== 9. Record Schema Version ====================
INSERT INTO schema_version (version, applied_utc)
VALUES (3, EXTRACT(EPOCH FROM NOW()) * 1000)
ON CONFLICT (version) DO UPDATE SET applied_utc = EXTRACT(EPOCH FROM NOW()) * 1000;

-- ==================== 10. Summary ====================
DO $$
DECLARE
    v_alarm_count BIGINT;
    v_group_count BIGINT;
    v_hourly_count BIGINT;
    v_daily_count BIGINT;
BEGIN
    SELECT COUNT(*) INTO v_alarm_count FROM alarm;
    SELECT COUNT(*) INTO v_group_count FROM alarm_group;
    SELECT COUNT(*) INTO v_hourly_count FROM alarm_hourly_cagg;
    SELECT COUNT(*) INTO v_daily_count FROM alarm_daily_cagg;

    RAISE NOTICE '========================================';
    RAISE NOTICE 'Alarm Center Optimization Complete';
    RAISE NOTICE '========================================';
    RAISE NOTICE 'Total alarms: %', v_alarm_count;
    RAISE NOTICE 'Total groups: %', v_group_count;
    RAISE NOTICE 'Hourly aggregates: %', v_hourly_count;
    RAISE NOTICE 'Daily aggregates: %', v_daily_count;
    RAISE NOTICE '';
    RAISE NOTICE 'Features enabled:';
    RAISE NOTICE '  - Continuous Aggregates (hourly/daily)';
    RAISE NOTICE '  - Compression (7+ days old data)';
    RAISE NOTICE '  - Retention Policy (90 days)';
    RAISE NOTICE '  - Optimized indexes';
    RAISE NOTICE '========================================';
END $$;
