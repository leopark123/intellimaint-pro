-- IntelliMaint Pro - Motor Fault Prediction Schema
-- Version: 1.0 (v64)
-- Purpose: Create tables for motor fault prediction module
-- Applied after: 05-schema-sync.sql

-- ==================== 1. Motor Model Table ====================

CREATE TABLE IF NOT EXISTS motor_model (
    model_id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    description TEXT,
    motor_type INTEGER NOT NULL DEFAULT 0,  -- 0=Induction, 1=Synchronous, 2=DC, 3=PMSM, 4=BLDC
    rated_power DOUBLE PRECISION,           -- kW
    rated_voltage DOUBLE PRECISION,         -- V
    rated_current DOUBLE PRECISION,         -- A
    rated_speed DOUBLE PRECISION,           -- RPM
    rated_frequency DOUBLE PRECISION,       -- Hz
    pole_pairs INTEGER,
    vfd_model TEXT,
    bearing_model TEXT,
    bearing_rolling_elements INTEGER,
    bearing_ball_diameter DOUBLE PRECISION,   -- mm
    bearing_pitch_diameter DOUBLE PRECISION,  -- mm
    bearing_contact_angle DOUBLE PRECISION,   -- degrees
    created_utc BIGINT NOT NULL,
    updated_utc BIGINT,
    created_by TEXT
);

COMMENT ON TABLE motor_model IS 'User-defined motor model templates for fault prediction';
COMMENT ON COLUMN motor_model.motor_type IS '0=InductionMotor, 1=SynchronousMotor, 2=DCMotor, 3=PMSyncMotor, 4=BrushlessDC';

-- ==================== 2. Motor Instance Table ====================

CREATE TABLE IF NOT EXISTS motor_instance (
    instance_id TEXT PRIMARY KEY,
    model_id TEXT NOT NULL REFERENCES motor_model(model_id) ON DELETE CASCADE,
    device_id TEXT NOT NULL REFERENCES device(device_id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    location TEXT,
    install_date TEXT,
    operating_hours DOUBLE PRECISION,
    asset_number TEXT,
    diagnosis_enabled BOOLEAN NOT NULL DEFAULT true,
    created_utc BIGINT NOT NULL,
    updated_utc BIGINT
);

CREATE INDEX IF NOT EXISTS idx_motor_instance_model ON motor_instance(model_id);
CREATE INDEX IF NOT EXISTS idx_motor_instance_device ON motor_instance(device_id);

COMMENT ON TABLE motor_instance IS 'Motor instances bound to specific devices for monitoring';

-- ==================== 3. Parameter Mapping Table ====================

CREATE TABLE IF NOT EXISTS motor_parameter_mapping (
    mapping_id TEXT PRIMARY KEY,
    instance_id TEXT NOT NULL REFERENCES motor_instance(instance_id) ON DELETE CASCADE,
    parameter INTEGER NOT NULL,  -- MotorParameter enum value
    tag_id TEXT NOT NULL REFERENCES tag(tag_id) ON DELETE CASCADE,
    scale_factor DOUBLE PRECISION NOT NULL DEFAULT 1.0,
    offset_value DOUBLE PRECISION NOT NULL DEFAULT 0.0,
    used_for_diagnosis BOOLEAN NOT NULL DEFAULT true
);

CREATE INDEX IF NOT EXISTS idx_motor_param_mapping_instance ON motor_parameter_mapping(instance_id);
CREATE UNIQUE INDEX IF NOT EXISTS idx_motor_param_mapping_unique ON motor_parameter_mapping(instance_id, parameter);

COMMENT ON TABLE motor_parameter_mapping IS 'Maps VFD tags to standard motor parameters';
COMMENT ON COLUMN motor_parameter_mapping.parameter IS 'Standard motor parameter type (MotorParameter enum)';

-- ==================== 4. Operation Mode Table ====================

CREATE TABLE IF NOT EXISTS operation_mode (
    mode_id TEXT PRIMARY KEY,
    instance_id TEXT NOT NULL REFERENCES motor_instance(instance_id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    description TEXT,
    trigger_tag_id TEXT,
    trigger_min_value DOUBLE PRECISION,
    trigger_max_value DOUBLE PRECISION,
    min_duration_ms INTEGER NOT NULL DEFAULT 0,
    max_duration_ms INTEGER NOT NULL DEFAULT 0,
    priority INTEGER NOT NULL DEFAULT 0,
    enabled BOOLEAN NOT NULL DEFAULT true,
    created_utc BIGINT NOT NULL,
    updated_utc BIGINT
);

CREATE INDEX IF NOT EXISTS idx_operation_mode_instance ON operation_mode(instance_id);
CREATE INDEX IF NOT EXISTS idx_operation_mode_enabled ON operation_mode(instance_id) WHERE enabled = true;

COMMENT ON TABLE operation_mode IS 'Operation modes with different normal parameter ranges (e.g., empty tilt, full tilt)';

-- ==================== 5. Baseline Profile Table ====================

CREATE TABLE IF NOT EXISTS baseline_profile (
    baseline_id TEXT PRIMARY KEY,
    mode_id TEXT NOT NULL REFERENCES operation_mode(mode_id) ON DELETE CASCADE,
    parameter INTEGER NOT NULL,
    mean DOUBLE PRECISION NOT NULL,
    std_dev DOUBLE PRECISION NOT NULL,
    min_value DOUBLE PRECISION NOT NULL,
    max_value DOUBLE PRECISION NOT NULL,
    percentile_05 DOUBLE PRECISION,
    percentile_95 DOUBLE PRECISION,
    median DOUBLE PRECISION,
    frequency_profile_json JSONB,  -- FFT features for current analysis
    sample_count INTEGER NOT NULL,
    learned_from_utc BIGINT NOT NULL,
    learned_to_utc BIGINT NOT NULL,
    confidence_level DOUBLE PRECISION,
    version INTEGER NOT NULL DEFAULT 1,
    created_utc BIGINT NOT NULL,
    updated_utc BIGINT
);

CREATE INDEX IF NOT EXISTS idx_baseline_mode ON baseline_profile(mode_id);
CREATE UNIQUE INDEX IF NOT EXISTS idx_baseline_mode_param ON baseline_profile(mode_id, parameter);

COMMENT ON TABLE baseline_profile IS 'Learned baseline profiles for each operation mode and parameter';
COMMENT ON COLUMN baseline_profile.frequency_profile_json IS 'FFT frequency domain features for MCSA';

-- ==================== 6. Diagnosis Record Table ====================

CREATE TABLE IF NOT EXISTS motor_diagnosis_record (
    record_id TEXT PRIMARY KEY,
    instance_id TEXT NOT NULL REFERENCES motor_instance(instance_id) ON DELETE CASCADE,
    mode_id TEXT,
    diagnosis_type INTEGER NOT NULL,  -- 0=baseline, 1=ml, 2=deep
    status INTEGER NOT NULL,          -- DiagnosisStatus enum
    health_score DOUBLE PRECISION,
    fault_type INTEGER,               -- MotorFaultType enum
    confidence DOUBLE PRECISION,
    deviations_json JSONB,
    details_json JSONB,
    created_utc BIGINT NOT NULL
);

-- Use TimescaleDB hypertable for diagnosis records (time-series data)
SELECT create_hypertable('motor_diagnosis_record', by_range('created_utc', 86400000), if_not_exists => true);

CREATE INDEX IF NOT EXISTS idx_diagnosis_instance_ts ON motor_diagnosis_record(instance_id, created_utc DESC);
CREATE INDEX IF NOT EXISTS idx_diagnosis_fault_type ON motor_diagnosis_record(instance_id, fault_type) WHERE fault_type IS NOT NULL;

COMMENT ON TABLE motor_diagnosis_record IS 'Historical diagnosis records for trending and analysis';
COMMENT ON COLUMN motor_diagnosis_record.diagnosis_type IS '0=BaselineComparison, 1=MachineLearning, 2=DeepLearning';
COMMENT ON COLUMN motor_diagnosis_record.fault_type IS 'MotorFaultType: 0=Normal, 1=BearingWear, 2=RotorBar, 3=StatorWinding, etc.';

-- ==================== 7. Compression Policy ====================

-- Compress diagnosis records older than 7 days
SELECT add_compression_policy('motor_diagnosis_record', INTERVAL '7 days', if_not_exists => true);

-- ==================== 8. Retention Policy ====================

-- Retain diagnosis records for 365 days
SELECT add_retention_policy('motor_diagnosis_record', INTERVAL '365 days', if_not_exists => true);

-- ==================== 9. Record Schema Version ====================

INSERT INTO schema_version (version, applied_utc)
VALUES (5, EXTRACT(EPOCH FROM NOW()) * 1000)
ON CONFLICT (version) DO UPDATE SET applied_utc = EXTRACT(EPOCH FROM NOW()) * 1000;

-- ==================== 10. Completion Log ====================

DO $$
BEGIN
    RAISE NOTICE '========================================';
    RAISE NOTICE 'Motor Fault Prediction Schema Complete';
    RAISE NOTICE '========================================';
    RAISE NOTICE 'Created tables:';
    RAISE NOTICE '  - motor_model (user-defined motor templates)';
    RAISE NOTICE '  - motor_instance (bound to devices)';
    RAISE NOTICE '  - motor_parameter_mapping (VFD tag mapping)';
    RAISE NOTICE '  - operation_mode (different operation contexts)';
    RAISE NOTICE '  - baseline_profile (learned baselines)';
    RAISE NOTICE '  - motor_diagnosis_record (diagnosis history)';
    RAISE NOTICE '';
    RAISE NOTICE 'Schema version: 5';
    RAISE NOTICE '========================================';
END $$;
