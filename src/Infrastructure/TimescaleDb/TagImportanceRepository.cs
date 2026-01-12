using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// v61: 标签重要性配置仓储 - TimescaleDB 实现
/// </summary>
public sealed class TagImportanceRepository : ITagImportanceRepository
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<TagImportanceRepository> _logger;

    public TagImportanceRepository(INpgsqlConnectionFactory factory, ILogger<TagImportanceRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TagImportanceConfig>> ListAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT id, pattern, importance, description, priority, enabled, created_utc, updated_utc
            FROM tag_importance_config
            ORDER BY priority DESC, id ASC";

        using var conn = _factory.CreateConnection();
        using var cmd = new NpgsqlCommand(sql, conn);

        var results = new List<TagImportanceConfig>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapConfig(reader));
        }
        return results;
    }

    public async Task<IReadOnlyList<TagImportanceConfig>> ListEnabledAsync(CancellationToken ct)
    {
        const string sql = @"
            SELECT id, pattern, importance, description, priority, enabled, created_utc, updated_utc
            FROM tag_importance_config
            WHERE enabled = true
            ORDER BY priority DESC, id ASC";

        using var conn = _factory.CreateConnection();
        using var cmd = new NpgsqlCommand(sql, conn);

        var results = new List<TagImportanceConfig>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(MapConfig(reader));
        }
        return results;
    }

    public async Task<TagImportanceConfig?> GetAsync(int id, CancellationToken ct)
    {
        const string sql = @"
            SELECT id, pattern, importance, description, priority, enabled, created_utc, updated_utc
            FROM tag_importance_config
            WHERE id = @Id";

        using var conn = _factory.CreateConnection();
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapConfig(reader);
        }
        return null;
    }

    public async Task<int> CreateAsync(TagImportanceConfig config, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        const string sql = @"
            INSERT INTO tag_importance_config (pattern, importance, description, priority, enabled, created_utc, updated_utc)
            VALUES (@Pattern, @Importance, @Description, @Priority, @Enabled, @CreatedUtc, @UpdatedUtc)
            RETURNING id";

        using var conn = _factory.CreateConnection();
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Pattern", config.Pattern);
        cmd.Parameters.AddWithValue("@Importance", (int)config.Importance);
        cmd.Parameters.AddWithValue("@Description", (object?)config.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Priority", config.Priority);
        cmd.Parameters.AddWithValue("@Enabled", config.Enabled);
        cmd.Parameters.AddWithValue("@CreatedUtc", nowUtc);
        cmd.Parameters.AddWithValue("@UpdatedUtc", nowUtc);

        var result = await cmd.ExecuteScalarAsync(ct);
        var id = Convert.ToInt32(result);

        _logger.LogInformation("Created tag importance config {Id}: pattern={Pattern}, importance={Importance}",
            id, config.Pattern, config.Importance);

        return id;
    }

    public async Task UpdateAsync(TagImportanceConfig config, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        const string sql = @"
            UPDATE tag_importance_config
            SET pattern = @Pattern,
                importance = @Importance,
                description = @Description,
                priority = @Priority,
                enabled = @Enabled,
                updated_utc = @UpdatedUtc
            WHERE id = @Id";

        using var conn = _factory.CreateConnection();
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", config.Id);
        cmd.Parameters.AddWithValue("@Pattern", config.Pattern);
        cmd.Parameters.AddWithValue("@Importance", (int)config.Importance);
        cmd.Parameters.AddWithValue("@Description", (object?)config.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Priority", config.Priority);
        cmd.Parameters.AddWithValue("@Enabled", config.Enabled);
        cmd.Parameters.AddWithValue("@UpdatedUtc", nowUtc);

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Updated tag importance config {Id}", config.Id);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        const string sql = "DELETE FROM tag_importance_config WHERE id = @Id";

        using var conn = _factory.CreateConnection();
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id);

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Deleted tag importance config {Id}", id);
    }

    public async Task SetEnabledAsync(int id, bool enabled, CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const string sql = @"
            UPDATE tag_importance_config
            SET enabled = @Enabled, updated_utc = @UpdatedUtc
            WHERE id = @Id";

        using var conn = _factory.CreateConnection();
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@Enabled", enabled);
        cmd.Parameters.AddWithValue("@UpdatedUtc", nowUtc);

        await cmd.ExecuteNonQueryAsync(ct);
        _logger.LogInformation("Set tag importance config {Id} enabled={Enabled}", id, enabled);
    }

    private static TagImportanceConfig MapConfig(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetInt32(reader.GetOrdinal("id")),
        Pattern = reader.GetString(reader.GetOrdinal("pattern")),
        Importance = (TagImportance)reader.GetInt32(reader.GetOrdinal("importance")),
        Description = reader.IsDBNull(reader.GetOrdinal("description"))
            ? null
            : reader.GetString(reader.GetOrdinal("description")),
        Priority = reader.GetInt32(reader.GetOrdinal("priority")),
        Enabled = reader.GetBoolean(reader.GetOrdinal("enabled")),
        CreatedUtc = reader.GetInt64(reader.GetOrdinal("created_utc")),
        UpdatedUtc = reader.GetInt64(reader.GetOrdinal("updated_utc"))
    };
}
