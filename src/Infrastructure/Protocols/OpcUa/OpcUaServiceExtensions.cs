using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IntelliMaint.Infrastructure.Protocols.OpcUa;

public static class OpcUaServiceExtensions
{
    public static IServiceCollection AddOpcUaCollector(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<OpcUaOptions>()
            .Bind(config.GetSection("Protocols:OpcUa"))
            .Validate(o => o is not null, "OpcUaOptions is required.");

        services.AddSingleton<OpcUaHealthChecker>();
        services.AddSingleton<OpcUaTypeMapper>();
        services.AddSingleton<OpcUaSessionManager>();
        services.AddSingleton<OpcUaSubscriptionManager>();
        
        // Batch 32: 数据库配置适配器
        services.AddSingleton<IOpcUaConfigAdapter, OpcUaConfigAdapter>();

        services.AddSingleton<OpcUaCollector>();
        services.AddSingleton<ICollector>(sp => sp.GetRequiredService<OpcUaCollector>());
        services.AddSingleton<ITelemetrySource>(sp => sp.GetRequiredService<OpcUaCollector>());

        return services;
    }
}
