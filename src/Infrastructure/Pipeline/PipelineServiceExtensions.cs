using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Infrastructure.Sqlite;

namespace IntelliMaint.Infrastructure.Pipeline;

/// <summary>
/// Pipeline 服务注册扩展
/// </summary>
public static class PipelineServiceExtensions
{
    /// <summary>
    /// 注册 Pipeline 相关服务
    /// </summary>
    public static IServiceCollection AddPipelineInfrastructure(this IServiceCollection services)
    {
        // 溢出导出器
        services.AddSingleton<OverflowExporter>();
        services.AddSingleton<IOverflowExporter>(sp => sp.GetRequiredService<OverflowExporter>());
        services.AddHostedService(sp => sp.GetRequiredService<OverflowExporter>());
        
        // 主管道
        services.AddSingleton<TelemetryPipeline>();
        services.AddSingleton<ITelemetryPipeline>(sp => sp.GetRequiredService<TelemetryPipeline>());
        
        // 分发器
        services.AddSingleton<TelemetryDispatcher>();
        services.AddSingleton<ITelemetryDispatcher>(sp => sp.GetRequiredService<TelemetryDispatcher>());
        services.AddHostedService(sp => sp.GetRequiredService<TelemetryDispatcher>());
        
        // DB Writer（需要在 Dispatcher 之后配置）
        services.AddSingleton(sp =>
        {
            var dispatcher = sp.GetRequiredService<TelemetryDispatcher>();
            var options = sp.GetRequiredService<IOptions<ChannelCapacityOptions>>();
            
            // 创建 DB 写入 Channel
            var (_, reader) = dispatcher.CreateTargetChannel(options.Value.DbWriterCapacity, "DbWriter");
            
            return new DbWriterLoop(
                reader,
                sp.GetRequiredService<ITelemetryRepository>(),
                sp.GetRequiredService<IOptions<EdgeOptions>>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DbWriterLoop>>());
        });
        services.AddHostedService(sp => sp.GetRequiredService<DbWriterLoop>());
        
        // Batch 30: Alarm Evaluator（和 DbWriter 并行处理数据）
        services.AddSingleton(sp =>
        {
            var dispatcher = sp.GetRequiredService<TelemetryDispatcher>();
            var options = sp.GetRequiredService<IOptions<ChannelCapacityOptions>>();

            // 创建告警评估 Channel
            var (_, reader) = dispatcher.CreateTargetChannel(options.Value.DbWriterCapacity / 2, "AlarmEvaluator");

            return new AlarmEvaluatorService(
                reader,
                sp.GetRequiredService<IAlarmRuleRepository>(),
                sp.GetRequiredService<IAlarmRepository>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AlarmEvaluatorService>>(),
                sp.GetService<AlarmAggregationService>());  // v59: 可选的聚合服务
        });
        services.AddHostedService(sp => sp.GetRequiredService<AlarmEvaluatorService>());

        // v56: 滑动窗口数据结构（变化率告警用）
        services.AddSingleton<RocSlidingWindow>();

        // v56: 最后数据追踪器（离线检测用）
        services.AddSingleton(sp =>
        {
            var dispatcher = sp.GetRequiredService<TelemetryDispatcher>();
            var options = sp.GetRequiredService<IOptions<ChannelCapacityOptions>>();

            // 创建追踪器 Channel
            var (_, reader) = dispatcher.CreateTargetChannel(options.Value.DbWriterCapacity / 4, "LastDataTracker");

            return new LastDataTracker(
                reader,
                sp.GetService<IDbExecutor>(),  // v56.2: 可选，TimescaleDB 模式下为 null
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LastDataTracker>>());
        });
        services.AddHostedService(sp => sp.GetRequiredService<LastDataTracker>());

        // v56: 离线检测服务（定时器驱动，不需要 Channel）
        services.AddHostedService<OfflineDetectorService>();

        // v56: 变化率告警评估服务
        services.AddSingleton(sp =>
        {
            var dispatcher = sp.GetRequiredService<TelemetryDispatcher>();
            var options = sp.GetRequiredService<IOptions<ChannelCapacityOptions>>();

            // 创建变化率评估 Channel
            var (_, reader) = dispatcher.CreateTargetChannel(options.Value.DbWriterCapacity / 4, "RocEvaluator");

            return new RocEvaluatorService(
                reader,
                sp.GetRequiredService<IAlarmRuleRepository>(),
                sp.GetRequiredService<IAlarmRepository>(),
                sp.GetRequiredService<RocSlidingWindow>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RocEvaluatorService>>(),
                sp.GetService<AlarmAggregationService>());  // v59: 可选的聚合服务
        });
        services.AddHostedService(sp => sp.GetRequiredService<RocEvaluatorService>());

        // v58: 波动告警评估服务
        services.AddSingleton(sp =>
        {
            var dispatcher = sp.GetRequiredService<TelemetryDispatcher>();
            var options = sp.GetRequiredService<IOptions<ChannelCapacityOptions>>();

            // 创建波动评估 Channel
            var (_, reader) = dispatcher.CreateTargetChannel(options.Value.DbWriterCapacity / 4, "VolatilityEvaluator");

            return new VolatilityEvaluatorService(
                reader,
                sp.GetRequiredService<IAlarmRuleRepository>(),
                sp.GetRequiredService<IAlarmRepository>(),
                sp.GetRequiredService<RocSlidingWindow>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<VolatilityEvaluatorService>>(),
                sp.GetService<AlarmAggregationService>());  // v59: 可选的聚合服务
        });
        services.AddHostedService(sp => sp.GetRequiredService<VolatilityEvaluatorService>());

        // v59: 告警聚合服务
        services.AddSingleton<AlarmAggregationService>();

        return services;
    }
}
