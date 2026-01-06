using Dapper;
using IntelliMaint.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB Schema manager - verifies schema exists (schema created by Docker init scripts)
/// </summary>
public sealed class SchemaManager
{
    private readonly INpgsqlConnectionFactory _factory;
    private readonly ILogger<SchemaManager> _logger;

    public SchemaManager(INpgsqlConnectionFactory factory, ILogger<SchemaManager> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Verifying TimescaleDB schema...");

        try
        {
            using var conn = _factory.CreateConnection();

            // Verify schema version table exists
            var version = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
                "SELECT version FROM schema_version ORDER BY version DESC LIMIT 1",
                cancellationToken: ct));

            if (version.HasValue)
            {
                _logger.LogInformation("TimescaleDB schema verified. Version: {Version}", version.Value);
            }
            else
            {
                _logger.LogWarning("TimescaleDB schema_version table is empty. Schema may not be properly initialized.");
            }

            // Verify key tables exist
            var tableCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public'",
                cancellationToken: ct));

            _logger.LogInformation("Found {TableCount} tables in TimescaleDB", tableCount);

            // Verify hypertables
            var hypertableCount = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM timescaledb_information.hypertables",
                cancellationToken: ct));

            _logger.LogInformation("Found {HypertableCount} hypertables", hypertableCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify TimescaleDB schema. Ensure database is initialized with Docker init scripts.");
            throw new InvalidOperationException(
                "TimescaleDB schema verification failed. Please ensure the database container is running and initialized.",
                ex);
        }
    }
}
