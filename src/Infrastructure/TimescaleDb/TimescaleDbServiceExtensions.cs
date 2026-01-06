using IntelliMaint.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace IntelliMaint.Infrastructure.TimescaleDb;

/// <summary>
/// TimescaleDB 基础设施服务注册扩展
/// </summary>
public static class TimescaleDbServiceExtensions
{
    /// <summary>
    /// 注册 TimescaleDB 基础设施服务
    /// </summary>
    public static IServiceCollection AddTimescaleDbInfrastructure(this IServiceCollection services)
    {
        // 连接工厂（Singleton）
        services.AddSingleton<INpgsqlConnectionFactory, TimescaleDbConnectionFactory>();

        // Schema 管理器（Singleton）
        services.AddSingleton<SchemaManager>();

        // 时序数据库抽象
        services.AddSingleton<ITimeSeriesDb, TimescaleDbTimeSeriesDb>();

        // 核心仓储（Singleton）
        services.AddSingleton<ITelemetryRepository, TelemetryRepository>();
        services.AddSingleton<IDeviceRepository, DeviceRepository>();
        services.AddSingleton<ITagRepository, TagRepository>();
        services.AddSingleton<IUserRepository, UserRepository>();

        // 告警相关仓储
        services.AddSingleton<IAlarmRepository, AlarmRepository>();
        services.AddSingleton<IAlarmRuleRepository, AlarmRuleRepository>();
        services.AddSingleton<IAlarmGroupRepository, AlarmGroupRepository>();

        // 健康评估相关仓储
        services.AddSingleton<IHealthSnapshotRepository, HealthSnapshotRepository>();
        services.AddSingleton<IHealthBaselineRepository, HealthBaselineRepository>();
        services.AddSingleton<IDeviceHealthSnapshotRepository, DeviceHealthSnapshotRepository>();

        // 系统配置相关仓储
        services.AddSingleton<ISystemSettingRepository, SystemSettingRepository>();
        services.AddSingleton<IAuditLogRepository, AuditLogRepository>();

        // 采集规则相关仓储
        services.AddSingleton<ICollectionRuleRepository, CollectionRuleRepository>();
        services.AddSingleton<ICollectionSegmentRepository, CollectionSegmentRepository>();

        // 周期分析相关仓储
        services.AddSingleton<IWorkCycleRepository, WorkCycleRepository>();
        services.AddSingleton<ICycleDeviceBaselineRepository, CycleDeviceBaselineRepository>();

        // 配置提供者
        services.AddSingleton<IDbConfigProvider, DbConfigProvider>();
        services.AddSingleton<IConfigRevisionProvider, ConfigRevisionProvider>();

        return services;
    }

    /// <summary>
    /// 初始化数据库
    /// 应在应用启动时调用
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        var schemaManager = services.GetRequiredService<SchemaManager>();
        await schemaManager.InitializeAsync(ct);
    }
}
