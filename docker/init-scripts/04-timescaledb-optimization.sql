-- IntelliMaint Pro - TimescaleDB Performance Optimization
-- Version: 1.0
-- Description: Enable compression, retention policies, and continuous aggregates

-- ==================== 1. 遥测数据压缩优化 ====================

-- 启用 telemetry 表压缩 (按 device_id, tag_id 分段压缩，7天后自动压缩)
ALTER TABLE telemetry SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'device_id, tag_id',
    timescaledb.compress_orderby = 'ts DESC, seq DESC'
);

-- 添加自动压缩策略：7天前的数据自动压缩
SELECT add_compression_policy('telemetry', INTERVAL '7 days');

-- 启用 telemetry_1m 表压缩
ALTER TABLE telemetry_1m SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'device_id, tag_id',
    timescaledb.compress_orderby = 'ts_bucket DESC'
);

SELECT add_compression_policy('telemetry_1m', INTERVAL '30 days');

-- 启用 telemetry_1h 表压缩
ALTER TABLE telemetry_1h SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'device_id, tag_id',
    timescaledb.compress_orderby = 'ts_bucket DESC'
);

SELECT add_compression_policy('telemetry_1h', INTERVAL '90 days');

-- 启用 device_health_snapshot 压缩
ALTER TABLE device_health_snapshot SET (
    timescaledb.compress,
    timescaledb.compress_segmentby = 'device_id',
    timescaledb.compress_orderby = 'ts DESC'
);

SELECT add_compression_policy('device_health_snapshot', INTERVAL '7 days');

-- 启用 audit_log 压缩
ALTER TABLE audit_log SET (
    timescaledb.compress,
    timescaledb.compress_orderby = 'ts DESC'
);

SELECT add_compression_policy('audit_log', INTERVAL '30 days');


-- ==================== 2. 数据保留策略 ====================

-- telemetry 原始数据保留90天 (90 * 24 * 60 * 60 * 1000 = 7776000000)
SELECT add_retention_policy('telemetry', BIGINT '7776000000');

-- telemetry_1m 聚合数据保留180天
SELECT add_retention_policy('telemetry_1m', BIGINT '15552000000');

-- telemetry_1h 聚合数据保留365天
SELECT add_retention_policy('telemetry_1h', BIGINT '31536000000');

-- device_health_snapshot 保留90天
SELECT add_retention_policy('device_health_snapshot', BIGINT '7776000000');

-- audit_log 保留180天
SELECT add_retention_policy('audit_log', BIGINT '15552000000');


-- ==================== 3. 遥测数据连续聚合视图 ====================

-- 3.1 1分钟聚合视图 (实时物化，自动增量更新)
CREATE MATERIALIZED VIEW IF NOT EXISTS telemetry_1m_cagg
WITH (timescaledb.continuous) AS
SELECT
    time_bucket(BIGINT '60000', ts) AS bucket,
    device_id,
    tag_id,
    MIN(COALESCE(float64_value, float32_value::double precision)) AS min_value,
    MAX(COALESCE(float64_value, float32_value::double precision)) AS max_value,
    AVG(COALESCE(float64_value, float32_value::double precision)) AS avg_value,
    SUM(COALESCE(float64_value, float32_value::double precision)) AS sum_value,
    COUNT(*) AS count_value,
    first(COALESCE(float64_value, float32_value::double precision), ts) AS first_value,
    last(COALESCE(float64_value, float32_value::double precision), ts) AS last_value
FROM telemetry
GROUP BY bucket, device_id, tag_id
WITH NO DATA;

-- 自动刷新策略：每1分钟刷新，覆盖最近10分钟的数据
SELECT add_continuous_aggregate_policy('telemetry_1m_cagg',
    start_offset => BIGINT '600000',   -- 10分钟前开始 (需覆盖至少2个1分钟桶)
    end_offset => BIGINT '60000',      -- 1分钟前结束
    schedule_interval => INTERVAL '1 minute'
);


-- 3.2 1小时聚合视图
CREATE MATERIALIZED VIEW IF NOT EXISTS telemetry_1h_cagg
WITH (timescaledb.continuous) AS
SELECT
    time_bucket(BIGINT '3600000', ts) AS bucket,
    device_id,
    tag_id,
    MIN(COALESCE(float64_value, float32_value::double precision)) AS min_value,
    MAX(COALESCE(float64_value, float32_value::double precision)) AS max_value,
    AVG(COALESCE(float64_value, float32_value::double precision)) AS avg_value,
    SUM(COALESCE(float64_value, float32_value::double precision)) AS sum_value,
    COUNT(*) AS count_value,
    first(COALESCE(float64_value, float32_value::double precision), ts) AS first_value,
    last(COALESCE(float64_value, float32_value::double precision), ts) AS last_value
FROM telemetry
GROUP BY bucket, device_id, tag_id
WITH NO DATA;

-- 自动刷新策略：每30分钟刷新，覆盖最近4小时的数据
SELECT add_continuous_aggregate_policy('telemetry_1h_cagg',
    start_offset => BIGINT '14400000',  -- 4小时前开始 (需覆盖至少2个1小时桶)
    end_offset => BIGINT '3600000',     -- 1小时前结束
    schedule_interval => INTERVAL '30 minutes'
);


-- 3.3 1天聚合视图 (用于长期趋势分析)
CREATE MATERIALIZED VIEW IF NOT EXISTS telemetry_1d_cagg
WITH (timescaledb.continuous) AS
SELECT
    time_bucket(BIGINT '86400000', ts) AS bucket,
    device_id,
    tag_id,
    MIN(COALESCE(float64_value, float32_value::double precision)) AS min_value,
    MAX(COALESCE(float64_value, float32_value::double precision)) AS max_value,
    AVG(COALESCE(float64_value, float32_value::double precision)) AS avg_value,
    SUM(COALESCE(float64_value, float32_value::double precision)) AS sum_value,
    COUNT(*) AS count_value,
    first(COALESCE(float64_value, float32_value::double precision), ts) AS first_value,
    last(COALESCE(float64_value, float32_value::double precision), ts) AS last_value
FROM telemetry
GROUP BY bucket, device_id, tag_id
WITH NO DATA;

-- 自动刷新策略：每6小时刷新，覆盖最近3天的数据
SELECT add_continuous_aggregate_policy('telemetry_1d_cagg',
    start_offset => BIGINT '259200000',  -- 3天前开始 (需覆盖至少2个1天桶)
    end_offset => BIGINT '86400000',     -- 1天前结束
    schedule_interval => INTERVAL '6 hours'
);


-- ==================== 4. 连续聚合压缩 ====================

-- 启用连续聚合的压缩
ALTER MATERIALIZED VIEW telemetry_1m_cagg SET (
    timescaledb.compress = true
);
SELECT add_compression_policy('telemetry_1m_cagg', INTERVAL '7 days');

ALTER MATERIALIZED VIEW telemetry_1h_cagg SET (
    timescaledb.compress = true
);
SELECT add_compression_policy('telemetry_1h_cagg', INTERVAL '30 days');

ALTER MATERIALIZED VIEW telemetry_1d_cagg SET (
    timescaledb.compress = true
);
SELECT add_compression_policy('telemetry_1d_cagg', INTERVAL '90 days');


-- ==================== 5. 优化索引 ====================

-- 为连续聚合视图创建辅助索引
CREATE INDEX IF NOT EXISTS idx_tel1m_cagg_device ON telemetry_1m_cagg (device_id, bucket DESC);
CREATE INDEX IF NOT EXISTS idx_tel1h_cagg_device ON telemetry_1h_cagg (device_id, bucket DESC);
CREATE INDEX IF NOT EXISTS idx_tel1d_cagg_device ON telemetry_1d_cagg (device_id, bucket DESC);

-- 为常用查询创建复合索引
CREATE INDEX IF NOT EXISTS idx_tel1m_cagg_full ON telemetry_1m_cagg (device_id, tag_id, bucket DESC);
CREATE INDEX IF NOT EXISTS idx_tel1h_cagg_full ON telemetry_1h_cagg (device_id, tag_id, bucket DESC);
CREATE INDEX IF NOT EXISTS idx_tel1d_cagg_full ON telemetry_1d_cagg (device_id, tag_id, bucket DESC);


-- ==================== 6. 查询优化函数 ====================

-- 创建自适应降采样函数：根据时间范围自动选择最佳数据源
CREATE OR REPLACE FUNCTION get_telemetry_adaptive(
    p_device_id TEXT,
    p_tag_id TEXT,
    p_start_ts BIGINT,
    p_end_ts BIGINT,
    p_max_points INTEGER DEFAULT 1000
) RETURNS TABLE (
    bucket BIGINT,
    min_value DOUBLE PRECISION,
    max_value DOUBLE PRECISION,
    avg_value DOUBLE PRECISION,
    count_value BIGINT
) LANGUAGE plpgsql AS $$
DECLARE
    time_range BIGINT;
    target_interval BIGINT;
BEGIN
    time_range := p_end_ts - p_start_ts;
    target_interval := time_range / p_max_points;

    -- 根据时间范围选择最佳数据源
    IF target_interval < 60000 THEN
        -- 小于1分钟，使用原始数据
        RETURN QUERY
        SELECT
            (t.ts / 10000) * 10000 AS bucket,
            MIN(COALESCE(t.float64_value, t.float32_value::double precision))::double precision,
            MAX(COALESCE(t.float64_value, t.float32_value::double precision))::double precision,
            AVG(COALESCE(t.float64_value, t.float32_value::double precision))::double precision,
            COUNT(*)
        FROM telemetry t
        WHERE t.device_id = p_device_id
          AND t.tag_id = p_tag_id
          AND t.ts >= p_start_ts
          AND t.ts < p_end_ts
        GROUP BY (t.ts / 10000) * 10000
        ORDER BY bucket;

    ELSIF target_interval < 3600000 THEN
        -- 小于1小时，使用1分钟聚合
        RETURN QUERY
        SELECT
            m.bucket,
            m.min_value::double precision,
            m.max_value::double precision,
            m.avg_value::double precision,
            m.count_value
        FROM telemetry_1m_cagg m
        WHERE m.device_id = p_device_id
          AND m.tag_id = p_tag_id
          AND m.bucket >= p_start_ts
          AND m.bucket < p_end_ts
        ORDER BY m.bucket;

    ELSIF target_interval < 86400000 THEN
        -- 小于1天，使用1小时聚合
        RETURN QUERY
        SELECT
            h.bucket,
            h.min_value::double precision,
            h.max_value::double precision,
            h.avg_value::double precision,
            h.count_value
        FROM telemetry_1h_cagg h
        WHERE h.device_id = p_device_id
          AND h.tag_id = p_tag_id
          AND h.bucket >= p_start_ts
          AND h.bucket < p_end_ts
        ORDER BY h.bucket;

    ELSE
        -- 超过1天，使用1天聚合
        RETURN QUERY
        SELECT
            d.bucket,
            d.min_value::double precision,
            d.max_value::double precision,
            d.avg_value::double precision,
            d.count_value
        FROM telemetry_1d_cagg d
        WHERE d.device_id = p_device_id
          AND d.tag_id = p_tag_id
          AND d.bucket >= p_start_ts
          AND d.bucket < p_end_ts
        ORDER BY d.bucket;
    END IF;
END;
$$;

COMMENT ON FUNCTION get_telemetry_adaptive IS '自适应降采样查询函数，根据时间范围自动选择最佳数据源';


-- ==================== 7. 数据库维护函数 ====================

-- 手动触发压缩函数
CREATE OR REPLACE FUNCTION compress_all_chunks(
    p_older_than INTERVAL DEFAULT '7 days'
) RETURNS TEXT LANGUAGE plpgsql AS $$
DECLARE
    compressed_count INTEGER := 0;
    chunk_name TEXT;
BEGIN
    -- 压缩 telemetry
    FOR chunk_name IN
        SELECT show_chunks('telemetry', older_than => p_older_than)
    LOOP
        BEGIN
            PERFORM compress_chunk(chunk_name);
            compressed_count := compressed_count + 1;
        EXCEPTION WHEN OTHERS THEN
            -- 忽略已压缩的 chunk
            NULL;
        END;
    END LOOP;

    RETURN format('Compressed %s chunks older than %s', compressed_count, p_older_than);
END;
$$;

COMMENT ON FUNCTION compress_all_chunks IS '手动压缩指定时间之前的所有数据块';


-- ==================== 8. 统计信息 ====================

-- 创建统计视图
CREATE OR REPLACE VIEW db_optimization_stats AS
SELECT
    'Compression' AS category,
    hypertable_name,
    CASE WHEN compression_enabled THEN 'Enabled' ELSE 'Disabled' END AS status
FROM timescaledb_information.hypertables
UNION ALL
SELECT
    'Continuous Aggregate' AS category,
    view_name AS hypertable_name,
    CASE WHEN finalized THEN 'Active' ELSE 'Pending' END AS status
FROM timescaledb_information.continuous_aggregates;

COMMENT ON VIEW db_optimization_stats IS '数据库优化状态统计视图';


-- ==================== 完成日志 ====================
DO $$
BEGIN
    RAISE NOTICE 'TimescaleDB optimization completed successfully';
    RAISE NOTICE 'Compression policies added for: telemetry, telemetry_1m, telemetry_1h, device_health_snapshot, audit_log';
    RAISE NOTICE 'Retention policies added for: telemetry(90d), telemetry_1m(180d), telemetry_1h(365d), device_health_snapshot(90d), audit_log(180d)';
    RAISE NOTICE 'Continuous aggregates created: telemetry_1m_cagg, telemetry_1h_cagg, telemetry_1d_cagg';
END $$;
