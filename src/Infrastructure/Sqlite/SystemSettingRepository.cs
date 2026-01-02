using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Data.Sqlite;

namespace IntelliMaint.Infrastructure.Sqlite;

public sealed class SystemSettingRepository : ISystemSettingRepository
{
    private readonly IDbExecutor _db;

    public SystemSettingRepository(IDbExecutor db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<SystemSetting>> GetAllAsync(CancellationToken ct)
    {
        const string sql = "SELECT key, value, updated_utc FROM system_setting ORDER BY key;";
        return await _db.QueryAsync(sql, MapSetting, null, ct);
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        const string sql = "SELECT value FROM system_setting WHERE key = @Key;";
        return await _db.ExecuteScalarAsync<string>(sql, new { Key = key }, ct);
    }

    public async Task SetAsync(string key, string value, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO system_setting (key, value, updated_utc) VALUES (@Key, @Value, @UpdatedUtc)
ON CONFLICT(key) DO UPDATE SET value = @Value, updated_utc = @UpdatedUtc;";

        await _db.ExecuteNonQueryAsync(sql, new
        {
            Key = key,
            Value = value,
            UpdatedUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }, ct);
    }

    private static SystemSetting MapSetting(SqliteDataReader reader)
    {
        return new SystemSetting
        {
            Key = reader.GetString(0),
            Value = reader.GetString(1),
            UpdatedUtc = reader.GetInt64(2)
        };
    }
}
