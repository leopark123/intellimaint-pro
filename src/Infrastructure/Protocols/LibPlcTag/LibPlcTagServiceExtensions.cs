using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IntelliMaint.Infrastructure.Protocols.LibPlcTag;

/// <summary>
/// LibPlcTag service registration extensions
/// v55: Added SimulatedTagReader and ConfigAdapter support
/// </summary>
public static class LibPlcTagServiceExtensions
{
    /// <summary>
    /// Add LibPlcTag collector services
    /// </summary>
    public static IServiceCollection AddLibPlcTagCollector(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind options
        services.Configure<LibPlcTagOptions>(
            configuration.GetSection("Protocols:LibPlcTag"));

        // Type mapper (singleton)
        services.AddSingleton<ITagTypeMapper, LibPlcTagTypeMapper>();
        
        // Health checker (singleton)
        services.AddSingleton<LibPlcTagHealthChecker>();
        
        // Connection pool (singleton)
        services.AddSingleton<LibPlcTagConnectionPool>();
        
        // Tag reader (singleton) - real PLC
        services.AddSingleton<LibPlcTagTagReader>();
        
        // v55: Simulated tag reader (singleton) - for testing
        services.AddSingleton<SimulatedTagReader>();
        
        // v55: Config adapter (singleton) - load from database
        services.AddSingleton<ILibPlcTagConfigAdapter, LibPlcTagConfigAdapter>();

        // Main collector (singleton, implements both interfaces)
        services.AddSingleton<LibPlcTagCollector>();
        services.AddSingleton<ICollector>(sp => sp.GetRequiredService<LibPlcTagCollector>());
        services.AddSingleton<ITelemetrySource>(sp => sp.GetRequiredService<LibPlcTagCollector>());

        return services;
    }
}
