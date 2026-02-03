using FluentAssertions;
using Xunit;

namespace IntelliMaint.Tests.Unit;

/// <summary>
/// 连接工厂接口设计测试
/// 验证异步接口契约（不依赖真实数据库）
/// </summary>
public class TimescaleDbConnectionFactoryTests
{
    [Fact]
    public void ConnectionFactory_Interface_HasAsyncMethod()
    {
        // 验证 INpgsqlConnectionFactory 接口包含 CreateConnectionAsync 方法
        var interfaceType = typeof(IntelliMaint.Infrastructure.TimescaleDb.INpgsqlConnectionFactory);

        var asyncMethod = interfaceType.GetMethod("CreateConnectionAsync");

        asyncMethod.Should().NotBeNull("接口应包含 CreateConnectionAsync 异步方法");
        asyncMethod!.ReturnType.Should().Be(typeof(Task<Npgsql.NpgsqlConnection>));
    }

    [Fact]
    public void ConnectionFactory_Interface_HasSyncMethod()
    {
        // 验证保持向后兼容
        var interfaceType = typeof(IntelliMaint.Infrastructure.TimescaleDb.INpgsqlConnectionFactory);

        var syncMethod = interfaceType.GetMethod("CreateConnection");

        syncMethod.Should().NotBeNull("接口应保持同步 CreateConnection 向后兼容");
        syncMethod!.ReturnType.Should().Be(typeof(Npgsql.NpgsqlConnection));
    }

    [Fact]
    public void ConnectionFactory_Interface_HasConnectionString()
    {
        var interfaceType = typeof(IntelliMaint.Infrastructure.TimescaleDb.INpgsqlConnectionFactory);

        var prop = interfaceType.GetProperty("ConnectionString");

        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(string));
    }
}
