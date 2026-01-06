using Microsoft.Extensions.Configuration;
using Npgsql;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB/PostgreSQL 连接工厂接口
/// </summary>
public interface INpgsqlConnectionFactory
{
    /// <summary>创建并打开一个新的数据库连接</summary>
    NpgsqlConnection CreateConnection();

    /// <summary>获取连接字符串</summary>
    string ConnectionString { get; }
}

/// <summary>
/// TimescaleDB 连接工厂实现
/// </summary>
public sealed class TimescaleDbConnectionFactory : INpgsqlConnectionFactory
{
    private readonly string _connectionString;

    public TimescaleDbConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("TimescaleDb")
            ?? throw new InvalidOperationException("Missing TimescaleDb connection string");

        // 验证连接字符串格式
        var builder = new NpgsqlConnectionStringBuilder(_connectionString);

        // 设置默认连接池参数
        if (!builder.ContainsKey("Minimum Pool Size"))
            builder.MinPoolSize = 5;
        if (!builder.ContainsKey("Maximum Pool Size"))
            builder.MaxPoolSize = 100;
        if (!builder.ContainsKey("Connection Idle Lifetime"))
            builder.ConnectionIdleLifetime = 300;
        if (!builder.ContainsKey("Connection Pruning Interval"))
            builder.ConnectionPruningInterval = 10;
        if (!builder.ContainsKey("Timeout"))
            builder.Timeout = 30;
        if (!builder.ContainsKey("Command Timeout"))
            builder.CommandTimeout = 60;

        _connectionString = builder.ToString();
    }

    public string ConnectionString => _connectionString;

    public NpgsqlConnection CreateConnection()
    {
        var conn = new NpgsqlConnection(_connectionString);
        conn.Open();
        return conn;
    }
}
