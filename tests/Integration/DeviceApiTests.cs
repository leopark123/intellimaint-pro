using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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
