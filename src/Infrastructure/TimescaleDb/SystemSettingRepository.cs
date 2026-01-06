using Dapper;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB SystemSetting repository implementation
/// </summary>
public sealed class SystemSettingRepository : ISystemSettingRepository
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<SystemSettingRepository> _logger;

    public SystemSettingRepository(INpgsqlConnectionFactory factory, ILogger<SystemSettingRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SystemSetting>> GetAllAsync(CancellationToken ct)
    {
        const string sql = "SELECT key, value, updated_utc FROM system_setting ORDER BY key";

        using var conn = _factory.CreateConnection();
        var rows = await conn.QueryAsync<SettingRow>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(r => new SystemSetting
        {
            Key = r.key,
            Value = r.value,
            UpdatedUtc = r.updated_utc
        }).ToList();
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        const string sql = "SELECT value FROM system_setting WHERE key = @Key";

        using var conn = _factory.CreateConnection();
        return await conn.ExecuteScalarAsync<string?>(
            new CommandDefinition(sql, new { Key = key }, cancellationToken: ct));
    }

    public async Task SetAsync(string key, string value, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        const string sql = @"
            INSERT INTO system_setting (key, value, updated_utc)
            VALUES (@Key, @Value, @UpdatedUtc)
            ON CONFLICT (key) DO UPDATE SET
                value = EXCLUDED.value,
                updated_utc = EXCLUDED.updated_utc";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            Key = key,
            Value = value,
            UpdatedUtc = nowUtc
        }, cancellationToken: ct));

        _logger.LogDebug("Set system setting {Key}", key);
    }

    private sealed class SettingRow
    {
        public string key { get; set; } = "";
        public string value { get; set; } = "";
        public long updated_utc { get; set; }
    }
}
