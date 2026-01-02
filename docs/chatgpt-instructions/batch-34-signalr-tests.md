# Batch 34: SignalR 分组推送 + 集成测试骨架

## 1. 任务背景

v33 完成了架构解耦和配置 revision 机制。v34 聚焦工程化：
- SignalR 当前广播所有数据给所有客户端，浪费带宽
- 缺乏集成测试，无法保证 API 回归

## 2. 任务目标

| 目标 | 完成定义 |
|------|----------|
| SignalR 分组推送 | 客户端可按 deviceId 订阅，只收到订阅设备的数据 |
| 集成测试骨架 | Device/Tag CRUD API 测试通过，使用临时 SQLite |

## 3. 文件变更清单

| 文件路径 | 操作 | 变更说明 |
|----------|------|----------|
| `src/Host.Api/Hubs/TelemetryHub.cs` | 修改 | 添加 Subscribe/Unsubscribe 方法 |
| `src/Host.Api/Services/TelemetryBroadcastService.cs` | 修改 | 改为分组推送 |
| `src/Host.Api/Program.cs` | 修改 | 末尾添加 `public partial class Program { }` |
| `intellimaint-ui/src/api/signalr.ts` | 修改 | 添加订阅方法 |
| `intellimaint-ui/src/hooks/useRealTimeData.ts` | 修改 | 支持按设备订阅 |
| `tests/Integration/IntelliMaint.Tests.Integration.csproj` | 新建 | 测试项目 |
| `tests/Integration/ApiTestFixture.cs` | 新建 | 测试基类 |
| `tests/Integration/DeviceApiTests.cs` | 新建 | Device CRUD 测试 |
| `tests/Integration/TagApiTests.cs` | 新建 | Tag CRUD 测试 |

## 4. 详细实现要求

### 4.1 修改 `src/Host.Api/Hubs/TelemetryHub.cs`

保留现有代码，添加以下方法：

```csharp
/// <summary>订阅指定设备</summary>
public async Task SubscribeDevice(string deviceId)
{
    if (string.IsNullOrWhiteSpace(deviceId)) return;
    await Groups.AddToGroupAsync(Context.ConnectionId, $"device:{deviceId}");
    _logger.LogDebug("Client {ConnectionId} subscribed to device {DeviceId}", Context.ConnectionId, deviceId);
}

/// <summary>取消订阅指定设备</summary>
public async Task UnsubscribeDevice(string deviceId)
{
    if (string.IsNullOrWhiteSpace(deviceId)) return;
    await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"device:{deviceId}");
    _logger.LogDebug("Client {ConnectionId} unsubscribed from device {DeviceId}", Context.ConnectionId, deviceId);
}

/// <summary>订阅所有设备（广播模式）</summary>
public async Task SubscribeAll()
{
    await Groups.AddToGroupAsync(Context.ConnectionId, "all");
    _logger.LogDebug("Client {ConnectionId} subscribed to all devices", Context.ConnectionId);
}

/// <summary>取消订阅所有</summary>
public async Task UnsubscribeAll()
{
    await Groups.RemoveFromGroupAsync(Context.ConnectionId, "all");
    _logger.LogDebug("Client {ConnectionId} unsubscribed from all", Context.ConnectionId);
}
```

### 4.2 修改 `src/Host.Api/Services/TelemetryBroadcastService.cs`

修改广播方法，同时发送到设备组和 all 组：

```csharp
private async Task BroadcastPointAsync(TelemetryPoint point, CancellationToken ct)
{
    try
    {
        var payload = new
        {
            point.DeviceId,
            point.TagId,
            point.Ts,
            point.Quality,
            Value = GetDisplayValue(point),
            ValueType = point.ValueType.ToString()
        };

        // 发送到设备特定组
        var deviceGroup = $"device:{point.DeviceId}";
        await _hubContext.Clients.Group(deviceGroup).SendAsync("ReceiveTelemetry", payload, ct);

        // 同时发送到 "all" 组
        await _hubContext.Clients.Group("all").SendAsync("ReceiveTelemetry", payload, ct);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to broadcast telemetry point");
    }
}
```

### 4.3 修改 `src/Host.Api/Program.cs`

在文件末尾添加：

```csharp
// 使 Program 类可被测试项目访问
public partial class Program { }
```

### 4.4 修改 `intellimaint-ui/src/api/signalr.ts`

保留现有代码，添加以下导出函数：

```typescript
// 订阅指定设备
export const subscribeDevice = async (deviceId: string): Promise<void> => {
  const conn = getConnection();
  if (conn.state === signalR.HubConnectionState.Connected) {
    await conn.invoke('SubscribeDevice', deviceId);
    console.log(`Subscribed to device: ${deviceId}`);
  }
};

// 取消订阅指定设备
export const unsubscribeDevice = async (deviceId: string): Promise<void> => {
  const conn = getConnection();
  if (conn.state === signalR.HubConnectionState.Connected) {
    await conn.invoke('UnsubscribeDevice', deviceId);
    console.log(`Unsubscribed from device: ${deviceId}`);
  }
};

// 订阅所有设备
export const subscribeAll = async (): Promise<void> => {
  const conn = getConnection();
  if (conn.state === signalR.HubConnectionState.Connected) {
    await conn.invoke('SubscribeAll');
    console.log('Subscribed to all devices');
  }
};

// 取消订阅所有
export const unsubscribeAll = async (): Promise<void> => {
  const conn = getConnection();
  if (conn.state === signalR.HubConnectionState.Connected) {
    await conn.invoke('UnsubscribeAll');
    console.log('Unsubscribed from all');
  }
};
```

### 4.5 修改 `intellimaint-ui/src/hooks/useRealTimeData.ts`

重写为支持按设备订阅：

```typescript
import { useEffect, useState, useCallback } from 'react';
import {
  startConnection,
  subscribeDevice,
  unsubscribeDevice,
  subscribeAll,
  unsubscribeAll,
  onReceiveTelemetry,
  offReceiveTelemetry,
} from '../api/signalr';

interface TelemetryData {
  deviceId: string;
  tagId: string;
  ts: number;
  quality: number;
  value: unknown;
  valueType: string;
}

interface UseRealTimeDataOptions {
  deviceIds?: string[];  // 为空则订阅所有
  maxPoints?: number;
}

export const useRealTimeData = (options: UseRealTimeDataOptions = {}) => {
  const { deviceIds, maxPoints = 100 } = options;
  const [data, setData] = useState<TelemetryData[]>([]);
  const [connected, setConnected] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleData = useCallback((point: TelemetryData) => {
    setData((prev) => {
      const updated = [...prev, point];
      return updated.slice(-maxPoints);
    });
  }, [maxPoints]);

  useEffect(() => {
    let mounted = true;

    const setup = async () => {
      try {
        await startConnection();
        if (!mounted) return;

        setConnected(true);
        setError(null);

        if (deviceIds && deviceIds.length > 0) {
          for (const deviceId of deviceIds) {
            await subscribeDevice(deviceId);
          }
        } else {
          await subscribeAll();
        }

        onReceiveTelemetry(handleData);
      } catch (err) {
        if (mounted) {
          setError(err instanceof Error ? err.message : 'Connection failed');
          setConnected(false);
        }
      }
    };

    setup();

    return () => {
      mounted = false;
      offReceiveTelemetry();

      const cleanup = async () => {
        try {
          if (deviceIds && deviceIds.length > 0) {
            for (const deviceId of deviceIds) {
              await unsubscribeDevice(deviceId);
            }
          } else {
            await unsubscribeAll();
          }
        } catch { /* ignore */ }
      };
      cleanup();
    };
  }, [deviceIds?.join(','), handleData]);

  const clearData = useCallback(() => setData([]), []);

  return { data, connected, error, clearData };
};
```

### 4.6 新建 `tests/Integration/IntelliMaint.Tests.Integration.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="8.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.4">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Host.Api\IntelliMaint.Host.Api.csproj" />
  </ItemGroup>

</Project>
```

### 4.7 新建 `tests/Integration/ApiTestFixture.cs`

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace IntelliMaint.Tests.Integration;

public class ApiTestFixture : WebApplicationFactory<Program>
{
    private readonly string _dbPath;

    public ApiTestFixture()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"intellimaint_test_{Guid.NewGuid():N}.db");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sqlite:DatabasePath"] = _dbPath
            });
        });

        builder.UseEnvironment("Testing");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
                File.Delete(_dbPath + "-wal");
                File.Delete(_dbPath + "-shm");
            }
            catch { /* ignore */ }
        }
    }
}
```

### 4.8 新建 `tests/Integration/DeviceApiTests.cs`

```csharp
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace IntelliMaint.Tests.Integration;

public class DeviceApiTests : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public DeviceApiTests(ApiTestFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    [Fact]
    public async Task ListDevices_Empty_ReturnsEmptyList()
    {
        var response = await _client.GetAsync("/api/devices");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<DeviceDto>>>(JsonOptions);
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateDevice_ValidRequest_ReturnsCreatedDevice()
    {
        var request = new { DeviceId = "test-device-001", Name = "Test Device", Protocol = "opcua", Enabled = true };
        var response = await _client.PostAsJsonAsync("/api/devices", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<DeviceDto>>(JsonOptions);
        result!.Success.Should().BeTrue();
        result.Data!.DeviceId.Should().Be("test-device-001");
    }

    [Fact]
    public async Task CreateDevice_DuplicateId_ReturnsBadRequest()
    {
        var request = new { DeviceId = "duplicate-device", Name = "First", Protocol = "opcua" };
        await _client.PostAsJsonAsync("/api/devices", request);

        var response = await _client.PostAsJsonAsync("/api/devices", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetDevice_Exists_ReturnsDevice()
    {
        var deviceId = "get-test-device";
        await _client.PostAsJsonAsync("/api/devices", new { DeviceId = deviceId, Name = "Test", Protocol = "modbus" });

        var response = await _client.GetAsync($"/api/devices/{deviceId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<DeviceDto>>(JsonOptions);
        result!.Data!.DeviceId.Should().Be(deviceId);
    }

    [Fact]
    public async Task GetDevice_NotExists_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/devices/non-existent");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateDevice_Exists_ReturnsUpdatedDevice()
    {
        var deviceId = "update-test-device";
        await _client.PostAsJsonAsync("/api/devices", new { DeviceId = deviceId, Name = "Original", Protocol = "opcua" });

        var response = await _client.PutAsJsonAsync($"/api/devices/{deviceId}", new { Name = "Updated" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<DeviceDto>>(JsonOptions);
        result!.Data!.Name.Should().Be("Updated");
    }

    [Fact]
    public async Task DeleteDevice_Exists_ReturnsOk()
    {
        var deviceId = "delete-test-device";
        await _client.PostAsJsonAsync("/api/devices", new { DeviceId = deviceId, Name = "ToDelete", Protocol = "opcua" });

        var response = await _client.DeleteAsync($"/api/devices/{deviceId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await _client.GetAsync($"/api/devices/{deviceId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private class ApiResponse<T>
    {
        public bool Success { get; set; } = true;
        public T? Data { get; set; }
        public string? Error { get; set; }
    }

    private class DeviceDto
    {
        public string DeviceId { get; set; } = "";
        public string? Name { get; set; }
        public string? Protocol { get; set; }
    }
}
```

### 4.9 新建 `tests/Integration/TagApiTests.cs`

```csharp
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace IntelliMaint.Tests.Integration;

public class TagApiTests : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public TagApiTests(ApiTestFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    [Fact]
    public async Task CreateTag_ValidRequest_ReturnsCreatedTag()
    {
        var deviceId = "tag-test-device";
        await _client.PostAsJsonAsync("/api/devices", new { DeviceId = deviceId, Name = "Test", Protocol = "opcua" });

        var tagRequest = new { TagId = "test-tag-001", DeviceId = deviceId, Name = "Test Tag", DataType = 5 };
        var response = await _client.PostAsJsonAsync("/api/tags", tagRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<TagDto>>(JsonOptions);
        result!.Data!.TagId.Should().Be("test-tag-001");
    }

    [Fact]
    public async Task ListTags_ByDevice_ReturnsDeviceTags()
    {
        var deviceId = "list-tags-device";
        await _client.PostAsJsonAsync("/api/devices", new { DeviceId = deviceId, Name = "Test", Protocol = "modbus" });
        await _client.PostAsJsonAsync("/api/tags", new { TagId = "tag-a", DeviceId = deviceId, DataType = 5 });
        await _client.PostAsJsonAsync("/api/tags", new { TagId = "tag-b", DeviceId = deviceId, DataType = 10 });

        var response = await _client.GetAsync($"/api/tags?deviceId={deviceId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<TagDto>>>(JsonOptions);
        result!.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteTag_Exists_ReturnsOk()
    {
        var deviceId = "delete-tag-device";
        var tagId = "tag-to-delete";
        await _client.PostAsJsonAsync("/api/devices", new { DeviceId = deviceId, Name = "Test", Protocol = "opcua" });
        await _client.PostAsJsonAsync("/api/tags", new { TagId = tagId, DeviceId = deviceId, DataType = 5 });

        var response = await _client.DeleteAsync($"/api/tags/{tagId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await _client.GetAsync($"/api/tags/{tagId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private class ApiResponse<T>
    {
        public bool Success { get; set; } = true;
        public T? Data { get; set; }
        public string? Error { get; set; }
    }

    private class TagDto
    {
        public string TagId { get; set; } = "";
        public string DeviceId { get; set; } = "";
    }
}
```

## 5. 技术约束

- 不使用 `.WithOpenApi()`
- 保留所有现有 using 声明
- 测试使用 xUnit + FluentAssertions
- 临时数据库路径使用 `Path.GetTempPath()`

## 6. 完成后执行

```bash
# 添加测试项目到解决方案
dotnet sln add tests/Integration/IntelliMaint.Tests.Integration.csproj

# 编译验证
dotnet build

# 运行测试
dotnet test
```

## 7. 验收标准

- [ ] `dotnet build` 编译通过
- [ ] `dotnet test` 所有测试通过
- [ ] SignalR 订阅方法可被前端调用
