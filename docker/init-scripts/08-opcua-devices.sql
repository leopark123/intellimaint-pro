-- IntelliMaint Pro - 电机预测性维护设备与标签
-- KEPServerEX OPC UA 模拟数据
-- Version: 2.0 - 专注电机预测

-- ==================== 清理旧数据 ====================
DELETE FROM alarm_rule WHERE device_id LIKE 'KEP-%';
DELETE FROM tag WHERE device_id LIKE 'KEP-%';
DELETE FROM device WHERE device_id LIKE 'KEP-%';

-- ==================== 电机设备 ====================

-- 电机 A (主驱动电机)
INSERT INTO device (device_id, name, description, protocol, host, port, enabled, status, location, created_utc, updated_utc) VALUES
('KEP-Motor-A', '电机A-主驱动', '三相异步电机 - 生产线主驱动 (55kW)', 'opcua', 'localhost', 49320, true, 'Online', '车间A-产线1', extract(epoch from now())*1000, extract(epoch from now())*1000)
ON CONFLICT (device_id) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    updated_utc = extract(epoch from now())*1000;

-- 电机 B (辅助电机)
INSERT INTO device (device_id, name, description, protocol, host, port, enabled, status, location, created_utc, updated_utc) VALUES
('KEP-Motor-B', '电机B-辅助驱动', '三相异步电机 - 辅助驱动系统 (37kW)', 'opcua', 'localhost', 49320, true, 'Online', '车间A-产线2', extract(epoch from now())*1000, extract(epoch from now())*1000)
ON CONFLICT (device_id) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    updated_utc = extract(epoch from now())*1000;

-- ==================== 电机 A 标签 ====================

INSERT INTO tag (tag_id, device_id, name, description, tag_group, data_type, unit, address, enabled, created_utc, updated_utc, scan_interval_ms) VALUES
-- 电气参数
('MotorA_Current', 'KEP-Motor-A', '电流', '三相平均电流', '电气监控', 'Float32', 'A', 'ns=2;s=MotorSim.Motor_A.Current', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 100),
('MotorA_Voltage', 'KEP-Motor-A', '电压', '三相平均电压', '电气监控', 'Float32', 'V', 'ns=2;s=MotorSim.Motor_A.Voltage', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 100),
('MotorA_Power', 'KEP-Motor-A', '功率', '实时功率', '电气监控', 'Float32', 'kW', 'ns=2;s=MotorSim.Motor_A.Power', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 100),
-- 机械参数
('MotorA_Vibration', 'KEP-Motor-A', '振动', '振动速度(RMS)', '振动监控', 'Float32', 'mm/s', 'ns=2;s=MotorSim.Motor_A.Vibration', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 100),
('MotorA_Temperature', 'KEP-Motor-A', '温度', '绕组温度', '温度监控', 'Float32', '°C', 'ns=2;s=MotorSim.Motor_A.Temperature', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 100),
('MotorA_Torque', 'KEP-Motor-A', '扭矩', '输出扭矩', '机械监控', 'Float32', 'Nm', 'ns=2;s=MotorSim.Motor_A.Torque', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 100),
('MotorA_Speed', 'KEP-Motor-A', '转速', '实时转速', '机械监控', 'Float32', 'RPM', 'ns=2;s=MotorSim.Motor_A.Speed', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 100),
-- 状态参数
('MotorA_Running', 'KEP-Motor-A', '运行状态', '电机运行中', '状态监控', 'Bool', null, 'ns=2;s=MotorSim.Motor_A.Running', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 100),
('MotorA_Fault', 'KEP-Motor-A', '故障状态', '故障报警', '状态监控', 'Bool', null, 'ns=2;s=MotorSim.Motor_A.Fault', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 100),
('MotorA_Hours', 'KEP-Motor-A', '运行小时', '累计运行时间', '统计信息', 'Float32', 'h', 'ns=2;s=MotorSim.Motor_A.Hours', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 1000)
ON CONFLICT (tag_id) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    updated_utc = extract(epoch from now())*1000;

-- ==================== 电机 B 标签 ====================

INSERT INTO tag (tag_id, device_id, name, description, tag_group, data_type, unit, address, enabled, created_utc, updated_utc, scan_interval_ms) VALUES
-- 电气参数
('MotorB_Current', 'KEP-Motor-B', '电流', '三相平均电流', '电气监控', 'Float32', 'A', 'ns=2;s=MotorSim.Motor_B.Current', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 100),
('MotorB_Voltage', 'KEP-Motor-B', '电压', '三相平均电压', '电气监控', 'Float32', 'V', 'ns=2;s=MotorSim.Motor_B.Voltage', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 100),
('MotorB_Power', 'KEP-Motor-B', '功率', '实时功率', '电气监控', 'Float32', 'kW', 'ns=2;s=MotorSim.Motor_B.Power', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 100),
-- 机械参数
('MotorB_Vibration', 'KEP-Motor-B', '振动', '振动速度(RMS)', '振动监控', 'Float32', 'mm/s', 'ns=2;s=MotorSim.Motor_B.Vibration', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 100),
('MotorB_Temperature', 'KEP-Motor-B', '温度', '绕组温度', '温度监控', 'Float32', '°C', 'ns=2;s=MotorSim.Motor_B.Temperature', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 100),
('MotorB_Torque', 'KEP-Motor-B', '扭矩', '输出扭矩', '机械监控', 'Float32', 'Nm', 'ns=2;s=MotorSim.Motor_B.Torque', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 100),
('MotorB_Speed', 'KEP-Motor-B', '转速', '实时转速', '机械监控', 'Float32', 'RPM', 'ns=2;s=MotorSim.Motor_B.Speed', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 100),
-- 状态参数
('MotorB_Running', 'KEP-Motor-B', '运行状态', '电机运行中', '状态监控', 'Bool', null, 'ns=2;s=MotorSim.Motor_B.Running', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 100),
('MotorB_Fault', 'KEP-Motor-B', '故障状态', '故障报警', '状态监控', 'Bool', null, 'ns=2;s=MotorSim.Motor_B.Fault', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 100),
('MotorB_Hours', 'KEP-Motor-B', '运行小时', '累计运行时间', '统计信息', 'Float32', 'h', 'ns=2;s=MotorSim.Motor_B.Hours', true, extract(epoch from now())*1000, extract(epoch from now())*1000, 1000)
ON CONFLICT (tag_id) DO UPDATE SET
    name = EXCLUDED.name,
    description = EXCLUDED.description,
    updated_utc = extract(epoch from now())*1000;

-- ==================== 电机告警规则 ====================

-- 电机 A 告警规则
INSERT INTO alarm_rule (rule_id, name, description, device_id, tag_id, enabled, condition_type, threshold_value, threshold_high, threshold_low, severity, created_utc, updated_utc) VALUES
-- 温度告警
('rule-motorA-temp-warn', '电机A温度警告', '绕组温度超过70°C', 'KEP-Motor-A', 'MotorA_Temperature', true, 'GreaterThan', 70, null, null, 'Warning', extract(epoch from now())*1000, extract(epoch from now())*1000),
('rule-motorA-temp-critical', '电机A温度严重', '绕组温度超过85°C', 'KEP-Motor-A', 'MotorA_Temperature', true, 'GreaterThan', 85, null, null, 'Critical', extract(epoch from now())*1000, extract(epoch from now())*1000),
-- 振动告警
('rule-motorA-vib-warn', '电机A振动警告', '振动超过4.5mm/s', 'KEP-Motor-A', 'MotorA_Vibration', true, 'GreaterThan', 4.5, null, null, 'Warning', extract(epoch from now())*1000, extract(epoch from now())*1000),
('rule-motorA-vib-critical', '电机A振动严重', '振动超过7mm/s', 'KEP-Motor-A', 'MotorA_Vibration', true, 'GreaterThan', 7, null, null, 'Critical', extract(epoch from now())*1000, extract(epoch from now())*1000),
-- 电流告警
('rule-motorA-current-high', '电机A电流过高', '电流超过额定值120%', 'KEP-Motor-A', 'MotorA_Current', true, 'GreaterThan', 120, null, null, 'Warning', extract(epoch from now())*1000, extract(epoch from now())*1000),
('rule-motorA-current-low', '电机A电流过低', '电流低于额定值50%', 'KEP-Motor-A', 'MotorA_Current', true, 'LessThan', 25, null, null, 'Warning', extract(epoch from now())*1000, extract(epoch from now())*1000),
-- 电压告警
('rule-motorA-voltage-high', '电机A电压过高', '电压超过420V', 'KEP-Motor-A', 'MotorA_Voltage', true, 'GreaterThan', 420, null, null, 'Warning', extract(epoch from now())*1000, extract(epoch from now())*1000),
('rule-motorA-voltage-low', '电机A电压过低', '电压低于360V', 'KEP-Motor-A', 'MotorA_Voltage', true, 'LessThan', 360, null, null, 'Warning', extract(epoch from now())*1000, extract(epoch from now())*1000)
ON CONFLICT (rule_id) DO UPDATE SET updated_utc = extract(epoch from now())*1000;

-- 电机 B 告警规则
INSERT INTO alarm_rule (rule_id, name, description, device_id, tag_id, enabled, condition_type, threshold_value, threshold_high, threshold_low, severity, created_utc, updated_utc) VALUES
-- 温度告警
('rule-motorB-temp-warn', '电机B温度警告', '绕组温度超过70°C', 'KEP-Motor-B', 'MotorB_Temperature', true, 'GreaterThan', 70, null, null, 'Warning', extract(epoch from now())*1000, extract(epoch from now())*1000),
('rule-motorB-temp-critical', '电机B温度严重', '绕组温度超过85°C', 'KEP-Motor-B', 'MotorB_Temperature', true, 'GreaterThan', 85, null, null, 'Critical', extract(epoch from now())*1000, extract(epoch from now())*1000),
-- 振动告警
('rule-motorB-vib-warn', '电机B振动警告', '振动超过4.5mm/s', 'KEP-Motor-B', 'MotorB_Vibration', true, 'GreaterThan', 4.5, null, null, 'Warning', extract(epoch from now())*1000, extract(epoch from now())*1000),
('rule-motorB-vib-critical', '电机B振动严重', '振动超过7mm/s', 'KEP-Motor-B', 'MotorB_Vibration', true, 'GreaterThan', 7, null, null, 'Critical', extract(epoch from now())*1000, extract(epoch from now())*1000),
-- 电流告警
('rule-motorB-current-high', '电机B电流过高', '电流超过额定值120%', 'KEP-Motor-B', 'MotorB_Current', true, 'GreaterThan', 90, null, null, 'Warning', extract(epoch from now())*1000, extract(epoch from now())*1000),
('rule-motorB-current-low', '电机B电流过低', '电流低于额定值50%', 'KEP-Motor-B', 'MotorB_Current', true, 'LessThan', 18, null, null, 'Warning', extract(epoch from now())*1000, extract(epoch from now())*1000),
-- 电压告警
('rule-motorB-voltage-high', '电机B电压过高', '电压超过420V', 'KEP-Motor-B', 'MotorB_Voltage', true, 'GreaterThan', 420, null, null, 'Warning', extract(epoch from now())*1000, extract(epoch from now())*1000),
('rule-motorB-voltage-low', '电机B电压过低', '电压低于360V', 'KEP-Motor-B', 'MotorB_Voltage', true, 'LessThan', 360, null, null, 'Warning', extract(epoch from now())*1000, extract(epoch from now())*1000)
ON CONFLICT (rule_id) DO UPDATE SET updated_utc = extract(epoch from now())*1000;

-- ==================== 验证 ====================
DO $$
DECLARE
    device_count INTEGER;
    tag_count INTEGER;
    rule_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO device_count FROM device WHERE device_id LIKE 'KEP-%';
    SELECT COUNT(*) INTO tag_count FROM tag WHERE device_id LIKE 'KEP-%';
    SELECT COUNT(*) INTO rule_count FROM alarm_rule WHERE device_id LIKE 'KEP-%';

    RAISE NOTICE '========================================';
    RAISE NOTICE '  电机预测性维护设备配置完成';
    RAISE NOTICE '========================================';
    RAISE NOTICE '  电机数量: %', device_count;
    RAISE NOTICE '  标签数量: % (每电机10个)', tag_count;
    RAISE NOTICE '  告警规则: %', rule_count;
    RAISE NOTICE '========================================';
END $$;
