using Microsoft.Extensions.DependencyInjection;
using IntelliMaint.Core.Abstractions;

namespace IntelliMaint.Infrastructure.Sqlite;

/// <summary>
/// SQLite 基础设施服务注册扩展
/// </summary>
public static class SqliteServiceExtensions
{
    /// <summary>
    /// 注册 SQLite 相关服务
    /// </summary>
    public static IServiceCollection AddSqliteInfrastructure(this IServiceCollection services)
    {
        // 连接工厂（Singleton）
        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        
        // 执行器（Singleton，内部有写锁）
        services.AddSingleton<IDbExecutor, DbExecutor>();
        
        // Schema 管理器（Singleton）
        services.AddSingleton<ISchemaManager, SchemaManager>();
        
        // 仓储（Singleton，依赖 DbExecutor）
        services.AddSingleton<ITelemetryRepository, TelemetryRepository>();
        
        // Batch 23: Device Repository
        services.AddSingleton<IDeviceRepository, DeviceRepository>();
        
        // Batch 24: Tag Repository
        services.AddSingleton<ITagRepository, TagRepository>();
        
        // Batch 25: Alarm Repository
        services.AddSingleton<IAlarmRepository, AlarmRepository>();
        
        // Batch 26: Health Snapshot Repository
        services.AddSingleton<IHealthSnapshotRepository, HealthSnapshotRepository>();
        
        // Batch 28: System Setting Repository
        services.AddSingleton<ISystemSettingRepository, SystemSettingRepository>();
        
        // Batch 29: Audit Log Repository
        services.AddSingleton<IAuditLogRepository, AuditLogRepository>();
        
        // Batch 30: Alarm Rule Repository
        services.AddSingleton<IAlarmRuleRepository, AlarmRuleRepository>();
        
        // Batch 31: Database Config Provider（供采集器使用）
        services.AddSingleton<IDbConfigProvider, DbConfigProvider>();
        
        // Batch 33: Config Revision Provider（配置版本管理）
        services.AddSingleton<IConfigRevisionProvider, ConfigRevisionProvider>();
        
        // Batch 35: User Repository（用户认证）
        services.AddSingleton<IUserRepository, UserRepository>();
        
        // v45: Health Baseline Repository（健康基线）
        services.AddSingleton<IHealthBaselineRepository, HealthBaselineRepository>();
        
        // v46: Collection Rule Repository（采集规则）
        services.AddSingleton<ICollectionRuleRepository, CollectionRuleRepository>();
        services.AddSingleton<ICollectionSegmentRepository, CollectionSegmentRepository>();
        
        // v47: Cycle Analysis Repository（周期分析）
        services.AddSingleton<IWorkCycleRepository, WorkCycleRepository>();
        services.AddSingleton<ICycleDeviceBaselineRepository, CycleDeviceBaselineRepository>();
        
        // Batch 32: Config Change Watcher（定期检测配置变更）
        services.AddHostedService<ConfigChangeWatcher>();
        
        // TODO: 其他仓储
        // services.AddSingleton<ITagRepository, TagRepository>();
        // services.AddSingleton<IAlarmRepository, AlarmRepository>();
        // services.AddSingleton<IHealthSnapshotRepository, HealthSnapshotRepository>();
        // services.AddSingleton<IMqttOutboxRepository, MqttOutboxRepository>();
        
        return services;
    }
    
    /// <summary>
    /// 初始化数据库
    /// 应在应用启动时调用
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        var schemaManager = services.GetRequiredService<ISchemaManager>();
        await schemaManager.InitializeAsync(ct);
    }
}
