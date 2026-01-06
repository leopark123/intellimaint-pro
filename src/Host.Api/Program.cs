using System.Text.Json;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Endpoints;
using IntelliMaint.Host.Api.Extensions;
using IntelliMaint.Host.Api.Hubs;
using IntelliMaint.Host.Api.Middleware;
using IntelliMaint.Host.Api.Services;
using IntelliMaint.Infrastructure.Sqlite;
using IntelliMaint.Infrastructure.TimescaleDb;
using Microsoft.AspNetCore.Http.Json;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/api-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Starting IntelliMaint API...");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // JSON 配置 - 使用 camelCase，并支持大小写不敏感的反序列化
    builder.Services.Configure<JsonOptions>(options =>
    {
        options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.SerializerOptions.PropertyNameCaseInsensitive = true;  // v56.1: 支持 PascalCase 和 camelCase 输入
    });

    // Configuration
    builder.Services.Configure<EdgeOptions>(
        builder.Configuration.GetSection(EdgeOptions.SectionName));

    // P2: Infrastructure - 根据配置选择数据库提供者
    var dbProvider = builder.Configuration["DatabaseProvider"] ?? "Sqlite";
    if (dbProvider.Equals("TimescaleDb", StringComparison.OrdinalIgnoreCase))
    {
        Log.Information("[Database] Using TimescaleDB provider");
        builder.Services.AddTimescaleDbInfrastructure();
    }
    else
    {
        Log.Information("[Database] Using SQLite provider");
        builder.Services.AddSqliteInfrastructure();
    }
    builder.Services.AddMemoryCache();
    builder.Services.AddSingleton<CacheService>();

    // P2: REST + Swagger
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddHealthChecks();

    // P2: SignalR
    builder.Services.AddSignalR();

    // P2: 应用层服务（统一注册）
    builder.Services.AddApplicationServices();

    // P2: 后台服务
    builder.Services.AddBackgroundServices(builder.Configuration);

    // P2: CORS 策略
    builder.Services.AddCorsPolicies(builder.Configuration);

    // P2: JWT 认证
    builder.Services.AddJwtAuthentication(builder.Configuration);

    // P2: 授权策略
    builder.Services.AddAuthorizationPolicies();

    var app = builder.Build();

    // Initialize database
    if (dbProvider.Equals("TimescaleDb", StringComparison.OrdinalIgnoreCase))
    {
        await TimescaleDbServiceExtensions.InitializeDatabaseAsync(app.Services);
    }
    else
    {
        await SqliteServiceExtensions.InitializeDatabaseAsync(app.Services);
    }

    // Middleware pipeline
    // v48: 全局异常处理（应该在最外层）
    app.UseGlobalExceptionHandler();

    // v56.1: HTTPS 强制重定向（生产环境）
    if (!app.Environment.IsDevelopment())
    {
        // HSTS - 强制浏览器使用 HTTPS（1年有效期）
        app.UseHsts();
        app.UseHttpsRedirection();
        Log.Information("[Security] HTTPS redirection and HSTS enabled for production");
    }
    else
    {
        Log.Warning("[Security] HTTPS redirection disabled in Development environment");
    }

    // v56.1: Swagger 仅在开发环境启用
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    app.UseSerilogRequestLogging();

    // v44: 请求限流 - 使用 SystemConstants 配置
    app.UseRateLimiting(options =>
    {
        options.WindowSeconds = SystemConstants.RateLimiting.WindowSeconds;
        options.MaxRequests = SystemConstants.RateLimiting.MaxRequests;
    });

    // v56.1: CORS must be before endpoints - 根据环境选择策略
    var corsPolicy = app.Environment.IsDevelopment() ? "development" : "production";
    app.UseCors(corsPolicy);
    Log.Information("[Security] CORS policy: {Policy}", corsPolicy);

    // Batch 35: Authentication & Authorization
    app.UseAuthentication();
    app.UseAuthorization();

    // Health endpoints
    app.MapHealthChecks("/health/live");
    app.MapHealthChecks("/health/ready");

    app.MapControllers();

    // Root endpoint
    app.MapGet("/", () => "IntelliMaint API is running");

    // REST API endpoints
    app.MapTelemetryEndpoints();
    
    // Batch 23: Device Management API
    app.MapDeviceEndpoints();
    
    // Batch 24: Tag Management API
    app.MapTagEndpoints();
    
    // Batch 25: Alarm Management API
    app.MapAlarmEndpoints();
    
    // Batch 26: Health API
    app.MapHealthEndpoints();
    
    // Batch 27: Export API
    app.MapExportEndpoints();
    
    // Batch 28: Settings API
    app.MapSettingsEndpoints();
    
    // Batch 29: Audit Log API
    app.MapAuditLogEndpoints();
    
    // Batch 30: Alarm Rule API
    app.MapAlarmRuleEndpoints();
    
    // Batch 35: Auth API
    app.MapAuthEndpoints();
    
    // Batch 40: User Management API
    app.MapUserEndpoints();
    
    // v45: Health Assessment API
    app.MapHealthAssessmentEndpoints();
    
    // v46: Collection Rule API
    app.MapCollectionRuleEndpoints();
    
    // v47: Cycle Analysis API
    app.MapCycleAnalysisEndpoints();

    // SignalR Hub endpoint
    app.MapHub<TelemetryHub>("/hubs/telemetry");

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// 使 Program 类可被测试项目访问
public partial class Program { }
