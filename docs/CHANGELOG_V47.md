# IntelliMaint Pro v47 变更日志

## v47 (2025-12-31) - 数据分析功能

### 新增功能

**周期分析 (Cycle Analysis)** - 自动识别和分析设备工作周期

### 核心能力

| 功能 | 说明 |
|------|------|
| **周期检测** | 根据角度数据自动识别工作周期（0°→158°→0°） |
| **特征提取** | 提取周期时长、峰值电流、能耗、平衡比等特征 |
| **基线学习** | 学习电流-角度模型、电机平衡基线 |
| **异常检测** | 基于多维度评分的异常检测 |

### 异常检测类型

| 类型 | 说明 | 检测方式 |
|------|------|----------|
| `over_current` | 过电流 | 峰值电流超过阈值 |
| `motor_imbalance` | 电机不平衡 | 左右电机电流比异常 |
| `cycle_timeout` | 周期超时 | 周期时长过长 |
| `cycle_too_short` | 周期过短 | 周期时长过短 |
| `baseline_deviation` | 基线偏离 | 电流-角度曲线偏离基线 |
| `angle_stall` | 角度停滞 | 最大角度不足 |

### 基线模型

**电流-角度模型** (多项式拟合)
```
Current = a × angle² + b × angle + c
```

**电机平衡模型**
```
正常范围: [mean - 2σ, mean + 2σ]
```

### API 端点

```
周期分析
POST   /api/cycle-analysis/analyze              # 分析周期
GET    /api/cycle-analysis/cycles               # 周期列表
GET    /api/cycle-analysis/cycles/{id}          # 周期详情
GET    /api/cycle-analysis/cycles/recent/{deviceId}     # 最近周期
GET    /api/cycle-analysis/cycles/anomalies/{deviceId}  # 异常周期
GET    /api/cycle-analysis/stats/{deviceId}     # 周期统计
DELETE /api/cycle-analysis/cycles/{id}          # 删除周期

基线管理
GET    /api/baselines/{deviceId}                # 获取基线
POST   /api/baselines/learn                     # 学习所有基线
POST   /api/baselines/learn/current-angle       # 学习电流-角度基线
POST   /api/baselines/learn/motor-balance       # 学习电机平衡基线
DELETE /api/baselines/{deviceId}/{baselineType} # 删除基线
```

### 数据库变更

**Schema Version: 8**

新增表：
- `work_cycle` - 工作周期记录
- `device_baseline` - 设备基线模型

### 新增文件

**后端**:
- `src/Core/Contracts/CycleAnalysis.cs` - 实体定义
- `src/Core/Abstractions/CycleAnalysis.cs` - 服务接口
- `src/Infrastructure/Sqlite/WorkCycleRepository.cs` - 周期仓储
- `src/Infrastructure/Sqlite/DeviceBaselineRepository.cs` - 基线仓储
- `src/Application/Services/CycleAnalysisService.cs` - 周期分析服务
- `src/Application/Services/BaselineLearningService.cs` - 基线学习服务
- `src/Host.Api/Endpoints/CycleAnalysisEndpoints.cs` - API 端点

**前端**:
- `intellimaint-ui/src/types/cycleAnalysis.ts` - 类型定义
- `intellimaint-ui/src/api/cycleAnalysis.ts` - API 调用
- `intellimaint-ui/src/pages/CycleAnalysis/index.tsx` - 分析页面

### 使用示例

**分析翻车机周期**:

```json
POST /api/cycle-analysis/analyze?save=true
{
  "deviceId": "CAR_DUMPER_01",
  "angleTagId": "CD_F[0]",
  "motor1CurrentTagId": "DMP_01_ACTUAL_CURRENT",
  "motor2CurrentTagId": "DMP_02_ACTUAL_CURRENT",
  "startTimeUtc": 1735084800000,
  "endTimeUtc": 1735171200000,
  "angleThreshold": 5,
  "minCycleDuration": 20,
  "maxCycleDuration": 300
}
```

**学习基线**:

```json
POST /api/baselines/learn
{
  "deviceId": "CAR_DUMPER_01",
  "angleTagId": "CD_F[0]",
  "motor1CurrentTagId": "DMP_01_ACTUAL_CURRENT",
  "motor2CurrentTagId": "DMP_02_ACTUAL_CURRENT",
  "startTimeUtc": 1735084800000,
  "endTimeUtc": 1735171200000
}
```

### 工作流程

```
1. 配置设备和标签
2. 选择学习数据时间范围
3. 点击"学习基线"建立正常模型
4. 选择分析时间范围
5. 点击"分析周期"检测异常
6. 查看异常周期详情
```

---

**版本**: v47  
**日期**: 2025-12-31  
**Schema**: v8
