using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace IntelliMaint.Tests.Integration;

// Program configures the real console/file logger. Keep console capture isolated from other tests.
[CollectionDefinition("Request logging", DisableParallelization = true)]
public sealed class RequestLoggingCollection { }

[Collection("Request logging")]
public sealed class RequestLoggingTests
{
    [Fact]
    public async Task SignalRRequestsRedactKnownJwtAndPreserveUsefulRequestLogs()
    {
        var originalOutput = Console.Out;
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        Console.SetOut(TextWriter.Synchronized(output));
        try
        {
            using var factory = new LoggingFactory();
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });
            var login = await client.PostAsJsonAsync("/api/auth/login",
                new { username = "logging_test", password = factory.Password });
            login.EnsureSuccessStatusCode();
            var payload = await login.Content.ReadFromJsonAsync<JsonElement>();
            var knownTestJwt = payload.GetProperty("data").GetProperty("token").GetString()!;
            const string refreshMarker = "logging-regression-refresh-value";
            const string apiKeyMarker = "logging-regression-api-key-value";

            // A successful real negotiation proves redaction did not alter the authentication input.
            var response = await client.PostAsync("/hubs/telemetry/negotiate?negotiateVersion=1" +
                "&access_token=" + Uri.EscapeDataString(knownTestJwt) +
                "&refreshToken=" + refreshMarker + "&API_KEY=" + apiKeyMarker +
                "&probe=public-observation", new StringContent(""));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var ordinaryRequest = await client.GetAsync("/health/live?probe=ordinary-request");
            Assert.Equal(HttpStatusCode.OK, ordinaryRequest.StatusCode);

            var logs = output.ToString();
            // Do not include secret-bearing captured output in assertion failure messages.
            Assert.False(logs.Contains(knownTestJwt, StringComparison.Ordinal), "JWT appeared in application logs.");
            Assert.False(logs.Contains(refreshMarker, StringComparison.Ordinal), "Refresh value appeared in application logs.");
            Assert.False(logs.Contains(apiKeyMarker, StringComparison.Ordinal), "API key appeared in application logs.");
            Assert.True(logs.Contains("Request starting", StringComparison.Ordinal), "Request-start logs are missing.");
            Assert.True(logs.Contains("Request finished", StringComparison.Ordinal), "Request-finish logs are missing.");
            Assert.True(logs.Contains("REDACTED", StringComparison.Ordinal), "Redaction marker is missing.");
            Assert.True(logs.Contains("probe=public-observation", StringComparison.Ordinal), "Non-sensitive query values were lost.");
            Assert.True(logs.Contains("probe=ordinary-request", StringComparison.Ordinal), "Ordinary request logging was lost.");
            Assert.True(logs.Contains("/hubs/telemetry/negotiate", StringComparison.Ordinal), "Request path is missing.");
        }
        finally
        {
            Console.SetOut(originalOutput);
        }
    }

    private sealed class LoggingFactory : WebApplicationFactory<Program>
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"intellimaint-log-test-{Guid.NewGuid():N}.db");
        public string Password { get; } = Guid.NewGuid().ToString("N");

        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProvider"] = "Sqlite", ["Demo:Enabled"] = "false", ["Edge:DatabasePath"] = _path,
                ["Jwt:SecretKey"] = Guid.NewGuid().ToString("N"),
                ["ADMIN_USERNAME"] = "logging_test", ["ADMIN_PASSWORD"] = Password
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
