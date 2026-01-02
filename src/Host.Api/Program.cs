using System.Text;
using System.Text.Json;
using IntelliMaint.Application.Services;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Endpoints;
using IntelliMaint.Host.Api.Hubs;
using IntelliMaint.Host.Api.Middleware;
using IntelliMaint.Host.Api.Services;
using IntelliMaint.Infrastructure.Sqlite;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.IdentityModel.Tokens;
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

    // JSON 配置 - 使用 camelCase
    builder.Services.Configure<JsonOptions>(options =>
    {
        options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

    // Configuration
    builder.Services.Configure<EdgeOptions>(
        builder.Configuration.GetSection(EdgeOptions.SectionName));

    // Infrastructure
    builder.Services.AddSqliteInfrastructure();
    
    // v48: 内存缓存
    builder.Services.AddMemoryCache();
    builder.Services.AddSingleton<CacheService>();

    // REST + Swagger
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // Health checks
    builder.Services.AddHealthChecks();

    // v44: 审计服务需要 HttpContextAccessor
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<AuditService>();

    // v45: 健康评估服务
    builder.Services.AddSingleton<IFeatureExtractor, FeatureExtractor>();
    builder.Services.AddSingleton<IHealthScoreCalculator, HealthScoreCalculator>();
    builder.Services.AddSingleton<HealthAssessmentService>();
    
    // v47: 周期分析服务
    builder.Services.AddSingleton<ICycleAnalysisService, CycleAnalysisService>();
    builder.Services.AddSingleton<IBaselineLearningService, BaselineLearningService>();

    // SignalR for real-time communication
    builder.Services.AddSignalR();

    // Background service for broadcasting telemetry data
    builder.Services.AddHostedService<TelemetryBroadcastService>();
    
    // v46: Collection Rule Engine
    builder.Services.AddHostedService<CollectionRuleEngine>();
    
    // v56: Data Cleanup Service - 定期清理旧数据
    builder.Services.Configure<DataCleanupOptions>(
        builder.Configuration.GetSection(DataCleanupOptions.SectionName));
    builder.Services.AddHostedService<DataCleanupService>();
    
    // v56: Data Aggregation Service - 数据降采样聚合
    builder.Services.AddHostedService<DataAggregationService>();

    // CORS (SignalR requires AllowCredentials)
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("ui", policy =>
        {
            policy.WithOrigins("http://localhost:3000", "http://127.0.0.1:3000")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
    });

    // Batch 35: JWT Authentication
    builder.Services.AddSingleton<JwtService>();

    // v43: 支持从环境变量读取 JWT 密钥
    // 优先级: 环境变量 > appsettings.json
    var jwtSecretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
        ?? builder.Configuration["Jwt:SecretKey"]
        ?? throw new InvalidOperationException("JWT_SECRET_KEY environment variable or Jwt:SecretKey config is required");

    if (jwtSecretKey.Length < 32)
    {
        throw new InvalidOperationException("JWT secret key must be at least 32 characters");
    }

    Log.Information("JWT configured. SecretKey source: {Source}", 
        Environment.GetEnvironmentVariable("JWT_SECRET_KEY") != null ? "Environment Variable" : "appsettings.json");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "IntelliMaint",
                ValidAudience = builder.Configuration["Jwt:Audience"] ?? "IntelliMaint",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey)),
                ClockSkew = TimeSpan.FromMinutes(1) // 减少时钟偏差容忍度
            };

            // v43: SignalR JWT 认证配置
            // SignalR 无法通过 HTTP Header 传递 Token，需要通过 Query String
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;

                    // 如果请求的是 SignalR Hub，从 query string 获取 token
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                },
                OnAuthenticationFailed = context =>
                {
                    Log.Warning("JWT authentication failed: {Error}", context.Exception.Message);
                    return Task.CompletedTask;
                }
            };
        });

    builder.Services.AddAuthorization(options =>
    {
        // AdminOnly: 只有 Admin 可访问
        options.AddPolicy(AuthPolicies.AdminOnly, policy =>
            policy.RequireRole(UserRoles.Admin));

        // OperatorOrAbove: Admin 或 Operator 可访问
        options.AddPolicy(AuthPolicies.OperatorOrAbove, policy =>
            policy.RequireRole(UserRoles.Admin, UserRoles.Operator));

        // AllAuthenticated: 所有已认证用户（默认策略）
        options.AddPolicy(AuthPolicies.AllAuthenticated, policy =>
            policy.RequireAuthenticatedUser());

        // 设置默认策略为 AllAuthenticated
        options.DefaultPolicy = options.GetPolicy(AuthPolicies.AllAuthenticated)!;
    });

    var app = builder.Build();

    // Initialize database
    await app.Services.InitializeDatabaseAsync();

    // Middleware pipeline
    // v48: 全局异常处理（应该在最外层）
    app.UseGlobalExceptionHandler();
    
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseSerilogRequestLogging();

    // v44: 请求限流 (60秒内最多100次请求)
    app.UseRateLimiting(options =>
    {
        options.WindowSeconds = 60;
        options.MaxRequests = 100;
    });

    // CORS must be before endpoints
    app.UseCors("ui");

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
