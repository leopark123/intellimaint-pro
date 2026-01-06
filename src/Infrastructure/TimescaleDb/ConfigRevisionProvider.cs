using Dapper;
using IntelliMaint.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB Config revision provider - tracks configuration changes
/// </summary>
public sealed class ConfigRevisionProvider : IConfigRevisionProvider
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<ConfigRevisionProvider> _logger;

    public ConfigRevisionProvider(INpgsqlConnectionFactory factory, ILogger<ConfigRevisionProvider> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<long> GetRevisionAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT COALESCE(
                (SELECT MAX(updated_utc) FROM device),
                (SELECT MAX(updated_utc) FROM tag),
                (SELECT MAX(updated_utc) FROM alarm_rule),
                0
            )";

        using var conn = _factory.CreateConnection();
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task<bool> HasChangedSinceAsync(long lastKnownRevision, CancellationToken ct)
    {
        var currentRevision = await GetRevisionAsync(ct);
        return currentRevision > lastKnownRevision;
    }

    public async Task IncrementRevisionAsync(CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        const string sql = @"
            INSERT INTO system_setting (key, value, updated_utc)
            VALUES ('config_revision', @Value, @UpdatedUtc)
            ON CONFLICT (key) DO UPDATE SET
                value = EXCLUDED.value,
                updated_utc = EXCLUDED.updated_utc";

        using var conn = _factory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(sql, new
        {
            Value = nowUtc.ToString(),
            UpdatedUtc = nowUtc
        }, cancellationToken: ct));

        _logger.LogDebug("Incremented config revision to {Revision}", nowUtc);
    }
}
