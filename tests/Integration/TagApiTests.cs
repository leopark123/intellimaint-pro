using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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
