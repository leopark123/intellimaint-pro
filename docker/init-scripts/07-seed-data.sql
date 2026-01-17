-- IntelliMaint Pro - Seed Data for Demo/Testing
-- Version: 1.0 (v65)
-- Purpose: Initialize demo data for fresh deployments
-- Applied after: 06-motor-fault-prediction.sql

-- ==================== 1. 默认用户 ====================

-- 密码哈希: admin123 (bcrypt)
INSERT INTO "user" (user_id, username, password_hash, role, display_name, enabled, created_utc, must_change_password)
VALUES
    ('admin0000000001', 'admin', '$2a$12$lY3.2k3Rr1wDptBU/Mtl.uNzaCQd/LL99xqntple921fppPgMQp1u', 'Admin', '系统管理员', true, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, true),
    ('operator00000001', 'operator', '$2a$12$lY3.2k3Rr1wDptBU/Mtl.uNzaCQd/LL99xqntple921fppPgMQp1u', 'Operator', '操作员', true, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, true),
    ('viewer0000000001', 'viewer', '$2a$12$lY3.2k3Rr1wDptBU/Mtl.uNzaCQd/LL99xqntple921fppPgMQp1u', 'Viewer', '观察者', true, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, true)
ON CONFLICT (user_id) DO NOTHING;

-- ==================== 2. 演示设备 ====================

INSERT INTO device (device_id, name, description, protocol, host, port, enabled, status, location, created_utc, updated_utc)
VALUES
    ('SIM-PLC-001', 'PLC模拟器', '用于测试的PLC模拟设备', 'LibPlcTag', 'localhost', 44818, true, 'Online', '车间A',
     EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('Motor-001', '电机-001', '主传送带电机', 'OpcUa', 'localhost', 4840, true, 'Online', '车间B',
     EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('Sim_Device', '仿真设备', '系统仿真设备', 'Internal', 'localhost', 0, true, 'Online', '测试区',
     EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000)
ON CONFLICT (device_id) DO NOTHING;

-- ==================== 3. 演示标签 ====================

-- data_type: Bool=1, Float32=10, Float64=11
INSERT INTO tag (tag_id, device_id, name, description, data_type, unit, address, enabled, created_utc, updated_utc)
VALUES
    -- SIM-PLC-001 标签
    ('Motor1_Current', 'SIM-PLC-001', '电机电流', '主电机运行电流', 10, 'A', 'Motor1.Current', true,
     EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('Motor1_Running', 'SIM-PLC-001', '电机运行状态', '电机是否运行', 1, '', 'Motor1.Running', true,
     EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('Motor1_Speed', 'SIM-PLC-001', '电机转速', '电机当前转速', 10, 'RPM', 'Motor1.Speed', true,
     EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('Motor1_Temp', 'SIM-PLC-001', '电机温度', '电机绕组温度', 10, '℃', 'Motor1.Temp', true,
     EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    -- Motor-001 标签
    ('Current', 'Motor-001', '电流', '电机电流', 10, 'A', 'ns=2;s=Current', true,
     EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('Running', 'Motor-001', '运行状态', '电机运行中', 1, '', 'ns=2;s=Running', true,
     EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('Torque', 'Motor-001', '扭矩', '电机输出扭矩', 10, 'Nm', 'ns=2;s=Torque', true,
     EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('Voltage', 'Motor-001', '电压', '电机电压', 10, 'V', 'ns=2;s=Voltage', true,
     EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    -- Sim_Device 标签
    ('Ramp_Value', 'Sim_Device', '斜坡值', '仿真斜坡信号', 11, '', 'Simulation.Ramp', true,
     EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000)
ON CONFLICT (tag_id) DO NOTHING;

-- ==================== 4. 告警规则 ====================

INSERT INTO alarm_rule (rule_id, name, description, device_id, tag_id, rule_type, condition_type, threshold, severity, enabled, debounce_ms, created_utc, updated_utc)
VALUES
    ('rule-temp-high', '电机温度过高', '温度超过80度告警', 'SIM-PLC-001', 'Motor1_Temp', 0, 0, 80, 2, true, 5000,
     EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('rule-temp-critical', '电机温度严重过高', '温度超过95度严重告警', 'SIM-PLC-001', 'Motor1_Temp', 0, 0, 95, 3, true, 5000,
     EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('rule-current-high', '电机电流过高', '电流超过15A告警', 'SIM-PLC-001', 'Motor1_Current', 0, 0, 15, 2, true, 5000,
     EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('rule-speed-low', '电机转速过低', '转速低于100RPM告警', 'SIM-PLC-001', 'Motor1_Speed', 0, 1, 100, 1, true, 5000,
     EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('rule-motor001-current', '电机001电流异常', '电流超过20A告警', 'Motor-001', 'Current', 0, 0, 20, 2, true, 5000,
     EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('rule-motor001-voltage', '电机001电压异常', '电压低于350V告警', 'Motor-001', 'Voltage', 0, 1, 350, 1, true, 5000,
     EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000)
ON CONFLICT (rule_id) DO NOTHING;

-- ==================== 5. 电机型号 ====================

INSERT INTO motor_model (model_id, name, description, motor_type, rated_power, rated_voltage, rated_current, rated_speed, rated_frequency, pole_pairs, created_utc)
VALUES
    ('model-y2-7.5kw', 'Y2-132M-4', '7.5kW三相异步电动机', 0, 7.5, 380, 15.4, 1440, 50, 2, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('model-y2-11kw', 'Y2-160M-4', '11kW三相异步电动机', 0, 11, 380, 22.6, 1460, 50, 2, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('model-y2-15kw', 'Y2-160L-4', '15kW三相异步电动机', 0, 15, 380, 30.5, 1460, 50, 2, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000)
ON CONFLICT (model_id) DO NOTHING;

-- ==================== 6. 电机实例 ====================

INSERT INTO motor_instance (instance_id, model_id, device_id, name, location, diagnosis_enabled, created_utc)
VALUES
    ('motor-inst-001', 'model-y2-7.5kw', 'Motor-001', '传送带主电机', '车间B-L1', true, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('motor-inst-002', 'model-y2-11kw', 'SIM-PLC-001', 'PLC控制电机', '车间A-L2', true, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000)
ON CONFLICT (instance_id) DO NOTHING;

-- ==================== 7. 参数映射 ====================

INSERT INTO motor_parameter_mapping (mapping_id, instance_id, parameter, tag_id, scale_factor, offset_value, used_for_diagnosis)
VALUES
    -- motor-inst-001 (Motor-001 设备)
    ('map-m1-current', 'motor-inst-001', 3, 'Current', 1.0, 0.0, true),
    ('map-m1-voltage', 'motor-inst-001', 7, 'Voltage', 1.0, 0.0, true),
    ('map-m1-torque', 'motor-inst-001', 20, 'Torque', 1.0, 0.0, true),
    ('map-m1-running', 'motor-inst-001', 34, 'Running', 1.0, 0.0, false),
    -- motor-inst-002 (SIM-PLC-001 设备)
    ('map-m2-current', 'motor-inst-002', 3, 'Motor1_Current', 1.0, 0.0, true),
    ('map-m2-speed', 'motor-inst-002', 21, 'Motor1_Speed', 1.0, 0.0, true),
    ('map-m2-temp', 'motor-inst-002', 40, 'Motor1_Temp', 1.0, 0.0, true),
    ('map-m2-running', 'motor-inst-002', 34, 'Motor1_Running', 1.0, 0.0, false)
ON CONFLICT (mapping_id) DO NOTHING;

-- ==================== 8. 操作模式 ====================

INSERT INTO operation_mode (mode_id, instance_id, name, description, trigger_tag_id, trigger_min_value, trigger_max_value, min_duration_ms, max_duration_ms, priority, enabled, created_utc)
VALUES
    ('mode-m1-normal', 'motor-inst-001', '正常运行', '电机正常运行模式', 'Running', 1, 1, 1000, 0, 0, true, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('mode-m2-normal', 'motor-inst-002', '正常运行', '电机正常运行模式', 'Motor1_Running', 1, 1, 1000, 0, 0, true, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000)
ON CONFLICT (mode_id) DO NOTHING;

-- ==================== 9. 基线数据 ====================

INSERT INTO baseline_profile (baseline_id, mode_id, parameter, mean, std_dev, min_value, max_value, percentile_05, percentile_95, median, sample_count, learned_from_utc, learned_to_utc, confidence_level, version, created_utc)
VALUES
    -- mode-m1-normal 基线
    ('bl-m1-current', 'mode-m1-normal', 3, 12.5, 1.5, 10.0, 15.0, 10.5, 14.5, 12.5, 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, 0.95, 1, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('bl-m1-voltage', 'mode-m1-normal', 7, 380.0, 10.0, 360.0, 400.0, 365.0, 395.0, 380.0, 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, 0.95, 1, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('bl-m1-torque', 'mode-m1-normal', 20, 50.0, 5.0, 40.0, 60.0, 42.0, 58.0, 50.0, 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, 0.95, 1, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    -- mode-m2-normal 基线
    ('bl-m2-current', 'mode-m2-normal', 3, 10.0, 1.2, 8.0, 12.0, 8.5, 11.5, 10.0, 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, 0.95, 1, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('bl-m2-speed', 'mode-m2-normal', 21, 1440.0, 50.0, 1340.0, 1540.0, 1360.0, 1520.0, 1440.0, 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, 0.95, 1, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('bl-m2-temp', 'mode-m2-normal', 40, 45.0, 5.0, 35.0, 55.0, 37.0, 53.0, 45.0, 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, 0.95, 1, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000)
ON CONFLICT (baseline_id) DO NOTHING;

-- ==================== 10. 健康基线 ====================

INSERT INTO health_baseline (device_id, baseline_data, created_utc, updated_utc, sample_count, learning_hours)
VALUES
    ('Motor-001', '{"healthScore": 85.5, "status": "Normal"}', EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, 1000, 24),
    ('SIM-PLC-001', '{"healthScore": 92.3, "status": "Normal"}', EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, 2000, 48)
ON CONFLICT (device_id) DO NOTHING;

-- ==================== 11. 采集规则 ====================

INSERT INTO collection_rule (rule_id, name, description, device_id, tag_ids, trigger_condition, duration_ms, pre_trigger_ms, post_trigger_ms, enabled, created_utc, updated_utc)
VALUES
    ('collect-motor001', 'Motor-001采集规则', '电机001数据采集', 'Motor-001', ARRAY['Current', 'Running', 'Torque', 'Voltage'], 'Running == true', 60000, 5000, 5000, true, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000),
    ('collect-plc001', 'PLC模拟器采集规则', 'PLC模拟器数据采集', 'SIM-PLC-001', ARRAY['Motor1_Current', 'Motor1_Running', 'Motor1_Speed', 'Motor1_Temp'], 'Motor1_Running == true', 60000, 5000, 5000, true, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000, EXTRACT(EPOCH FROM NOW())::BIGINT * 1000)
ON CONFLICT (rule_id) DO NOTHING;

-- ==================== 12. 完成日志 ====================

DO $$
BEGIN
    RAISE NOTICE '========================================';
    RAISE NOTICE 'Seed Data Initialization Complete';
    RAISE NOTICE '========================================';
    RAISE NOTICE 'Created:';
    RAISE NOTICE '  - 3 users (admin, operator, viewer)';
    RAISE NOTICE '  - 3 devices';
    RAISE NOTICE '  - 9 tags';
    RAISE NOTICE '  - 6 alarm rules';
    RAISE NOTICE '  - 3 motor models';
    RAISE NOTICE '  - 2 motor instances';
    RAISE NOTICE '  - 8 parameter mappings';
    RAISE NOTICE '  - 2 operation modes';
    RAISE NOTICE '  - 6 baseline profiles';
    RAISE NOTICE '  - 2 health baselines';
    RAISE NOTICE '  - 2 collection rules';
    RAISE NOTICE '';
    RAISE NOTICE 'Default credentials:';
    RAISE NOTICE '  admin/admin123, operator/admin123, viewer/admin123';
    RAISE NOTICE '========================================';
END $$;
