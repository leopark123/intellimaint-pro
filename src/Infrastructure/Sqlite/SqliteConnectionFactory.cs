using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// SQLite 连接工厂接口
/// </summary>
public interface ISqliteConnectionFactory
{
    /// <summary>创建新连接（调用方负责释放）</summary>
    SqliteConnection CreateConnection();
    
    /// <summary>数据库文件路径</summary>
    string DatabasePath { get; }
}

/// <summary>
/// SQLite 连接工厂实现
/// 遵循技术宪法：禁止共享单例连接，每次操作创建新连接
/// </summary>
public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteConnectionFactory> _logger;
    
    public string DatabasePath { get; }
    
    public SqliteConnectionFactory(IOptions<EdgeOptions> options, ILogger<SqliteConnectionFactory> logger)
    {
        DatabasePath = options.Value.DatabasePath;
        _connectionString = BuildConnectionString(DatabasePath);
        _logger = logger;
        
        // 确保目录存在
        var dir = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            _logger.LogInformation("Created database directory: {Directory}", dir);
        }
    }
    
    /// <summary>
    /// 创建新的数据库连接
    /// 每次调用返回新连接，调用方必须使用 using 释放
    /// </summary>
    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        
        // 应用 PRAGMA 设置（每个连接都需要）
        ApplyPragmas(connection);
        
        return connection;
    }
    
    private static string BuildConnectionString(string dbPath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true  // 启用连接池
        };
        return builder.ConnectionString;
    }
    
    private void ApplyPragmas(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA temp_store = MEMORY;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            PRAGMA cache_size = 10000;
        ";
        cmd.ExecuteNonQuery();
    }
}
