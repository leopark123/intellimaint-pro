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

    // JSON 閰嶇疆 - 浣跨敤 camelCase锛屽苟鏀寔澶у皬鍐欎笉鏁忔劅鐨勫弽搴忓垪鍖?
    builder.Services.Configure<JsonOptions>(options =>
    {
        options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.SerializerOptions.PropertyNameCaseInsensitive = true;  // v56.1: 鏀寔 PascalCase 鍜?camelCase 杈撳叆
    });

    // Configuration
    builder.Services.Configure<EdgeOptions>(
        builder.Configuration.GetSection(EdgeOptions.SectionName));

    // v61: 健康评估配置
    builder.Services.Configure<HealthAssessmentOptions>(
        builder.Configuration.GetSection("HealthAssessment"));

    // P2: Infrastructure - 鏍规嵁閰嶇疆閫夋嫨鏁版嵁搴撴彁渚涜€?
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
    builder.Services.AddSignalR(options =>
    {
        options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        options.HandshakeTimeout = TimeSpan.FromSeconds(15);
        options.EnableDetailedErrors = true;
    });

    // P2: 搴旂敤灞傛湇鍔★紙缁熶竴娉ㄥ唽锛?
    builder.Services.AddApplicationServices();

    // P2: 鍚庡彴鏈嶅姟
    builder.Services.AddBackgroundServices(builder.Configuration);

    // P2: CORS 绛栫暐
    builder.Services.AddCorsPolicies(builder.Configuration);

    // P2: JWT 璁よ瘉
    builder.Services.AddJwtAuthentication(builder.Configuration);

    // P2: 鎺堟潈绛栫暐
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
    // v48: 鍏ㄥ眬寮傚父澶勭悊锛堝簲璇ュ湪鏈€澶栧眰锛?
    app.UseGlobalExceptionHandler();

    // v56.1: HTTPS 寮哄埗閲嶅畾鍚戯紙鐢熶骇鐜锛?
    if (!app.Environment.IsDevelopment())
    {
        // HSTS - 寮哄埗娴忚鍣ㄤ娇鐢?HTTPS锛?骞存湁鏁堟湡锛?
        app.UseHsts();
        app.UseHttpsRedirection();
        Log.Information("[Security] HTTPS redirection and HSTS enabled for production");
    }
    else
    {
        Log.Warning("[Security] HTTPS redirection disabled in Development environment");
    }

    // v56.1: Swagger 浠呭湪寮€鍙戠幆澧冨惎鐢?
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }
    app.UseSerilogRequestLogging();

    // v44: 璇锋眰闄愭祦 - 浣跨敤 SystemConstants 閰嶇疆
    app.UseRateLimiting(options =>
    {
        options.WindowSeconds = SystemConstants.RateLimiting.WindowSeconds;
        options.MaxRequests = SystemConstants.RateLimiting.MaxRequests;
    });

    // v56.1: CORS must be before endpoints - 鏍规嵁鐜閫夋嫨绛栫暐
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

    // v63: Prediction & Alert API
    app.MapPredictionEndpoints();

    // v64: Motor Fault Prediction API
    app.MapMotorEndpoints();

    // v65: Edge Config Management API
    app.MapEdgeConfigEndpoints();

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

// 浣?Program 绫诲彲琚祴璇曢」鐩闂?
public partial class Program { }
