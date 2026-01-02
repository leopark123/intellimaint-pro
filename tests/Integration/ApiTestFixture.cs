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
