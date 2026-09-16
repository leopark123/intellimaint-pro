using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Host.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace IntelliMaint.Tests.Integration;

// Uses the actual JWT/Edge handlers, unlike the CRUD fixture's test administrator.
public sealed class DemoSecurityTests
{
    [Fact]
    public async Task FreshDemoRequiresAuthAndProvidesTelemetryAlarmAndHealth()
    {
        using var factory = new DemoFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        foreach (var path in new[] { "/api/devices", "/api/edge-config/", "/api/telemetry/latest" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/health/snapshot", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/edge-config/test/heartbeat", new { })).StatusCode);

        var users = factory.Services.GetRequiredService<IUserRepository>();
        var accounts = await users.ListAsync(default);
        Assert.Single(accounts);
        Assert.Equal("test_admin", accounts[0].Username);
        Assert.Null(await users.GetByUsernameAsync("admin", default));
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username = "test_admin", password = factory.Password });
        login.EnsureSuccessStatusCode();
        var payload = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", payload.GetProperty("data").GetProperty("token").GetString());

        var telemetry = factory.Services.GetRequiredService<ITelemetryRepository>();
        var alarms = factory.Services.GetRequiredService<IAlarmRepository>();
        for (var i = 0; i < 100 && await alarms.GetOpenCountAsync(SyntheticDemoService.DeviceId, default) == 0; i++)
            await Task.Delay(100);
        Assert.Equal(5, (await telemetry.GetLatestAsync(SyntheticDemoService.DeviceId, null, default)).Count);
        Assert.True(await alarms.GetOpenCountAsync(SyntheticDemoService.DeviceId, default) > 0);
        var latest = await client.GetAsync("/api/telemetry/latest?deviceId=" + SyntheticDemoService.DeviceId);
        latest.EnsureSuccessStatusCode();
        var trend = await client.GetAsync("/api/telemetry/query?deviceId=" + SyntheticDemoService.DeviceId);
        Assert.True(trend.IsSuccessStatusCode, await trend.Content.ReadAsStringAsync());
        var health = await client.GetAsync("/api/health-assessment/devices/" + SyntheticDemoService.DeviceId);
        health.EnsureSuccessStatusCode();

        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("X-Edge-Key", "incorrect");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/edge-config/")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Edge-Key");
        client.DefaultRequestHeaders.Add("X-Edge-Key", factory.EdgeKey);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/edge-config/")).StatusCode);
        // A status credential must never grant account administration or configuration writes.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/edge-config/test", new { })).StatusCode);
    }

    private sealed class DemoFactory : WebApplicationFactory<Program>
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"intellimaint-demo-{Guid.NewGuid():N}.db");
        public string Password { get; } = Guid.NewGuid().ToString("N");
        public string EdgeKey { get; } = Guid.NewGuid().ToString("N");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
        }
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProvider"] = "Sqlite", ["Demo:Enabled"] = "true", ["Edge:DatabasePath"] = _path,
                ["Jwt:SecretKey"] = Guid.NewGuid().ToString("N"), ["Edge:ApiKey"] = EdgeKey,
                ["ADMIN_USERNAME"] = "test_admin", ["ADMIN_PASSWORD"] = Password
            }));
            return base.CreateHost(builder);
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(_path + suffix);
        }
    }
}
