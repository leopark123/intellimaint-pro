using System.Threading.Channels;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Infrastructure.Pipeline;

namespace IntelliMaint.Host.Api.Services;

/// <summary>Opt-in local demo. No protocol client and no connection to industrial hardware.</summary>
public sealed class SyntheticDemoService(IServiceProvider services, ILogger<SyntheticDemoService> logger)
    : BackgroundService
{
    public const string DeviceId = "synthetic-motor-001";
    public static readonly (string Id, string Unit)[] Signals =
    {
        ("Temperature", "°C"), ("Vibration", "mm/s"), ("RPM", "rpm"),
        ("Current", "A"), ("Voltage", "V")
    };

    public static IReadOnlyList<TelemetryPoint> CreateSample(long ts, int step)
    {
        // Repeatable 60-second cycle: deliberately exceed the temperature threshold for 10 seconds.
        var phase = ((step % 60) + 60) % 60;
        var hot = phase < 10;
        var wave = Math.Sin(step * Math.PI / 30);
        double[] values = { hot ? 92 : 58 + 4 * wave, 2.2 + .4 * wave,
            1480 + 15 * wave, hot ? 18 : 11 + wave, 400 + 3 * wave };
        return Signals.Select((signal, i) => new TelemetryPoint
        {
            DeviceId = DeviceId, TagId = "Synthetic." + signal.Id,
            Ts = ts, Seq = i, ValueType = TagValueType.Float64, Float64Value = values[i],
            Unit = signal.Unit, Source = "Synthetic Demo Data", Protocol = "synthetic"
        }).ToArray();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var devices = services.GetRequiredService<IDeviceRepository>();
        var tags = services.GetRequiredService<ITagRepository>();
        var telemetry = services.GetRequiredService<ITelemetryRepository>();
        var rules = services.GetRequiredService<IAlarmRuleRepository>();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await devices.UpsertAsync(new DeviceDto
        {
            DeviceId = DeviceId, Name = "Synthetic Demo Data — Motor", Protocol = "synthetic",
            Location = "Local demonstration", CreatedUtc = now, UpdatedUtc = now
        }, stoppingToken);
        foreach (var signal in Signals)
            await tags.UpsertAsync(new TagDto
            {
                DeviceId = DeviceId, TagId = "Synthetic." + signal.Id, Name = signal.Id,
                Description = "Synthetic Demo Data", Unit = signal.Unit, DataType = TagValueType.Float64,
                CreatedUtc = now, UpdatedUtc = now
            }, stoppingToken);

        await rules.UpsertAsync(new AlarmRule
        {
            RuleId = "synthetic-temperature-high", Name = "Synthetic Demo Data: high temperature",
            DeviceId = DeviceId, TagId = "Synthetic.Temperature", ConditionType = "gt",
            Threshold = 85, Severity = 3, CreatedUtc = now, UpdatedUtc = now,
            MessageTemplate = "Synthetic Demo Data: temperature {value} exceeds {threshold} °C"
        }, stoppingToken);

        using var evaluator = new AlarmEvaluatorService(Channel.CreateUnbounded<TelemetryPoint>().Reader,
            rules, services.GetRequiredService<IAlarmRepository>(),
            services.GetRequiredService<ILogger<AlarmEvaluatorService>>());
        await evaluator.RefreshRulesAsync(stoppingToken);
        // Seed a small recent history so trend and health pages have meaningful input immediately.
        var history = Enumerable.Range(-300, 300).SelectMany(i => CreateSample(now + i * 1000, i)).ToArray();
        await telemetry.AppendBatchAsync(history, stoppingToken);
        logger.LogWarning("Synthetic Demo Data enabled. No PLC or OPC UA server is contacted.");

        var step = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var batch = CreateSample(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), step++);
                await telemetry.AppendBatchAsync(batch, stoppingToken);
                foreach (var point in batch) await evaluator.EvaluateAsync(point, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
