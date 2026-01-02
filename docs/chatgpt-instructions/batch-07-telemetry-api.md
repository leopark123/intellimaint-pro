# Batch 7: 数据查询 API - ChatGPT 开发指令

## 项目背景

你正在为 **IntelliMaint Pro** 工业数据采集平台开发数据查询 API。

### 当前已完成
- OPC UA 数据采集 ✅
- SQLite 数据存储 ✅
- 基础 API 框架 ✅

### 本批次目标
实现完整的遥测数据查询 REST API。

---

## 技术约束（必须遵守）

### 1. 框架版本
- .NET 8.0
- ASP.NET Core Minimal API 或 Controller
- SQLite + Dapper

### 2. 命名规范
- 命名空间: `IntelliMaint.Host.Api.Controllers` 或 `IntelliMaint.Host.Api.Endpoints`
- 文件名: PascalCase
- 方法名: PascalCase
- 变量名: camelCase
- 私有字段: _camelCase

### 3. 已存在的类型（直接使用，不要重新定义）

```csharp
// 位置: IntelliMaint.Core.Contracts.TelemetryPoint
public sealed record TelemetryPoint
{
    public required string DeviceId { get; init; }
    public required string TagId { get; init; }
    public required long Ts { get; init; }           // Unix 毫秒时间戳
    public required int Seq { get; init; }
    public required TagValueType ValueType { get; init; }
    
    // 值字段（根据 ValueType 使用对应字段）
    public bool? BoolValue { get; init; }
    public sbyte? Int8Value { get; init; }
    public byte? UInt8Value { get; init; }
    public short? Int16Value { get; init; }
    public ushort? UInt16Value { get; init; }
    public int? Int32Value { get; init; }
    public uint? UInt32Value { get; init; }
    public long? Int64Value { get; init; }
    public ulong? UInt64Value { get; init; }
    public float? Float32Value { get; init; }
    public double? Float64Value { get; init; }
    public string? StringValue { get; init; }
    public byte[]? ByteArrayValue { get; init; }
    
    public int Quality { get; init; }
    public string? Unit { get; init; }
    public string? Protocol { get; init; }
}

// 位置: IntelliMaint.Core.Contracts.ValueType
public enum TagValueType
{
    Bool = 1,
    Int8 = 2,
    UInt8 = 3,
    Int16 = 4,
    UInt16 = 5,
    Int32 = 6,
    UInt32 = 7,
    Int64 = 8,
    UInt64 = 9,
    Float32 = 10,
    Float64 = 11,
    String = 12,
    DateTime = 13,
    ByteArray = 14
}
```

### 4. 已存在的接口（需要扩展）

```csharp
// 位置: IntelliMaint.Core.Abstractions.Repositories
public interface ITelemetryRepository
{
    Task<int> AppendBatchAsync(IReadOnlyList<TelemetryPoint> points, CancellationToken ct);
    // 需要添加查询方法
}
```

### 5. 数据库表结构（已存在）

```sql
CREATE TABLE telemetry (
    device_id TEXT NOT NULL,
    tag_id TEXT NOT NULL,
    ts INTEGER NOT NULL,           -- Unix 毫秒时间戳
    seq INTEGER NOT NULL,
    value_type INTEGER NOT NULL,
    bool_value INTEGER,
    int8_value INTEGER,
    uint8_value INTEGER,
    int16_value INTEGER,
    uint16_value INTEGER,
    int32_value INTEGER,
    uint32_value INTEGER,
    int64_value INTEGER,
    uint64_value INTEGER,
    float32_value REAL,
    float64_value REAL,
    string_value TEXT,
    byte_array_value BLOB,
    quality INTEGER NOT NULL DEFAULT 192,
    unit TEXT,
    protocol TEXT,
    PRIMARY KEY (device_id, tag_id, ts, seq)
);

CREATE INDEX idx_telemetry_ts ON telemetry(ts);
CREATE INDEX idx_telemetry_device_tag ON telemetry(device_id, tag_id);
```

---

## 需要创建的文件

### 文件 1: `src/Core/Abstractions/Repositories.cs`（修改）

在 `ITelemetryRepository` 接口中添加查询方法：

```csharp
public interface ITelemetryRepository
{
    // 已存在
    Task<int> AppendBatchAsync(IReadOnlyList<TelemetryPoint> points, CancellationToken ct);
    
    // 新增：查询历史数据
    Task<IReadOnlyList<TelemetryPoint>> QueryAsync(
        string? deviceId,
        string? tagId,
        long? startTs,
        long? endTs,
        int limit,
        CancellationToken ct);
    
    // 新增：获取最新值
    Task<IReadOnlyList<TelemetryPoint>> GetLatestAsync(
        string? deviceId,
        string? tagId,
        CancellationToken ct);
    
    // 新增：获取所有已知的 Tag 列表
    Task<IReadOnlyList<TagInfo>> GetTagsAsync(CancellationToken ct);
    
    // 新增：聚合查询
    Task<IReadOnlyList<AggregateResult>> AggregateAsync(
        string deviceId,
        string tagId,
        long startTs,
        long endTs,
        int intervalMs,
        AggregateFunction func,
        CancellationToken ct);
}

// 新增类型
public sealed record TagInfo
{
    public required string DeviceId { get; init; }
    public required string TagId { get; init; }
    public required TagValueType ValueType { get; init; }
    public string? Unit { get; init; }
    public long? LastTs { get; init; }
    public int PointCount { get; init; }
}

public enum AggregateFunction
{
    Avg,
    Min,
    Max,
    Sum,
    Count,
    First,
    Last
}

public sealed record AggregateResult
{
    public required long Ts { get; init; }          // 时间桶起始时间
    public required double Value { get; init; }     // 聚合值
    public required int Count { get; init; }        // 样本数量
}
```

### 文件 2: `src/Infrastructure/Sqlite/TelemetryRepository.cs`（修改）

实现新增的查询方法。使用 Dapper 执行 SQL。

**关键实现要点：**

1. `QueryAsync`: 支持可选的 deviceId、tagId 过滤，时间范围过滤，分页
2. `GetLatestAsync`: 每个 (deviceId, tagId) 组合返回最新一条记录
3. `GetTagsAsync`: 返回去重的 tag 列表及统计信息
4. `AggregateAsync`: 按时间桶聚合，支持 avg/min/max/sum/count

### 文件 3: `src/Host.Api/Endpoints/TelemetryEndpoints.cs`（新建）

使用 Minimal API 风格定义端点：

```csharp
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace IntelliMaint.Host.Api.Endpoints;

public static class TelemetryEndpoints
{
    public static void MapTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/telemetry")
            .WithTags("Telemetry")
            .WithOpenApi();

        // GET /api/telemetry/query
        group.MapGet("/query", QueryAsync)
            .WithName("QueryTelemetry")
            .WithSummary("查询历史遥测数据");

        // GET /api/telemetry/latest
        group.MapGet("/latest", GetLatestAsync)
            .WithName("GetLatestTelemetry")
            .WithSummary("获取最新遥测值");

        // GET /api/telemetry/tags
        group.MapGet("/tags", GetTagsAsync)
            .WithName("GetTags")
            .WithSummary("获取所有已知标签");

        // GET /api/telemetry/aggregate
        group.MapGet("/aggregate", AggregateAsync)
            .WithName("AggregateTelemetry")
            .WithSummary("聚合查询");
    }

    // 实现各个端点方法...
}
```

### 文件 4: `src/Host.Api/Models/TelemetryModels.cs`（新建）

API 请求/响应模型：

```csharp
namespace IntelliMaint.Host.Api.Models;

// 查询请求参数
public sealed record TelemetryQueryRequest
{
    public string? DeviceId { get; init; }
    public string? TagId { get; init; }
    public long? StartTs { get; init; }     // Unix 毫秒
    public long? EndTs { get; init; }       // Unix 毫秒
    public int Limit { get; init; } = 1000; // 默认 1000，最大 10000
}

// 聚合请求参数
public sealed record AggregateRequest
{
    public required string DeviceId { get; init; }
    public required string TagId { get; init; }
    public required long StartTs { get; init; }
    public required long EndTs { get; init; }
    public int IntervalMs { get; init; } = 60000;  // 默认 1 分钟
    public string Function { get; init; } = "avg"; // avg, min, max, sum, count
}

// 通用响应包装
public sealed record ApiResponse<T>
{
    public bool Success { get; init; } = true;
    public T? Data { get; init; }
    public string? Error { get; init; }
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

// 遥测数据响应（简化版，用于 API 返回）
public sealed record TelemetryDataPoint
{
    public required string DeviceId { get; init; }
    public required string TagId { get; init; }
    public required long Ts { get; init; }
    public required object? Value { get; init; }    // 实际值（根据类型）
    public required string ValueType { get; init; } // 类型名称
    public required int Quality { get; init; }
    public string? Unit { get; init; }
}
```

---

## API 契约定义

### 1. GET /api/telemetry/query

**请求参数（Query String）：**
| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| deviceId | string | 否 | 设备 ID 过滤 |
| tagId | string | 否 | 标签 ID 过滤 |
| startTs | long | 否 | 开始时间（Unix 毫秒） |
| endTs | long | 否 | 结束时间（Unix 毫秒） |
| limit | int | 否 | 返回条数，默认 1000，最大 10000 |

**响应示例：**
```json
{
  "success": true,
  "data": [
    {
      "deviceId": "KEP-001",
      "tagId": "Ramp_Value",
      "ts": 1766807376544,
      "value": 3269,
      "valueType": "UInt16",
      "quality": 192,
      "unit": null
    }
  ],
  "timestamp": 1766807400000
}
```

### 2. GET /api/telemetry/latest

**请求参数（Query String）：**
| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| deviceId | string | 否 | 设备 ID 过滤 |
| tagId | string | 否 | 标签 ID 过滤 |

**响应示例：**
```json
{
  "success": true,
  "data": [
    {
      "deviceId": "KEP-001",
      "tagId": "Ramp_Value",
      "ts": 1766807376544,
      "value": 3269,
      "valueType": "UInt16",
      "quality": 192,
      "unit": null
    }
  ],
  "timestamp": 1766807400000
}
```

### 3. GET /api/telemetry/tags

**响应示例：**
```json
{
  "success": true,
  "data": [
    {
      "deviceId": "KEP-001",
      "tagId": "Ramp_Value",
      "valueType": "UInt16",
      "unit": null,
      "lastTs": 1766807376544,
      "pointCount": 12345
    }
  ],
  "timestamp": 1766807400000
}
```

### 4. GET /api/telemetry/aggregate

**请求参数（Query String）：**
| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| deviceId | string | 是 | 设备 ID |
| tagId | string | 是 | 标签 ID |
| startTs | long | 是 | 开始时间（Unix 毫秒） |
| endTs | long | 是 | 结束时间（Unix 毫秒） |
| intervalMs | int | 否 | 聚合间隔（毫秒），默认 60000 |
| function | string | 否 | 聚合函数：avg/min/max/sum/count，默认 avg |

**响应示例：**
```json
{
  "success": true,
  "data": [
    { "ts": 1766807340000, "value": 3250.5, "count": 60 },
    { "ts": 1766807400000, "value": 3310.2, "count": 60 }
  ],
  "timestamp": 1766807400000
}
```

---

## 代码实现要求

### 1. TelemetryRepository 查询实现

```csharp
public async Task<IReadOnlyList<TelemetryPoint>> QueryAsync(
    string? deviceId,
    string? tagId,
    long? startTs,
    long? endTs,
    int limit,
    CancellationToken ct)
{
    // 构建动态 SQL
    var sql = new StringBuilder(@"
        SELECT device_id, tag_id, ts, seq, value_type,
               bool_value, int8_value, uint8_value, int16_value, uint16_value,
               int32_value, uint32_value, int64_value, uint64_value,
               float32_value, float64_value, string_value, byte_array_value,
               quality, unit, protocol
        FROM telemetry
        WHERE 1=1");
    
    var parameters = new DynamicParameters();
    
    if (!string.IsNullOrEmpty(deviceId))
    {
        sql.Append(" AND device_id = @DeviceId");
        parameters.Add("DeviceId", deviceId);
    }
    
    if (!string.IsNullOrEmpty(tagId))
    {
        sql.Append(" AND tag_id = @TagId");
        parameters.Add("TagId", tagId);
    }
    
    if (startTs.HasValue)
    {
        sql.Append(" AND ts >= @StartTs");
        parameters.Add("StartTs", startTs.Value);
    }
    
    if (endTs.HasValue)
    {
        sql.Append(" AND ts <= @EndTs");
        parameters.Add("EndTs", endTs.Value);
    }
    
    sql.Append(" ORDER BY ts DESC LIMIT @Limit");
    parameters.Add("Limit", Math.Min(limit, 10000));
    
    // 执行查询并映射结果
    // ...
}
```

### 2. GetLatestAsync 实现

使用窗口函数或子查询获取每个 tag 的最新记录：

```sql
SELECT * FROM telemetry t1
WHERE ts = (
    SELECT MAX(ts) FROM telemetry t2
    WHERE t2.device_id = t1.device_id AND t2.tag_id = t1.tag_id
)
```

### 3. AggregateAsync 实现

使用 SQLite 的数学函数和时间桶：

```sql
SELECT 
    (ts / @IntervalMs) * @IntervalMs AS bucket_ts,
    AVG(COALESCE(float64_value, float32_value, int32_value, uint16_value, 0)) AS value,
    COUNT(*) AS count
FROM telemetry
WHERE device_id = @DeviceId 
  AND tag_id = @TagId
  AND ts >= @StartTs 
  AND ts < @EndTs
GROUP BY bucket_ts
ORDER BY bucket_ts
```

### 4. 值提取辅助方法

在返回 API 响应时，需要根据 ValueType 提取正确的值：

```csharp
public static object? ExtractValue(TelemetryPoint p)
{
    return p.ValueType switch
    {
        TagValueType.Bool => p.BoolValue,
        TagValueType.Int8 => p.Int8Value,
        TagValueType.UInt8 => p.UInt8Value,
        TagValueType.Int16 => p.Int16Value,
        TagValueType.UInt16 => p.UInt16Value,
        TagValueType.Int32 => p.Int32Value,
        TagValueType.UInt32 => p.UInt32Value,
        TagValueType.Int64 => p.Int64Value,
        TagValueType.UInt64 => p.UInt64Value,
        TagValueType.Float32 => p.Float32Value,
        TagValueType.Float64 => p.Float64Value,
        TagValueType.String => p.StringValue,
        TagValueType.ByteArray => p.ByteArrayValue != null 
            ? Convert.ToBase64String(p.ByteArrayValue) 
            : null,
        _ => null
    };
}
```

---

## Host.Api/Program.cs 修改

需要注册新的端点：

```csharp
// 在 app.MapGet("/", ...) 之后添加
app.MapTelemetryEndpoints();
```

---

## 测试用例

完成后，应能通过以下测试：

### 测试 1: 查询所有数据
```
GET http://localhost:5000/api/telemetry/query?limit=10
```
预期: 返回最近 10 条记录

### 测试 2: 按设备过滤
```
GET http://localhost:5000/api/telemetry/query?deviceId=KEP-001&limit=10
```
预期: 返回 KEP-001 设备的最近 10 条记录

### 测试 3: 按时间范围查询
```
GET http://localhost:5000/api/telemetry/query?startTs=1766807370000&endTs=1766807380000
```
预期: 返回指定时间范围内的记录

### 测试 4: 获取最新值
```
GET http://localhost:5000/api/telemetry/latest
```
预期: 返回每个 tag 的最新一条记录

### 测试 5: 获取标签列表
```
GET http://localhost:5000/api/telemetry/tags
```
预期: 返回所有已知 tag 及统计信息

### 测试 6: 聚合查询
```
GET http://localhost:5000/api/telemetry/aggregate?deviceId=KEP-001&tagId=Ramp_Value&startTs=1766807000000&endTs=1766808000000&intervalMs=60000&function=avg
```
预期: 返回每分钟的平均值

---

## 输出要求

请提供以下文件的完整代码：

1. **`src/Core/Abstractions/Repositories.cs`** - 完整文件（包含新增的接口方法和类型）
2. **`src/Infrastructure/Sqlite/TelemetryRepository.cs`** - 完整文件（实现所有查询方法）
3. **`src/Host.Api/Endpoints/TelemetryEndpoints.cs`** - 新文件
4. **`src/Host.Api/Models/TelemetryModels.cs`** - 新文件
5. **`src/Host.Api/Program.cs`** - 显示需要添加的代码行

每个文件必须：
- 包含完整的 using 语句
- 包含正确的命名空间
- 可直接复制使用
- 不要省略任何代码（不要写 "// ... 其他代码"）

---

## 重要提醒

1. **不要重新定义** `TelemetryPoint` 和 `TagValueType`，它们已存在
2. **使用 Dapper** 执行 SQL，不要使用 EF Core
3. **SQLite 语法**，不是 SQL Server 或 PostgreSQL
4. **Minimal API** 风格，不是 Controller
5. 所有方法必须支持 **CancellationToken**
6. 使用 **async/await** 模式
