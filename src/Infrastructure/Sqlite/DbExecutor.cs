using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// 数据库执行器接口
/// </summary>
public interface IDbExecutor
{
    /// <summary>执行非查询命令（写操作，串行化）</summary>
    Task<int> ExecuteNonQueryAsync(string sql, object? parameters = null, CancellationToken ct = default);
    
    /// <summary>执行标量查询</summary>
    Task<T?> ExecuteScalarAsync<T>(string sql, object? parameters = null, CancellationToken ct = default);
    
    /// <summary>执行查询，返回结果列表</summary>
    Task<List<T>> QueryAsync<T>(string sql, Func<SqliteDataReader, T> mapper, object? parameters = null, CancellationToken ct = default);
    
    /// <summary>执行查询，返回单个结果</summary>
    Task<T?> QuerySingleAsync<T>(string sql, Func<SqliteDataReader, T> mapper, object? parameters = null, CancellationToken ct = default);
    
    /// <summary>在事务中执行多个操作（写操作，串行化）</summary>
    Task ExecuteInTransactionAsync(Func<SqliteConnection, SqliteTransaction, Task> action, CancellationToken ct = default);
    
    /// <summary>批量执行（写操作，串行化）</summary>
    Task<int> ExecuteBatchAsync(string sql, IEnumerable<object> parametersList, CancellationToken ct = default);
}

/// <summary>
/// 数据库执行器实现
/// 遵循技术宪法：写入必须串行化（单写者）
/// </summary>
public sealed class DbExecutor : IDbExecutor
{
    private readonly ISqliteConnectionFactory _factory;
    private readonly ILogger<DbExecutor> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    
    public DbExecutor(ISqliteConnectionFactory factory, ILogger<DbExecutor> logger)
    {
        _factory = factory;
        _logger = logger;
    }
    
    /// <summary>
    /// 执行非查询命令（写操作）
    /// 使用写锁确保串行化
    /// </summary>
    public async Task<int> ExecuteNonQueryAsync(string sql, object? parameters = null, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            
            if (parameters != null)
                AddParameters(cmd, parameters);
            
            return await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecuteNonQuery failed: {Sql}", TruncateSql(sql));
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }
    
    /// <summary>
    /// 执行标量查询（读操作，不需要锁）
    /// </summary>
    public async Task<T?> ExecuteScalarAsync<T>(string sql, object? parameters = null, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        
        if (parameters != null)
            AddParameters(cmd, parameters);
        
        var result = await cmd.ExecuteScalarAsync(ct);
        
        if (result == null || result == DBNull.Value)
            return default;
        
        // Handle nullable types - Convert.ChangeType doesn't support Nullable<T>
        var targetType = typeof(T);
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        var convertedValue = Convert.ChangeType(result, underlyingType);
        return (T)convertedValue;
    }
    
    /// <summary>
    /// 执行查询（读操作，不需要锁）
    /// </summary>
    public async Task<List<T>> QueryAsync<T>(string sql, Func<SqliteDataReader, T> mapper, object? parameters = null, CancellationToken ct = default)
    {
        var results = new List<T>();
        
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        
        if (parameters != null)
            AddParameters(cmd, parameters);
        
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(mapper(reader));
        }
        
        return results;
    }
    
    /// <summary>
    /// 执行查询，返回单个结果
    /// </summary>
    public async Task<T?> QuerySingleAsync<T>(string sql, Func<SqliteDataReader, T> mapper, object? parameters = null, CancellationToken ct = default)
    {
        using var conn = _factory.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        
        if (parameters != null)
            AddParameters(cmd, parameters);
        
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return mapper(reader);
        }
        
        return default;
    }
    
    /// <summary>
    /// 在事务中执行操作（写操作）
    /// 确保所有命令绑定事务对象
    /// </summary>
    public async Task ExecuteInTransactionAsync(Func<SqliteConnection, SqliteTransaction, Task> action, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            using var conn = _factory.CreateConnection();
            using var transaction = conn.BeginTransaction();
            
            try
            {
                await action(conn, transaction);
                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transaction failed");
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
    }
    
    /// <summary>
    /// 批量执行（写操作，在单个事务中）
    /// </summary>
    public async Task<int> ExecuteBatchAsync(string sql, IEnumerable<object> parametersList, CancellationToken ct = default)
    {
        var totalAffected = 0;
        
        await _writeLock.WaitAsync(ct);
        try
        {
            using var conn = _factory.CreateConnection();
            using var transaction = conn.BeginTransaction();
            
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                cmd.Transaction = transaction;  // 关键：命令必须绑定事务
                
                foreach (var parameters in parametersList)
                {
                    cmd.Parameters.Clear();
                    AddParameters(cmd, parameters);
                    totalAffected += await cmd.ExecuteNonQueryAsync(ct);
                }
                
                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch execution failed: {Sql}", TruncateSql(sql));
            throw;
        }
        finally
        {
            _writeLock.Release();
        }
        
        return totalAffected;
    }
    
    /// <summary>
    /// 添加参数到命令
    /// 支持匿名对象和字典
    /// </summary>
    private static void AddParameters(SqliteCommand cmd, object parameters)
    {
        // 支持 Dictionary<string, object> 和 Dictionary<string, object?>
        if (parameters is System.Collections.IDictionary dict)
        {
            foreach (System.Collections.DictionaryEntry kvp in dict)
            {
                var key = kvp.Key?.ToString() ?? string.Empty;
                cmd.Parameters.AddWithValue($"@{key}", kvp.Value ?? DBNull.Value);
            }
        }
        else
        {
            // 匿名对象或普通对象
            var properties = parameters.GetType().GetProperties();
            foreach (var prop in properties)
            {
                var value = prop.GetValue(parameters);
                cmd.Parameters.AddWithValue($"@{prop.Name}", value ?? DBNull.Value);
            }
        }
    }
    
    private static string TruncateSql(string sql)
    {
        return sql.Length > 100 ? sql[..100] + "..." : sql;
    }
}
