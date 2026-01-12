# Edge Phase 1 优化方案 (修订版)

> 版本: v2.0
> 修订说明:
> - 本地存储使用文件滚动，不依赖 SQLite
> - 配置通过 API 管理，前端可视化设置
> - Edge 动态拉取配置，支持热更新

---

## 一、整体架构

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                                  前端 UI                                     │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                      Edge 配置管理页面                                │   │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────────┐  │   │
│  │  │  预处理配置   │  │  断网续传配置 │  │  标签级配置              │  │   │
│  │  │  - 死区阈值   │  │  - 存储容量   │  │  - 每个标签独立配置     │  │   │
│  │  │  - 采样间隔   │  │  - 保留天数   │  │  - 优先级设置           │  │   │
│  │  │  - 异常检测   │  │  - 压缩算法   │  │  - 批量导入/导出        │  │   │
│  │  └──────────────┘  └──────────────┘  └──────────────────────────┘  │   │
│  └─────────────────────────────────────────┬───────────────────────────┘   │
└────────────────────────────────────────────┼───────────────────────────────┘
                                             │ REST API
┌────────────────────────────────────────────▼───────────────────────────────┐
│                                  Host.Api                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                    EdgeConfigEndpoints                               │   │
│  │  GET  /api/edge-config/{edgeId}         获取配置                     │   │
│  │  PUT  /api/edge-config/{edgeId}         更新配置                     │   │
│  │  GET  /api/edge-config/{edgeId}/tags    获取标签配置列表             │   │
│  │  PUT  /api/edge-config/{edgeId}/tags    批量更新标签配置             │   │
│  │  POST /api/edge-config/{edgeId}/sync    通知Edge同步配置             │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                     │                                       │
│                          ┌──────────▼──────────┐                           │
│                          │  TimescaleDB/SQLite │                           │
│                          │  edge_config 表     │                           │
│                          │  tag_processing 表  │                           │
│                          └─────────────────────┘                           │
└────────────────────────────────────────────────────────────────────────────┘
                                             │
                    ┌────────────────────────┼────────────────────────┐
                    │ 定期拉取配置 (30s)      │ 配置变更通知 (WebHook) │
                    ▼                        ▼                        │
┌─────────────────────────────────────────────────────────────────────────────┐
│                                  Host.Edge                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                      ConfigSyncService                               │   │
│  │  - 定期从 API 拉取配置                                               │   │
│  │  - 检测配置变更                                                      │   │
│  │  - 热更新运行中的服务                                                │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                     │                                       │
│  ┌──────────────────────────────────▼──────────────────────────────────┐   │
│  │                      EdgeDataProcessor                               │   │
│  │  [死区过滤] → [采样控制] → [异常检测] → [批量聚合]                   │   │
│  └──────────────────────────────────┬──────────────────────────────────┘   │
│                                     │                                       │
│  ┌──────────────────────────────────▼──────────────────────────────────┐   │
│  │                      StoreAndForwardService                          │   │
│  │                                                                      │   │
│  │   在线模式 ───────────────────▶ 压缩发送到 API                       │   │
│  │       │                                                              │   │
│  │       └─ 发送失败 ─┐                                                 │   │
│  │                    ▼                                                 │   │
│  │   离线模式 ───▶ FileRollingStore (文件滚动存储)                      │   │
│  │                    │                                                 │   │
│  │                    ▼                                                 │   │
│  │   ┌────────────────────────────────────────────────────────────┐    │   │
│  │   │  data/outbox/                                              │    │   │
│  │   │  ├── batch_001.bin.gz  (压缩的批量数据)                    │    │   │
│  │   │  ├── batch_002.bin.gz                                      │    │   │
│  │   │  ├── batch_003.bin.gz                                      │    │   │
│  │   │  └── index.json        (索引文件)                          │    │   │
│  │   └────────────────────────────────────────────────────────────┘    │   │
│  │                    │                                                 │   │
│  │   网络恢复 ────────┴──▶ DrainService (按序上传)                      │   │
│  └──────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 二、数据模型设计

### 2.1 Edge 配置表 (后端)

```sql
-- edge_config: Edge 节点全局配置
CREATE TABLE edge_config (
    edge_id VARCHAR(64) PRIMARY KEY,
    name VARCHAR(128) NOT NULL,
    description TEXT,

    -- 预处理配置
    processing_enabled BOOLEAN DEFAULT true,
    default_deadband DOUBLE DEFAULT 0.01,
    default_deadband_percent DOUBLE DEFAULT 0.5,
    default_min_interval_ms INTEGER DEFAULT 1000,
    force_upload_interval_ms INTEGER DEFAULT 60000,
    outlier_enabled BOOLEAN DEFAULT true,
    outlier_sigma_threshold DOUBLE DEFAULT 4.0,
    outlier_action VARCHAR(16) DEFAULT 'Mark',

    -- 断网续传配置
    store_forward_enabled BOOLEAN DEFAULT true,
    max_store_size_mb INTEGER DEFAULT 1000,
    retention_days INTEGER DEFAULT 7,
    compression_enabled BOOLEAN DEFAULT true,
    compression_algorithm VARCHAR(16) DEFAULT 'Gzip',

    -- 网络配置
    health_check_interval_ms INTEGER DEFAULT 5000,
    health_check_timeout_ms INTEGER DEFAULT 3000,
    offline_threshold INTEGER DEFAULT 3,
    send_batch_size INTEGER DEFAULT 500,
    send_interval_ms INTEGER DEFAULT 500,

    -- 元数据
    created_utc BIGINT NOT NULL,
    updated_utc BIGINT,
    updated_by VARCHAR(64)
);

-- tag_processing_config: 标签级预处理配置
CREATE TABLE tag_processing_config (
    id SERIAL PRIMARY KEY,
    edge_id VARCHAR(64) NOT NULL,
    tag_id VARCHAR(128) NOT NULL,

    -- 预处理配置 (NULL 表示使用默认值)
    deadband DOUBLE,
    deadband_percent DOUBLE,
    min_interval_ms INTEGER,
    bypass BOOLEAN DEFAULT false,      -- true: 跳过预处理，原样上传
    priority INTEGER DEFAULT 0,        -- 优先级 (值越高越优先)

    -- 元数据
    description TEXT,
    created_utc BIGINT NOT NULL,
    updated_utc BIGINT,

    UNIQUE(edge_id, tag_id)
);

CREATE INDEX idx_tag_processing_edge ON tag_processing_config(edge_id);
```

### 2.2 配置 DTO

```csharp
// Core/Contracts/EdgeConfig.cs
namespace IntelliMaint.Core.Contracts;

/// <summary>
/// Edge 节点配置
/// </summary>
public record EdgeConfigDto
{
    public string EdgeId { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }

    // 预处理配置
    public ProcessingConfigDto Processing { get; init; } = new();

    // 断网续传配置
    public StoreForwardConfigDto StoreForward { get; init; } = new();

    // 网络配置
    public NetworkConfigDto Network { get; init; } = new();

    // 元数据
    public long CreatedUtc { get; init; }
    public long? UpdatedUtc { get; init; }
    public string? UpdatedBy { get; init; }
}

public record ProcessingConfigDto
{
    public bool Enabled { get; init; } = true;
    public double DefaultDeadband { get; init; } = 0.01;
    public double DefaultDeadbandPercent { get; init; } = 0.5;
    public int DefaultMinIntervalMs { get; init; } = 1000;
    public int ForceUploadIntervalMs { get; init; } = 60000;

    // 异常检测
    public bool OutlierEnabled { get; init; } = true;
    public double OutlierSigmaThreshold { get; init; } = 4.0;
    public string OutlierAction { get; init; } = "Mark"; // Drop/Mark/Pass
}

public record StoreForwardConfigDto
{
    public bool Enabled { get; init; } = true;
    public int MaxStoreSizeMB { get; init; } = 1000;
    public int RetentionDays { get; init; } = 7;
    public bool CompressionEnabled { get; init; } = true;
    public string CompressionAlgorithm { get; init; } = "Gzip"; // Gzip/Brotli
}

public record NetworkConfigDto
{
    public int HealthCheckIntervalMs { get; init; } = 5000;
    public int HealthCheckTimeoutMs { get; init; } = 3000;
    public int OfflineThreshold { get; init; } = 3;
    public int SendBatchSize { get; init; } = 500;
    public int SendIntervalMs { get; init; } = 500;
}

/// <summary>
/// 标签级预处理配置
/// </summary>
public record TagProcessingConfigDto
{
    public string TagId { get; init; } = "";
    public string? TagName { get; init; }
    public double? Deadband { get; init; }
    public double? DeadbandPercent { get; init; }
    public int? MinIntervalMs { get; init; }
    public bool Bypass { get; init; } = false;
    public int Priority { get; init; } = 0;
    public string? Description { get; init; }
}

/// <summary>
/// 批量更新标签配置请求
/// </summary>
public record BatchUpdateTagConfigRequest
{
    public List<TagProcessingConfigDto> Tags { get; init; } = new();
}
```

---

## 三、后端 API 设计

### 3.1 Edge 配置 API

```csharp
// Host.Api/Endpoints/EdgeConfigEndpoints.cs
namespace IntelliMaint.Host.Api.Endpoints;

public static class EdgeConfigEndpoints
{
    public static void MapEdgeConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/edge-config")
            .WithTags("Edge配置管理");

        // 获取 Edge 配置
        group.MapGet("/{edgeId}", GetEdgeConfig)
            .WithName("GetEdgeConfig")
            .WithSummary("获取Edge节点配置");

        // 更新 Edge 配置
        group.MapPut("/{edgeId}", UpdateEdgeConfig)
            .WithName("UpdateEdgeConfig")
            .WithSummary("更新Edge节点配置")
            .RequireAuthorization("AdminOrOperator");

        // 获取标签配置列表
        group.MapGet("/{edgeId}/tags", GetTagConfigs)
            .WithName("GetTagProcessingConfigs")
            .WithSummary("获取标签级预处理配置");

        // 批量更新标签配置
        group.MapPut("/{edgeId}/tags", BatchUpdateTagConfigs)
            .WithName("BatchUpdateTagConfigs")
            .WithSummary("批量更新标签预处理配置")
            .RequireAuthorization("AdminOrOperator");

        // 删除标签配置
        group.MapDelete("/{edgeId}/tags/{tagId}", DeleteTagConfig)
            .WithName("DeleteTagConfig")
            .WithSummary("删除标签预处理配置")
            .RequireAuthorization("AdminOrOperator");

        // 通知 Edge 同步配置
        group.MapPost("/{edgeId}/sync", NotifyConfigSync)
            .WithName("NotifyEdgeConfigSync")
            .WithSummary("通知Edge同步配置")
            .RequireAuthorization("AdminOrOperator");

        // 获取 Edge 状态
        group.MapGet("/{edgeId}/status", GetEdgeStatus)
            .WithName("GetEdgeStatus")
            .WithSummary("获取Edge运行状态");

        // 获取所有 Edge 列表
        group.MapGet("/", ListEdges)
            .WithName("ListEdges")
            .WithSummary("获取所有Edge节点");
    }

    private static async Task<IResult> GetEdgeConfig(
        string edgeId,
        [FromServices] IEdgeConfigRepository repo,
        CancellationToken ct)
    {
        var config = await repo.GetAsync(edgeId, ct);
        if (config == null)
        {
            // 返回默认配置
            config = new EdgeConfigDto
            {
                EdgeId = edgeId,
                Name = edgeId,
                CreatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }
        return Results.Ok(new { success = true, data = config });
    }

    private static async Task<IResult> UpdateEdgeConfig(
        string edgeId,
        [FromBody] EdgeConfigDto config,
        [FromServices] IEdgeConfigRepository repo,
        [FromServices] IEdgeNotificationService notifier,
        HttpContext http,
        CancellationToken ct)
    {
        var userId = http.User.Identity?.Name ?? "system";
        var updated = config with
        {
            EdgeId = edgeId,
            UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UpdatedBy = userId
        };

        await repo.UpsertAsync(updated, ct);

        // 通知 Edge 配置已变更
        await notifier.NotifyConfigChangedAsync(edgeId, ct);

        return Results.Ok(new { success = true, data = updated });
    }

    private static async Task<IResult> GetTagConfigs(
        string edgeId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        [FromServices] ITagProcessingConfigRepository repo,
        CancellationToken ct)
    {
        var configs = await repo.ListByEdgeAsync(edgeId, page ?? 1, pageSize ?? 50, search, ct);
        return Results.Ok(new { success = true, data = configs });
    }

    private static async Task<IResult> BatchUpdateTagConfigs(
        string edgeId,
        [FromBody] BatchUpdateTagConfigRequest request,
        [FromServices] ITagProcessingConfigRepository repo,
        [FromServices] IEdgeNotificationService notifier,
        CancellationToken ct)
    {
        await repo.BatchUpsertAsync(edgeId, request.Tags, ct);
        await notifier.NotifyConfigChangedAsync(edgeId, ct);

        return Results.Ok(new { success = true, message = $"Updated {request.Tags.Count} tag configs" });
    }

    private static async Task<IResult> DeleteTagConfig(
        string edgeId,
        string tagId,
        [FromServices] ITagProcessingConfigRepository repo,
        CancellationToken ct)
    {
        await repo.DeleteAsync(edgeId, tagId, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> NotifyConfigSync(
        string edgeId,
        [FromServices] IEdgeNotificationService notifier,
        CancellationToken ct)
    {
        await notifier.NotifyConfigChangedAsync(edgeId, ct);
        return Results.Ok(new { success = true, message = "Sync notification sent" });
    }

    private static async Task<IResult> GetEdgeStatus(
        string edgeId,
        [FromServices] IEdgeStatusRepository repo,
        CancellationToken ct)
    {
        var status = await repo.GetAsync(edgeId, ct);
        return Results.Ok(new { success = true, data = status });
    }

    private static async Task<IResult> ListEdges(
        [FromServices] IEdgeConfigRepository repo,
        CancellationToken ct)
    {
        var edges = await repo.ListAllAsync(ct);
        return Results.Ok(new { success = true, data = edges });
    }
}
```

---

## 四、前端界面设计

### 4.1 页面结构

```
/settings/edge-config
├── EdgeConfigPage.tsx          # 主页面
├── components/
│   ├── EdgeSelector.tsx        # Edge 选择器
│   ├── ProcessingConfigCard.tsx # 预处理配置卡片
│   ├── StoreForwardConfigCard.tsx # 断网续传配置卡片
│   ├── NetworkConfigCard.tsx   # 网络配置卡片
│   ├── TagConfigTable.tsx      # 标签配置表格
│   ├── TagConfigModal.tsx      # 标签配置编辑弹窗
│   └── EdgeStatusCard.tsx      # Edge 状态卡片
└── api/
    └── edgeConfig.ts           # API 调用
```

### 4.2 页面布局

```tsx
// pages/EdgeConfig/index.tsx
import { useState, useEffect } from 'react'
import {
  Card, Row, Col, Form, InputNumber, Switch, Select, Button,
  Table, Space, Tag, Modal, Input, message, Tooltip, Statistic
} from 'antd'
import {
  SettingOutlined, CloudServerOutlined, SyncOutlined,
  CheckCircleOutlined, ExclamationCircleOutlined
} from '@ant-design/icons'

const EdgeConfigPage = () => {
  const [selectedEdge, setSelectedEdge] = useState<string>('edge-001')
  const [config, setConfig] = useState<EdgeConfig | null>(null)
  const [tagConfigs, setTagConfigs] = useState<TagConfig[]>([])
  const [edgeStatus, setEdgeStatus] = useState<EdgeStatus | null>(null)
  const [loading, setLoading] = useState(false)

  return (
    <div style={{ padding: 24 }}>
      {/* 页面标题 */}
      <div style={{ marginBottom: 24 }}>
        <h2>
          <CloudServerOutlined /> Edge 配置管理
        </h2>
        <p style={{ color: '#666' }}>
          配置边缘节点的数据预处理、断网续传等参数
        </p>
      </div>

      {/* Edge 选择和状态 */}
      <Row gutter={16} style={{ marginBottom: 24 }}>
        <Col span={8}>
          <Card size="small" title="选择 Edge 节点">
            <Select
              style={{ width: '100%' }}
              value={selectedEdge}
              onChange={setSelectedEdge}
              options={[
                { label: 'Edge-001 (主车间)', value: 'edge-001' },
                { label: 'Edge-002 (副车间)', value: 'edge-002' },
              ]}
            />
          </Card>
        </Col>
        <Col span={16}>
          <EdgeStatusCard status={edgeStatus} onSync={handleSync} />
        </Col>
      </Row>

      {/* 配置卡片 */}
      <Row gutter={16}>
        {/* 数据预处理配置 */}
        <Col span={8}>
          <ProcessingConfigCard
            config={config?.processing}
            onChange={handleProcessingChange}
          />
        </Col>

        {/* 断网续传配置 */}
        <Col span={8}>
          <StoreForwardConfigCard
            config={config?.storeForward}
            onChange={handleStoreForwardChange}
          />
        </Col>

        {/* 网络配置 */}
        <Col span={8}>
          <NetworkConfigCard
            config={config?.network}
            onChange={handleNetworkChange}
          />
        </Col>
      </Row>

      {/* 标签级配置 */}
      <Card
        title="标签级配置"
        style={{ marginTop: 24 }}
        extra={
          <Space>
            <Button onClick={handleBatchImport}>批量导入</Button>
            <Button onClick={handleExport}>导出配置</Button>
            <Button type="primary" onClick={handleAddTag}>添加标签</Button>
          </Space>
        }
      >
        <TagConfigTable
          data={tagConfigs}
          onEdit={handleEditTag}
          onDelete={handleDeleteTag}
        />
      </Card>
    </div>
  )
}
```

### 4.3 预处理配置卡片

```tsx
// components/ProcessingConfigCard.tsx
const ProcessingConfigCard = ({ config, onChange }) => {
  return (
    <Card
      title={
        <span>
          <SettingOutlined /> 数据预处理
        </span>
      }
      extra={
        <Switch
          checked={config?.enabled}
          onChange={(v) => onChange({ ...config, enabled: v })}
          checkedChildren="启用"
          unCheckedChildren="禁用"
        />
      }
    >
      <Form layout="vertical" size="small">
        <Form.Item
          label={
            <Tooltip title="数值变化小于此阈值不上传 (绝对值)">
              默认死区 (绝对值)
            </Tooltip>
          }
        >
          <InputNumber
            style={{ width: '100%' }}
            value={config?.defaultDeadband}
            onChange={(v) => onChange({ ...config, defaultDeadband: v })}
            min={0}
            step={0.01}
            addonAfter="单位值"
          />
        </Form.Item>

        <Form.Item
          label={
            <Tooltip title="数值变化小于此百分比不上传 (相对上次值)">
              默认死区 (百分比)
            </Tooltip>
          }
        >
          <InputNumber
            style={{ width: '100%' }}
            value={config?.defaultDeadbandPercent}
            onChange={(v) => onChange({ ...config, defaultDeadbandPercent: v })}
            min={0}
            max={100}
            step={0.1}
            addonAfter="%"
          />
        </Form.Item>

        <Form.Item
          label={
            <Tooltip title="同一标签两次上传的最小间隔">
              最小上传间隔
            </Tooltip>
          }
        >
          <InputNumber
            style={{ width: '100%' }}
            value={config?.defaultMinIntervalMs}
            onChange={(v) => onChange({ ...config, defaultMinIntervalMs: v })}
            min={100}
            step={100}
            addonAfter="毫秒"
          />
        </Form.Item>

        <Form.Item
          label={
            <Tooltip title="即使数值未变化，超过此时间也强制上传">
              强制上传间隔
            </Tooltip>
          }
        >
          <InputNumber
            style={{ width: '100%' }}
            value={config?.forceUploadIntervalMs}
            onChange={(v) => onChange({ ...config, forceUploadIntervalMs: v })}
            min={1000}
            step={1000}
            addonAfter="毫秒"
          />
        </Form.Item>

        <Form.Item label="异常值检测">
          <Space direction="vertical" style={{ width: '100%' }}>
            <Switch
              checked={config?.outlierEnabled}
              onChange={(v) => onChange({ ...config, outlierEnabled: v })}
              checkedChildren="启用"
              unCheckedChildren="禁用"
            />
            {config?.outlierEnabled && (
              <>
                <InputNumber
                  style={{ width: '100%' }}
                  value={config?.outlierSigmaThreshold}
                  onChange={(v) => onChange({ ...config, outlierSigmaThreshold: v })}
                  min={1}
                  max={10}
                  step={0.5}
                  addonBefore="Sigma 阈值"
                />
                <Select
                  style={{ width: '100%' }}
                  value={config?.outlierAction}
                  onChange={(v) => onChange({ ...config, outlierAction: v })}
                  options={[
                    { label: '丢弃 (Drop)', value: 'Drop' },
                    { label: '标记 (Mark)', value: 'Mark' },
                    { label: '放行 (Pass)', value: 'Pass' },
                  ]}
                />
              </>
            )}
          </Space>
        </Form.Item>
      </Form>
    </Card>
  )
}
```

### 4.4 断网续传配置卡片

```tsx
// components/StoreForwardConfigCard.tsx
const StoreForwardConfigCard = ({ config, onChange }) => {
  return (
    <Card
      title={
        <span>
          <CloudServerOutlined /> 断网续传
        </span>
      }
      extra={
        <Switch
          checked={config?.enabled}
          onChange={(v) => onChange({ ...config, enabled: v })}
          checkedChildren="启用"
          unCheckedChildren="禁用"
        />
      }
    >
      <Form layout="vertical" size="small">
        <Form.Item
          label={
            <Tooltip title="本地缓存数据的最大容量">
              最大存储容量
            </Tooltip>
          }
        >
          <InputNumber
            style={{ width: '100%' }}
            value={config?.maxStoreSizeMB}
            onChange={(v) => onChange({ ...config, maxStoreSizeMB: v })}
            min={100}
            max={10000}
            step={100}
            addonAfter="MB"
          />
        </Form.Item>

        <Form.Item
          label={
            <Tooltip title="超过此天数的缓存数据自动清理">
              数据保留天数
            </Tooltip>
          }
        >
          <InputNumber
            style={{ width: '100%' }}
            value={config?.retentionDays}
            onChange={(v) => onChange({ ...config, retentionDays: v })}
            min={1}
            max={30}
            addonAfter="天"
          />
        </Form.Item>

        <Form.Item label="数据压缩">
          <Space direction="vertical" style={{ width: '100%' }}>
            <Switch
              checked={config?.compressionEnabled}
              onChange={(v) => onChange({ ...config, compressionEnabled: v })}
              checkedChildren="启用"
              unCheckedChildren="禁用"
            />
            {config?.compressionEnabled && (
              <Select
                style={{ width: '100%' }}
                value={config?.compressionAlgorithm}
                onChange={(v) => onChange({ ...config, compressionAlgorithm: v })}
                options={[
                  { label: 'Gzip (推荐)', value: 'Gzip' },
                  { label: 'Brotli (更高压缩率)', value: 'Brotli' },
                ]}
              />
            )}
          </Space>
        </Form.Item>
      </Form>
    </Card>
  )
}
```

### 4.5 标签配置表格

```tsx
// components/TagConfigTable.tsx
const TagConfigTable = ({ data, onEdit, onDelete }) => {
  const columns = [
    {
      title: '标签ID',
      dataIndex: 'tagId',
      key: 'tagId',
      width: 200,
    },
    {
      title: '死区',
      dataIndex: 'deadband',
      key: 'deadband',
      width: 100,
      render: (v) => v ?? <span style={{ color: '#999' }}>默认</span>
    },
    {
      title: '最小间隔',
      dataIndex: 'minIntervalMs',
      key: 'minIntervalMs',
      width: 120,
      render: (v) => v ? `${v}ms` : <span style={{ color: '#999' }}>默认</span>
    },
    {
      title: '绕过预处理',
      dataIndex: 'bypass',
      key: 'bypass',
      width: 100,
      render: (v) => v ? <Tag color="warning">是</Tag> : <Tag>否</Tag>
    },
    {
      title: '优先级',
      dataIndex: 'priority',
      key: 'priority',
      width: 80,
      render: (v) => <Tag color={v > 0 ? 'blue' : 'default'}>{v}</Tag>
    },
    {
      title: '说明',
      dataIndex: 'description',
      key: 'description',
      ellipsis: true,
    },
    {
      title: '操作',
      key: 'action',
      width: 120,
      render: (_, record) => (
        <Space>
          <Button type="link" size="small" onClick={() => onEdit(record)}>
            编辑
          </Button>
          <Button type="link" size="small" danger onClick={() => onDelete(record)}>
            删除
          </Button>
        </Space>
      )
    }
  ]

  return (
    <Table
      columns={columns}
      dataSource={data}
      rowKey="tagId"
      size="small"
      pagination={{ pageSize: 20 }}
    />
  )
}
```

### 4.6 Edge 状态卡片

```tsx
// components/EdgeStatusCard.tsx
const EdgeStatusCard = ({ status, onSync }) => {
  return (
    <Card size="small" title="Edge 运行状态">
      <Row gutter={16}>
        <Col span={4}>
          <Statistic
            title="网络状态"
            value={status?.isOnline ? '在线' : '离线'}
            prefix={status?.isOnline ?
              <CheckCircleOutlined style={{ color: '#52c41a' }} /> :
              <ExclamationCircleOutlined style={{ color: '#f5222d' }} />
            }
          />
        </Col>
        <Col span={4}>
          <Statistic
            title="待发送"
            value={status?.pendingPoints ?? 0}
            suffix="条"
          />
        </Col>
        <Col span={4}>
          <Statistic
            title="过滤率"
            value={status?.filterRate ?? 0}
            precision={1}
            suffix="%"
          />
        </Col>
        <Col span={4}>
          <Statistic
            title="已发送"
            value={status?.sentCount ?? 0}
          />
        </Col>
        <Col span={4}>
          <Statistic
            title="本地缓存"
            value={status?.storedMB ?? 0}
            suffix="MB"
          />
        </Col>
        <Col span={4}>
          <Button
            type="primary"
            icon={<SyncOutlined />}
            onClick={onSync}
          >
            同步配置
          </Button>
        </Col>
      </Row>
    </Card>
  )
}
```

---

## 五、Edge 端实现

### 5.1 文件滚动存储 (替代 SQLite)

```csharp
// Services/FileRollingStore.cs
using System.IO.Compression;
using System.Text.Json;
using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Host.Edge.Services;

/// <summary>
/// 文件滚动存储 - 用于断网续传
/// 不依赖 SQLite，使用压缩文件存储
/// </summary>
public sealed class FileRollingStore : ILocalOutboxStore, IDisposable
{
    private readonly string _baseDir;
    private readonly int _maxSizeMB;
    private readonly int _retentionDays;
    private readonly ILogger<FileRollingStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private const string IndexFile = "index.json";
    private const string BatchPrefix = "batch_";
    private const string BatchSuffix = ".bin.gz";

    private long _nextBatchId;
    private readonly Queue<BatchInfo> _pendingBatches = new();

    public FileRollingStore(
        IOptions<StoreForwardConfigDto> options,
        ILogger<FileRollingStore> logger)
    {
        _baseDir = Path.Combine(AppContext.BaseDirectory, "data", "outbox");
        _maxSizeMB = options.Value.MaxStoreSizeMB;
        _retentionDays = options.Value.RetentionDays;
        _logger = logger;

        Directory.CreateDirectory(_baseDir);
        LoadIndex();

        _logger.LogInformation("FileRollingStore initialized: {Path}, MaxSize={Size}MB",
            _baseDir, _maxSizeMB);
    }

    /// <summary>
    /// 存储批量数据
    /// </summary>
    public async Task StoreAsync(IReadOnlyList<TelemetryPoint> points, CancellationToken ct)
    {
        if (points.Count == 0) return;

        await _writeLock.WaitAsync(ct);
        try
        {
            var batchId = Interlocked.Increment(ref _nextBatchId);
            var fileName = $"{BatchPrefix}{batchId:D10}{BatchSuffix}";
            var filePath = Path.Combine(_baseDir, fileName);

            // 序列化并压缩
            var json = JsonSerializer.SerializeToUtf8Bytes(points);
            await using (var fs = new FileStream(filePath, FileMode.Create))
            await using (var gzip = new GZipStream(fs, CompressionLevel.Fastest))
            {
                await gzip.WriteAsync(json, ct);
            }

            var fileSize = new FileInfo(filePath).Length;
            var batchInfo = new BatchInfo
            {
                Id = batchId,
                FileName = fileName,
                PointCount = points.Count,
                SizeBytes = fileSize,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _pendingBatches.Enqueue(batchInfo);
            await SaveIndexAsync(ct);

            _logger.LogDebug("Stored batch {Id}: {Count} points, {Size} bytes",
                batchId, points.Count, fileSize);

            // 检查容量限制
            await EnforceCapacityLimitAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// 读取下一个待发送批次
    /// </summary>
    public async Task<OutboxBatch?> ReadBatchAsync(int limit, CancellationToken ct)
    {
        if (_pendingBatches.Count == 0) return null;

        var batchInfo = _pendingBatches.Peek();
        var filePath = Path.Combine(_baseDir, batchInfo.FileName);

        if (!File.Exists(filePath))
        {
            _pendingBatches.Dequeue();
            await SaveIndexAsync(ct);
            return await ReadBatchAsync(limit, ct);
        }

        await using var fs = new FileStream(filePath, FileMode.Open);
        await using var gzip = new GZipStream(fs, CompressionMode.Decompress);
        using var ms = new MemoryStream();
        await gzip.CopyToAsync(ms, ct);

        var points = JsonSerializer.Deserialize<List<TelemetryPoint>>(ms.ToArray());

        return new OutboxBatch
        {
            Id = batchInfo.Id,
            Points = points ?? new(),
            CreatedAt = batchInfo.CreatedAt
        };
    }

    /// <summary>
    /// 确认批次已发送
    /// </summary>
    public async Task AcknowledgeAsync(long batchId, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            if (_pendingBatches.Count > 0 && _pendingBatches.Peek().Id == batchId)
            {
                var batch = _pendingBatches.Dequeue();
                var filePath = Path.Combine(_baseDir, batch.FileName);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                await SaveIndexAsync(ct);
                _logger.LogDebug("Acknowledged and deleted batch {Id}", batchId);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Task<bool> HasDataAsync(CancellationToken ct)
    {
        return Task.FromResult(_pendingBatches.Count > 0);
    }

    public Task<int> GetPendingCountAsync(CancellationToken ct)
    {
        return Task.FromResult(_pendingBatches.Sum(b => b.PointCount));
    }

    public async Task CleanupAsync(CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-_retentionDays).ToUnixTimeMilliseconds();
            var expired = _pendingBatches.Where(b => b.CreatedAt < cutoff).ToList();

            foreach (var batch in expired)
            {
                var filePath = Path.Combine(_baseDir, batch.FileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }

            if (expired.Count > 0)
            {
                // 重建队列
                var remaining = _pendingBatches.Where(b => b.CreatedAt >= cutoff).ToList();
                _pendingBatches.Clear();
                foreach (var b in remaining)
                {
                    _pendingBatches.Enqueue(b);
                }
                await SaveIndexAsync(ct);
                _logger.LogInformation("Cleaned up {Count} expired batches", expired.Count);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public OutboxStats GetStats()
    {
        var totalSize = _pendingBatches.Sum(b => b.SizeBytes);
        return new OutboxStats
        {
            PendingBatches = _pendingBatches.Count,
            PendingPoints = _pendingBatches.Sum(b => b.PointCount),
            TotalStoredBytes = totalSize
        };
    }

    private async Task EnforceCapacityLimitAsync(CancellationToken ct)
    {
        var totalSizeMB = _pendingBatches.Sum(b => b.SizeBytes) / (1024.0 * 1024.0);

        while (totalSizeMB > _maxSizeMB && _pendingBatches.Count > 0)
        {
            var oldest = _pendingBatches.Dequeue();
            var filePath = Path.Combine(_baseDir, oldest.FileName);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            totalSizeMB -= oldest.SizeBytes / (1024.0 * 1024.0);
            _logger.LogWarning("Capacity limit reached, deleted oldest batch {Id}", oldest.Id);
        }

        await SaveIndexAsync(ct);
    }

    private void LoadIndex()
    {
        var indexPath = Path.Combine(_baseDir, IndexFile);
        if (!File.Exists(indexPath)) return;

        try
        {
            var json = File.ReadAllText(indexPath);
            var index = JsonSerializer.Deserialize<OutboxIndex>(json);

            if (index != null)
            {
                _nextBatchId = index.NextBatchId;
                foreach (var batch in index.Batches.OrderBy(b => b.Id))
                {
                    _pendingBatches.Enqueue(batch);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load index, starting fresh");
        }
    }

    private async Task SaveIndexAsync(CancellationToken ct)
    {
        var index = new OutboxIndex
        {
            NextBatchId = _nextBatchId,
            Batches = _pendingBatches.ToList()
        };

        var json = JsonSerializer.Serialize(index);
        var indexPath = Path.Combine(_baseDir, IndexFile);
        await File.WriteAllTextAsync(indexPath, json, ct);
    }

    public void Dispose()
    {
        _writeLock.Dispose();
    }

    private class OutboxIndex
    {
        public long NextBatchId { get; set; }
        public List<BatchInfo> Batches { get; set; } = new();
    }

    private class BatchInfo
    {
        public long Id { get; set; }
        public string FileName { get; set; } = "";
        public int PointCount { get; set; }
        public long SizeBytes { get; set; }
        public long CreatedAt { get; set; }
    }
}
```

### 5.2 配置同步服务

```csharp
// Services/ConfigSyncService.cs
namespace IntelliMaint.Host.Edge.Services;

/// <summary>
/// 配置同步服务 - 定期从 API 拉取配置
/// </summary>
public sealed class ConfigSyncService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EdgeOptions _edgeOptions;
    private readonly ILogger<ConfigSyncService> _logger;

    private volatile EdgeConfigDto? _currentConfig;
    private volatile List<TagProcessingConfigDto> _tagConfigs = new();

    public event Action<EdgeConfigDto>? OnConfigChanged;

    public EdgeConfigDto? CurrentConfig => _currentConfig;
    public IReadOnlyList<TagProcessingConfigDto> TagConfigs => _tagConfigs;

    private const int SyncIntervalSeconds = 30;

    public ConfigSyncService(
        IHttpClientFactory httpClientFactory,
        IOptions<EdgeOptions> edgeOptions,
        ILogger<ConfigSyncService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _edgeOptions = edgeOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ConfigSyncService started");

        // 启动时立即同步一次
        await SyncConfigAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(SyncIntervalSeconds), stoppingToken);
            await SyncConfigAsync(stoppingToken);
        }
    }

    private async Task SyncConfigAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("ApiClient");
            var baseUrl = _edgeOptions.ApiBaseUrl?.TrimEnd('/');
            var edgeId = _edgeOptions.EdgeId;

            // 获取全局配置
            var configUrl = $"{baseUrl}/api/edge-config/{edgeId}";
            var configResponse = await client.GetAsync(configUrl, ct);

            if (configResponse.IsSuccessStatusCode)
            {
                var json = await configResponse.Content.ReadAsStringAsync(ct);
                var result = JsonSerializer.Deserialize<ApiResponse<EdgeConfigDto>>(json);

                if (result?.Data != null)
                {
                    var changed = !ConfigEquals(_currentConfig, result.Data);
                    _currentConfig = result.Data;

                    if (changed)
                    {
                        _logger.LogInformation("Configuration updated from API");
                        OnConfigChanged?.Invoke(result.Data);
                    }
                }
            }

            // 获取标签配置
            var tagsUrl = $"{baseUrl}/api/edge-config/{edgeId}/tags";
            var tagsResponse = await client.GetAsync(tagsUrl, ct);

            if (tagsResponse.IsSuccessStatusCode)
            {
                var json = await tagsResponse.Content.ReadAsStringAsync(ct);
                var result = JsonSerializer.Deserialize<ApiResponse<List<TagProcessingConfigDto>>>(json);

                if (result?.Data != null)
                {
                    _tagConfigs = result.Data;
                }
            }

            _logger.LogDebug("Config synced: {TagCount} tag configs", _tagConfigs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync config from API");
        }
    }

    private static bool ConfigEquals(EdgeConfigDto? a, EdgeConfigDto? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return a.UpdatedUtc == b.UpdatedUtc;
    }

    /// <summary>
    /// 获取标签的预处理配置
    /// </summary>
    public TagProcessingConfigDto? GetTagConfig(string tagId)
    {
        return _tagConfigs.FirstOrDefault(t => t.TagId == tagId);
    }

    private class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
    }
}
```

---

## 六、实施计划

### Week 1: 后端 + Edge

| 天 | 任务 |
|----|------|
| Day 1 | 数据库表设计 + Repository 实现 |
| Day 2 | EdgeConfigEndpoints API 实现 |
| Day 3 | EdgeDataProcessor 实现 |
| Day 4 | FileRollingStore 实现 |
| Day 5 | StoreAndForwardService 集成 |

### Week 2: 前端 + 测试

| 天 | 任务 |
|----|------|
| Day 1 | EdgeConfig API 调用封装 |
| Day 2 | EdgeConfigPage 主页面 |
| Day 3 | 配置卡片组件实现 |
| Day 4 | 标签配置表格 + 弹窗 |
| Day 5 | 集成测试 + 修复 |

---

## 七、预期效果

| 指标 | 优化前 | 优化后 |
|------|--------|--------|
| 数据传输量 | 100% | **30%** |
| 断网数据丢失 | 100% | **0%** |
| 配置修改方式 | 改配置文件重启 | **前端实时修改** |
| 配置生效时间 | 重启后 | **30秒内热更新** |

---

*文档由 Claude Code 生成*
