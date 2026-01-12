using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
                ["Edge:DatabasePath"] = _dbPath,  // Fixed: EdgeOptions uses "Edge" section
                ["Edge:EdgeId"] = "test-edge"     // Required property
            });
        });

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
