// 采集规则相关类型定义

export interface ConditionItem {
  type: 'tag' | 'duration';
  tagId?: string;
  operator?: 'gt' | 'gte' | 'lt' | 'lte' | 'eq' | 'ne';
  value?: number;
  seconds?: number;
}

export interface ConditionConfig {
  logic: 'AND' | 'OR';
  conditions: ConditionItem[];
}

export interface CollectionConfig {
  tagIds: string[];
  preBufferSeconds: number;
  postBufferSeconds: number;
}

export interface PostAction {
  type: 'saveCycleRecord' | 'runAnomalyDetection' | 'createAlarm' | 'sendNotification';
  alarmSeverity?: number;
  alarmMessage?: string;
}

export interface CollectionRule {
  ruleId: string;
  name: string;
  description?: string;
  deviceId: string;
  enabled: boolean;
  startConditionJson: string;
  stopConditionJson: string;
  collectionConfigJson: string;
  postActionsJson?: string;
  triggerCount: number;
  lastTriggerUtc?: number;
  createdUtc: number;
  updatedUtc: number;
}

export interface CollectionRuleParsed extends Omit<CollectionRule, 'startConditionJson' | 'stopConditionJson' | 'collectionConfigJson' | 'postActionsJson'> {
  startCondition: ConditionConfig;
  stopCondition: ConditionConfig;
  collectionConfig: CollectionConfig;
  postActions?: PostAction[];
}

export interface CollectionSegment {
  id: number;
  ruleId: string;
  deviceId: string;
  startTimeUtc: number;
  endTimeUtc?: number;
  status: SegmentStatus;
  dataPointCount: number;
  metadataJson?: string;
  createdUtc: number;
}

export enum SegmentStatus {
  Collecting = 0,
  Completed = 1,
  Failed = 2,
}

export interface CreateCollectionRuleRequest {
  ruleId: string;
  name: string;
  description?: string;
  deviceId: string;
  enabled?: boolean;
  startCondition: ConditionConfig;
  stopCondition: ConditionConfig;
  collectionConfig: CollectionConfig;
  postActions?: PostAction[];
}

export interface UpdateCollectionRuleRequest {
  name?: string;
  description?: string;
  deviceId?: string;
  enabled?: boolean;
  startCondition?: ConditionConfig;
  stopCondition?: ConditionConfig;
  collectionConfig?: CollectionConfig;
  postActions?: PostAction[];
}

export interface TestConditionRequest {
  condition: ConditionConfig;
  testData?: Record<string, number>;
}

export interface TestConditionResult {
  valid: boolean;
  result?: boolean;
  message?: string;
}

// 辅助函数：解析规则 JSON（支持 PascalCase 和 camelCase）
export function parseCollectionRule(rule: CollectionRule): CollectionRuleParsed {
  const defaultCondition: ConditionConfig = { logic: 'AND', conditions: [] };
  const defaultConfig: CollectionConfig = { tagIds: [], preBufferSeconds: 0, postBufferSeconds: 0 };
  
  let startCondition = defaultCondition;
  let stopCondition = defaultCondition;
  let collectionConfig = defaultConfig;
  let postActions: PostAction[] | undefined;
  
  try {
    const parsed = JSON.parse(rule.startConditionJson || '{}');
    // 支持 PascalCase (Logic/Conditions) 和 camelCase (logic/conditions)
    const logic = parsed.logic || parsed.Logic || 'AND';
    const conditions = parsed.conditions || parsed.Conditions;
    startCondition = {
      logic: logic as 'AND' | 'OR',
      conditions: Array.isArray(conditions) ? conditions.map(normalizeConditionItem) : []
    };
  } catch { /* use default */ }
  
  try {
    const parsed = JSON.parse(rule.stopConditionJson || '{}');
    const logic = parsed.logic || parsed.Logic || 'AND';
    const conditions = parsed.conditions || parsed.Conditions;
    stopCondition = {
      logic: logic as 'AND' | 'OR',
      conditions: Array.isArray(conditions) ? conditions.map(normalizeConditionItem) : []
    };
  } catch { /* use default */ }
  
  try {
    const parsed = JSON.parse(rule.collectionConfigJson || '{}');
    collectionConfig = {
      tagIds: Array.isArray(parsed.tagIds || parsed.TagIds) ? (parsed.tagIds || parsed.TagIds) : [],
      preBufferSeconds: parsed.preBufferSeconds ?? parsed.PreBufferSeconds ?? 0,
      postBufferSeconds: parsed.postBufferSeconds ?? parsed.PostBufferSeconds ?? 0
    };
  } catch { /* use default */ }
  
  try {
    postActions = rule.postActionsJson ? JSON.parse(rule.postActionsJson) : undefined;
  } catch { /* use default */ }
  
  return {
    ...rule,
    startCondition,
    stopCondition,
    collectionConfig,
    postActions,
  };
}

// 辅助函数：标准化条件项（支持 PascalCase）
function normalizeConditionItem(item: any): ConditionItem {
  return {
    type: (item.type || item.Type || 'tag') as 'tag' | 'duration',
    tagId: item.tagId || item.TagId,
    operator: item.operator || item.Operator,
    value: item.value ?? item.Value,
    seconds: item.seconds ?? item.Seconds,
  };
}

// 辅助函数：获取状态文本
export function getSegmentStatusText(status: SegmentStatus): string {
  switch (status) {
    case SegmentStatus.Collecting:
      return '采集中';
    case SegmentStatus.Completed:
      return '已完成';
    case SegmentStatus.Failed:
      return '失败';
    default:
      return '未知';
  }
}

// 辅助函数：获取状态颜色
export function getSegmentStatusColor(status: SegmentStatus): string {
  switch (status) {
    case SegmentStatus.Collecting:
      return 'processing';
    case SegmentStatus.Completed:
      return 'success';
    case SegmentStatus.Failed:
      return 'error';
    default:
      return 'default';
  }
}

// 辅助函数：获取操作符文本
export function getOperatorText(op: string): string {
  switch (op) {
    case 'gt':
      return '>';
    case 'gte':
      return '≥';
    case 'lt':
      return '<';
    case 'lte':
      return '≤';
    case 'eq':
      return '=';
    case 'ne':
      return '≠';
    default:
      return op;
  }
}
