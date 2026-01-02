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
                sp.GetRequiredService<IDbExecutor>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AlarmEvaluatorService>>());
        });
        services.AddHostedService(sp => sp.GetRequiredService<AlarmEvaluatorService>());
        
        return services;
    }
}
