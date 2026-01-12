using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace IntelliMaint.Tests.Integration;

/// <summary>
/// v56: 告警规则 API 集成测试
/// 覆盖阈值告警、离线检测、变化率告警三种类型
/// </summary>
public class AlarmRuleApiTests : IClassFixture<ApiTestFixture>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AlarmRuleApiTests(ApiTestFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    #region 基础 CRUD 测试

    [Fact]
    public async Task ListRules_ReturnsOkWithEmptyList()
    {
        var response = await _client.GetAsync("/api/alarm-rules");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<AlarmRuleDto>>>(JsonOptions);
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateRule_ThresholdRule_Success()
    {
        var request = new
        {
            RuleId = "test-threshold-001",
            Name = "温度过高告警",
            TagId = "reactor.temperature",
            ConditionType = "gt",
            Threshold = 100.0,
            Severity = 3
        };

        var response = await _client.PostAsJsonAsync("/api/alarm-rules", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<AlarmRuleDto>>(JsonOptions);
        result!.Success.Should().BeTrue();
        result.Data!.RuleId.Should().Be("test-threshold-001");
        result.Data.ConditionType.Should().Be("gt");
        result.Data.RuleType.Should().Be("threshold");
    }

    [Fact]
    public async Task CreateRule_DuplicateId_ReturnsBadRequest()
    {
        var request = new
        {
            RuleId = "duplicate-rule",
            Name = "重复规则",
            TagId = "test.tag",
            ConditionType = "gt",
            Threshold = 50.0
        };

        await _client.PostAsJsonAsync("/api/alarm-rules", request);
        var response = await _client.PostAsJsonAsync("/api/alarm-rules", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetRule_Exists_ReturnsRule()
    {
        var ruleId = "get-test-rule";
        await _client.PostAsJsonAsync("/api/alarm-rules", new
        {
            RuleId = ruleId,
            Name = "测试规则",
            TagId = "test.tag",
            ConditionType = "lt",
            Threshold = 10.0
        });

        var response = await _client.GetAsync($"/api/alarm-rules/{ruleId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<AlarmRuleDto>>(JsonOptions);
        result!.Data!.RuleId.Should().Be(ruleId);
    }

    [Fact]
    public async Task GetRule_NotExists_ReturnsNotFound()
    {
        var response = await _client.GetAsync("/api/alarm-rules/non-existent");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteRule_Exists_ReturnsOk()
    {
        var ruleId = "delete-test-rule";
        await _client.PostAsJsonAsync("/api/alarm-rules", new
        {
            RuleId = ruleId,
            Name = "待删除规则",
            TagId = "test.tag",
            ConditionType = "eq",
            Threshold = 0.0
        });

        var response = await _client.DeleteAsync($"/api/alarm-rules/{ruleId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse = await _client.GetAsync($"/api/alarm-rules/{ruleId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region v56: 离线检测规则测试

    [Fact]
    public async Task CreateRule_OfflineRule_Success()
    {
        var request = new
        {
            RuleId = "offline-pump-001",
            Name = "水泵离线检测",
            TagId = "pump1.status",
            ConditionType = "offline",
            Threshold = 30.0,  // 30秒超时
            Severity = 4
        };

        var response = await _client.PostAsJsonAsync("/api/alarm-rules", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<AlarmRuleDto>>(JsonOptions);
        result!.Success.Should().BeTrue();
        result.Data!.RuleId.Should().Be("offline-pump-001");
        result.Data.ConditionType.Should().Be("offline");
        result.Data.RuleType.Should().Be("offline");
        result.Data.Threshold.Should().Be(30.0);
    }

    [Fact]
    public async Task CreateRule_OfflineRule_InvalidThreshold_ReturnsBadRequest()
    {
        var request = new
        {
            RuleId = "offline-invalid-001",
            Name = "无效离线规则",
            TagId = "pump1.status",
            ConditionType = "offline",
            Threshold = -10.0  // 无效：必须为正数
        };

        var response = await _client.PostAsJsonAsync("/api/alarm-rules", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<AlarmRuleDto>>(JsonOptions);
        result!.Success.Should().BeFalse();
        result.Error.Should().Contain("正数");
    }

    [Fact]
    public async Task CreateRule_OfflineRule_ZeroThreshold_ReturnsBadRequest()
    {
        var request = new
        {
            RuleId = "offline-zero-001",
            Name = "零超时规则",
            TagId = "pump1.status",
            ConditionType = "offline",
            Threshold = 0.0  // 无效：必须为正数
        };

        var response = await _client.PostAsJsonAsync("/api/alarm-rules", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region v56: 变化率告警规则测试

    [Fact]
    public async Task CreateRule_RocPercentRule_Success()
    {
        var request = new
        {
            RuleId = "roc-temp-001",
            Name = "温度突变检测",
            TagId = "reactor.temperature",
            ConditionType = "roc_percent",
            Threshold = 15.0,  // 15% 变化
            RocWindowMs = 300000,  // 5分钟窗口
            Severity = 3
        };

        var response = await _client.PostAsJsonAsync("/api/alarm-rules", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<AlarmRuleDto>>(JsonOptions);
        result!.Success.Should().BeTrue();
        result.Data!.RuleId.Should().Be("roc-temp-001");
        result.Data.ConditionType.Should().Be("roc_percent");
        result.Data.RuleType.Should().Be("roc");
        result.Data.RocWindowMs.Should().Be(300000);
    }

    [Fact]
    public async Task CreateRule_RocAbsoluteRule_Success()
    {
        var request = new
        {
            RuleId = "roc-pressure-001",
            Name = "压力突变检测",
            TagId = "tank.pressure",
            ConditionType = "roc_absolute",
            Threshold = 50.0,  // 50单位变化
            RocWindowMs = 60000,  // 1分钟窗口
            Severity = 4
        };

        var response = await _client.PostAsJsonAsync("/api/alarm-rules", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<AlarmRuleDto>>(JsonOptions);
        result!.Success.Should().BeTrue();
        result.Data!.ConditionType.Should().Be("roc_absolute");
        result.Data.RuleType.Should().Be("roc");
    }

    [Fact]
    public async Task CreateRule_RocRule_MissingWindow_ReturnsBadRequest()
    {
        var request = new
        {
            RuleId = "roc-nowindow-001",
            Name = "缺少窗口的变化率规则",
            TagId = "sensor.value",
            ConditionType = "roc_percent",
            Threshold = 10.0
            // 缺少 RocWindowMs
        };

        var response = await _client.PostAsJsonAsync("/api/alarm-rules", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<AlarmRuleDto>>(JsonOptions);
        result!.Success.Should().BeFalse();
        result.Error.Should().Contain("RocWindowMs");
    }

    [Fact]
    public async Task CreateRule_RocRule_ZeroWindow_ReturnsBadRequest()
    {
        var request = new
        {
            RuleId = "roc-zerowindow-001",
            Name = "零窗口的变化率规则",
            TagId = "sensor.value",
            ConditionType = "roc_percent",
            Threshold = 10.0,
            RocWindowMs = 0  // 无效：必须为正数
        };

        var response = await _client.PostAsJsonAsync("/api/alarm-rules", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateRule_RocRule_WindowTooLarge_ReturnsBadRequest()
    {
        var request = new
        {
            RuleId = "roc-bigwindow-001",
            Name = "超大窗口的变化率规则",
            TagId = "sensor.value",
            ConditionType = "roc_percent",
            Threshold = 10.0,
            RocWindowMs = 7200000  // 2小时，超过1小时限制
        };

        var response = await _client.PostAsJsonAsync("/api/alarm-rules", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<AlarmRuleDto>>(JsonOptions);
        result!.Error.Should().Contain("3600000");
    }

    #endregion

    #region 验证测试

    [Theory]
    [InlineData("gt")]
    [InlineData("gte")]
    [InlineData("lt")]
    [InlineData("lte")]
    [InlineData("eq")]
    [InlineData("ne")]
    [InlineData("offline")]
    [InlineData("roc_percent")]
    [InlineData("roc_absolute")]
    public async Task CreateRule_AllValidConditionTypes_Accepted(string conditionType)
    {
        var request = new
        {
            RuleId = $"valid-cond-{conditionType}",
            Name = $"条件类型测试-{conditionType}",
            TagId = "test.tag",
            ConditionType = conditionType,
            Threshold = conditionType == "offline" ? 30.0 : 100.0,
            RocWindowMs = conditionType.StartsWith("roc_") ? 60000 : (int?)null
        };

        var response = await _client.PostAsJsonAsync("/api/alarm-rules", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"条件类型 '{conditionType}' 应该被接受");
    }

    [Fact]
    public async Task CreateRule_InvalidConditionType_ReturnsBadRequest()
    {
        var request = new
        {
            RuleId = "invalid-cond-001",
            Name = "无效条件类型",
            TagId = "test.tag",
            ConditionType = "invalid_type",
            Threshold = 100.0
        };

        var response = await _client.PostAsJsonAsync("/api/alarm-rules", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<AlarmRuleDto>>(JsonOptions);
        result!.Error.Should().Contain("ConditionType");
    }

    [Fact]
    public async Task CreateRule_MissingRequiredFields_ReturnsBadRequest()
    {
        var request = new
        {
            RuleId = "missing-fields"
            // 缺少 Name, TagId, ConditionType, Threshold
        };

        var response = await _client.PostAsJsonAsync("/api/alarm-rules", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateRule_InvalidSeverity_ReturnsBadRequest()
    {
        var request = new
        {
            RuleId = "invalid-severity-001",
            Name = "无效严重度",
            TagId = "test.tag",
            ConditionType = "gt",
            Threshold = 100.0,
            Severity = 10  // 无效：必须 1-5
        };

        var response = await _client.PostAsJsonAsync("/api/alarm-rules", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<ApiResponse<AlarmRuleDto>>(JsonOptions);
        result!.Error.Should().Contain("Severity");
    }

    #endregion

    #region 更新规则测试

    [Fact]
    public async Task UpdateRule_ChangeConditionType_UpdatesRuleType()
    {
        // 创建阈值规则
        var ruleId = "update-type-001";
        await _client.PostAsJsonAsync("/api/alarm-rules", new
        {
            RuleId = ruleId,
            Name = "原始规则",
            TagId = "test.tag",
            ConditionType = "gt",
            Threshold = 100.0
        });

        // 更新为离线规则
        var updateResponse = await _client.PutAsJsonAsync($"/api/alarm-rules/{ruleId}", new
        {
            ConditionType = "offline",
            Threshold = 30.0
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 验证规则类型已更新
        var getResponse = await _client.GetAsync($"/api/alarm-rules/{ruleId}");
        var result = await getResponse.Content.ReadFromJsonAsync<ApiResponse<AlarmRuleDto>>(JsonOptions);
        result!.Data!.ConditionType.Should().Be("offline");
        result.Data.RuleType.Should().Be("offline");
    }

    [Fact]
    public async Task UpdateRule_AddRocWindow_Success()
    {
        // 创建阈值规则
        var ruleId = "update-roc-001";
        await _client.PostAsJsonAsync("/api/alarm-rules", new
        {
            RuleId = ruleId,
            Name = "原始规则",
            TagId = "test.tag",
            ConditionType = "gt",
            Threshold = 100.0
        });

        // 更新为变化率规则
        var updateResponse = await _client.PutAsJsonAsync($"/api/alarm-rules/{ruleId}", new
        {
            ConditionType = "roc_percent",
            RocWindowMs = 120000
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 验证
        var getResponse = await _client.GetAsync($"/api/alarm-rules/{ruleId}");
        var result = await getResponse.Content.ReadFromJsonAsync<ApiResponse<AlarmRuleDto>>(JsonOptions);
        result!.Data!.ConditionType.Should().Be("roc_percent");
        result.Data.RuleType.Should().Be("roc");
        result.Data.RocWindowMs.Should().Be(120000);
    }

    #endregion

    #region 启用/禁用测试

    [Fact]
    public async Task EnableDisableRule_Success()
    {
        var ruleId = "enable-disable-001";
        await _client.PostAsJsonAsync("/api/alarm-rules", new
        {
            RuleId = ruleId,
            Name = "启用禁用测试",
            TagId = "test.tag",
            ConditionType = "gt",
            Threshold = 100.0,
            Enabled = true
        });

        // 禁用
        var disableResponse = await _client.PutAsync($"/api/alarm-rules/{ruleId}/disable", null);
        disableResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse1 = await _client.GetAsync($"/api/alarm-rules/{ruleId}");
        var result1 = await getResponse1.Content.ReadFromJsonAsync<ApiResponse<AlarmRuleDto>>(JsonOptions);
        result1!.Data!.Enabled.Should().BeFalse();

        // 启用
        var enableResponse = await _client.PutAsync($"/api/alarm-rules/{ruleId}/enable", null);
        enableResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResponse2 = await _client.GetAsync($"/api/alarm-rules/{ruleId}");
        var result2 = await getResponse2.Content.ReadFromJsonAsync<ApiResponse<AlarmRuleDto>>(JsonOptions);
        result2!.Data!.Enabled.Should().BeTrue();
    }

    #endregion

    #region DTOs

    private class ApiResponse<T>
    {
        public bool Success { get; set; } = true;
        public T? Data { get; set; }
        public string? Error { get; set; }
    }

    private class AlarmRuleDto
    {
        public string RuleId { get; set; } = "";
        public string? Name { get; set; }
        public string? TagId { get; set; }
        public string? DeviceId { get; set; }
        public string ConditionType { get; set; } = "";
        public double Threshold { get; set; }
        public int DurationMs { get; set; }
        public int Severity { get; set; }
        public string? MessageTemplate { get; set; }
        public bool Enabled { get; set; }
        public int RocWindowMs { get; set; }
        public string RuleType { get; set; } = "";
    }

    #endregion
}
