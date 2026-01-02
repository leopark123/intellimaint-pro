// 周期分析相关类型定义

export interface WorkCycle {
  id: number;
  deviceId: string;
  segmentId?: number;
  startTimeUtc: number;
  endTimeUtc: number;
  durationSeconds: number;
  maxAngle: number;
  motor1PeakCurrent: number;
  motor2PeakCurrent: number;
  motor1AvgCurrent: number;
  motor2AvgCurrent: number;
  motor1Energy: number;
  motor2Energy: number;
  motorBalanceRatio: number;
  baselineDeviationPercent: number;
  anomalyScore: number;
  isAnomaly: boolean;
  anomalyType?: string;
  detailsJson?: string;
  createdUtc: number;
}

export interface CycleAnalysisResult {
  cycleCount: number;
  anomalyCycleCount: number;
  cycles: WorkCycle[];
  summary?: CycleStatsSummary;
}

export interface CycleStatsSummary {
  avgDuration: number;
  avgMotor1PeakCurrent: number;
  avgMotor2PeakCurrent: number;
  avgMotorBalanceRatio: number;
  avgAnomalyScore: number;
}

export interface CurrentAngleModel {
  motorTagId: string;
  coefficients: number[];
  rSquared: number;
  angleRanges?: Record<number, CurrentRange>;
}

export interface CurrentRange {
  mean: number;
  std: number;
  min: number;
  max: number;
}

export interface MotorBalanceModel {
  meanRatio: number;
  stdRatio: number;
  lowerBound: number;
  upperBound: number;
}

export interface DeviceBaseline {
  deviceId: string;
  baselineType: string;
  sampleCount: number;
  updatedUtc: number;
  model: unknown;
}

export interface AnalyzeCyclesRequest {
  deviceId: string;
  angleTagId: string;
  motor1CurrentTagId: string;
  motor2CurrentTagId: string;
  startTimeUtc: number;
  endTimeUtc: number;
  angleThreshold?: number;
  minCycleDuration?: number;
  maxCycleDuration?: number;
}

export interface LearnBaselinesRequest {
  deviceId: string;
  angleTagId: string;
  motor1CurrentTagId: string;
  motor2CurrentTagId: string;
  startTimeUtc: number;
  endTimeUtc: number;
}

export interface WorkCycleQuery {
  deviceId?: string;
  startTime?: number;
  endTime?: number;
  isAnomaly?: boolean;
  anomalyType?: string;
  limit?: number;
}

// 异常类型常量
export const AnomalyTypes = {
  OVER_CURRENT: 'over_current',
  MOTOR_IMBALANCE: 'motor_imbalance',
  CYCLE_TIMEOUT: 'cycle_timeout',
  CYCLE_TOO_SHORT: 'cycle_too_short',
  BASELINE_DEVIATION: 'baseline_deviation',
  ANGLE_STALL: 'angle_stall',
} as const;

// 异常类型显示文本
export const AnomalyTypeLabels: Record<string, string> = {
  [AnomalyTypes.OVER_CURRENT]: '过电流',
  [AnomalyTypes.MOTOR_IMBALANCE]: '电机不平衡',
  [AnomalyTypes.CYCLE_TIMEOUT]: '周期超时',
  [AnomalyTypes.CYCLE_TOO_SHORT]: '周期过短',
  [AnomalyTypes.BASELINE_DEVIATION]: '基线偏离',
  [AnomalyTypes.ANGLE_STALL]: '角度停滞',
};

// 获取异常类型颜色
export function getAnomalyTypeColor(type: string): string {
  switch (type) {
    case AnomalyTypes.OVER_CURRENT:
      return 'red';
    case AnomalyTypes.MOTOR_IMBALANCE:
      return 'orange';
    case AnomalyTypes.CYCLE_TIMEOUT:
      return 'gold';
    case AnomalyTypes.CYCLE_TOO_SHORT:
      return 'purple';
    case AnomalyTypes.BASELINE_DEVIATION:
      return 'blue';
    case AnomalyTypes.ANGLE_STALL:
      return 'magenta';
    default:
      return 'default';
  }
}

// 格式化周期时长
export function formatDuration(seconds: number): string {
  if (seconds < 60) {
    return `${seconds.toFixed(1)}秒`;
  }
  const mins = Math.floor(seconds / 60);
  const secs = seconds % 60;
  return `${mins}分${secs.toFixed(0)}秒`;
}

// 格式化异常分数
export function formatAnomalyScore(score: number): string {
  if (score < 20) return '正常';
  if (score < 40) return '轻微异常';
  if (score < 60) return '中度异常';
  if (score < 80) return '严重异常';
  return '极度异常';
}

// 获取异常分数颜色
export function getAnomalyScoreColor(score: number): string {
  if (score < 20) return 'green';
  if (score < 40) return 'lime';
  if (score < 60) return 'gold';
  if (score < 80) return 'orange';
  return 'red';
}
