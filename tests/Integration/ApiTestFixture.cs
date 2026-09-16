using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IntelliMaint.Tests.Integration;

public class ApiTestFixture : WebApplicationFactory<Program>
{
    private readonly string _dbPath;

    public ApiTestFixture()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"intellimaint_test_{Guid.NewGuid():N}.db");
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Host configuration is available to minimal Program before service registration.
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseProvider"] = "Sqlite",
                ["Demo:Enabled"] = "false",
                ["Jwt:SecretKey"] = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                ["ADMIN_USERNAME"] = "test_admin",
                ["ADMIN_PASSWORD"] = Guid.NewGuid().ToString("N") + "aA1!",
                ["Edge:DatabasePath"] = _dbPath,  // Fixed: EdgeOptions uses "Edge" section
                ["Edge:EdgeId"] = "test-edge"     // Required property
            });
        });
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Configure test authentication to bypass JWT
        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, options => { });
        });
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
