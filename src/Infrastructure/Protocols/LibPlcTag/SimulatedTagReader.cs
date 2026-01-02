using System.Diagnostics;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;

namespace IntelliMaint.Infrastructure.Protocols.LibPlcTag;

/// <summary>
/// Simulated tag reader for testing without real PLC.
/// Generates realistic data patterns: sine waves, ramps, random, boolean toggles.
/// </summary>
public sealed class SimulatedTagReader
{
    private readonly ILogger<SimulatedTagReader> _logger;
    private readonly Random _random = new();
    private readonly Dictionary<string, SimulationState> _tagStates = new();
    private readonly object _lock = new();

    public SimulatedTagReader(ILogger<SimulatedTagReader> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<TagReadResult>> ReadBatchAsync(
        TagGroupConfig group,
        PlcEndpointConfig plcConfig,
        CancellationToken ct)
    {
        var results = new List<TagReadResult>(group.Tags.Count);
        var stopwatch = Stopwatch.StartNew();

        foreach (var tagConfig in group.Tags)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var rawValue = GenerateValue(tagConfig);
                var latencyMs = stopwatch.Elapsed.TotalMilliseconds + _random.NextDouble() * 2; // 模拟 0-2ms 延迟

                results.Add(new TagReadResult(
                    PlcId: plcConfig.PlcId,
                    DeviceId: plcConfig.PlcId,
                    TagId: tagConfig.TagId,
                    TagConfig: tagConfig,
                    Success: true,
                    RawValue: rawValue,
                    Quality: 192,  // Good
                    Error: LibPlcTagError.OK,
                    ErrorMessage: null,
                    LatencyMs: latencyMs));
            }
            catch (Exception ex)
            {
                results.Add(new TagReadResult(
                    PlcId: plcConfig.PlcId,
                    DeviceId: plcConfig.PlcId,
                    TagId: tagConfig.TagId,
                    TagConfig: tagConfig,
                    Success: false,
                    RawValue: null,
                    Quality: 0,
                    Error: LibPlcTagError.UNKNOWN,
                    ErrorMessage: ex.Message,
                    LatencyMs: stopwatch.Elapsed.TotalMilliseconds));

                _logger.LogWarning("Simulated tag read failed: {TagId} - {Message}", 
                    tagConfig.TagId, ex.Message);
            }

            stopwatch.Restart();
        }

        return Task.FromResult<IReadOnlyList<TagReadResult>>(results);
    }

    private object GenerateValue(PlcTagConfig tagConfig)
    {
        var state = GetOrCreateState(tagConfig);
        var cipType = tagConfig.CipType.Trim().ToUpperInvariant();

        // 根据标签名称推断模拟模式
        var tagName = tagConfig.TagId.ToUpperInvariant();
        var simMode = InferSimulationMode(tagName, cipType);

        return simMode switch
        {
            SimMode.Sine => GenerateSineValue(state, cipType),
            SimMode.Ramp => GenerateRampValue(state, cipType),
            SimMode.Random => GenerateRandomValue(state, cipType),
            SimMode.Toggle => GenerateToggleValue(state),
            SimMode.Counter => GenerateCounterValue(state, cipType),
            _ => GenerateRandomValue(state, cipType)
        };
    }

    private SimMode InferSimulationMode(string tagName, string cipType)
    {
        // Bool 类型使用 Toggle
        if (cipType == "BOOL")
            return SimMode.Toggle;

        // 根据名称关键字推断
        if (tagName.Contains("TEMP") || tagName.Contains("CURRENT") || tagName.Contains("SPEED"))
            return SimMode.Sine;
        
        if (tagName.Contains("COUNT") || tagName.Contains("TOTAL") || tagName.Contains("PROD"))
            return SimMode.Counter;
        
        if (tagName.Contains("RAMP") || tagName.Contains("SETPOINT"))
            return SimMode.Ramp;
        
        if (tagName.Contains("LEVEL") || tagName.Contains("PRESSURE") || tagName.Contains("FLOW"))
            return SimMode.Random;

        // 默认使用随机
        return SimMode.Random;
    }

    private SimulationState GetOrCreateState(PlcTagConfig tagConfig)
    {
        lock (_lock)
        {
            if (!_tagStates.TryGetValue(tagConfig.TagId, out var state))
            {
                state = new SimulationState
                {
                    Phase = _random.NextDouble() * Math.PI * 2,
                    BaseValue = GetDefaultBaseValue(tagConfig.CipType),
                    Amplitude = GetDefaultAmplitude(tagConfig.CipType),
                    CurrentValue = GetDefaultBaseValue(tagConfig.CipType),  // 初始化为基准值
                    LastUpdate = DateTimeOffset.UtcNow,
                    Counter = 0,
                    BoolState = false
                };
                _tagStates[tagConfig.TagId] = state;
            }
            return state;
        }
    }

    private static double GetDefaultBaseValue(string cipType) => cipType.ToUpperInvariant() switch
    {
        "REAL" or "LREAL" => 50.0,
        "DINT" or "INT" or "SINT" => 500,
        "UDINT" or "UINT" or "USINT" => 1000,
        _ => 100
    };

    private static double GetDefaultAmplitude(string cipType) => cipType.ToUpperInvariant() switch
    {
        "REAL" or "LREAL" => 25.0,
        "DINT" or "INT" or "SINT" => 100,
        "UDINT" or "UINT" or "USINT" => 200,
        _ => 50
    };

    /// <summary>
    /// 正弦波：适合温度、电流、速度等周期性变化的值
    /// </summary>
    private object GenerateSineValue(SimulationState state, string cipType)
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = (now - state.LastUpdate).TotalSeconds;
        
        // 周期约 30 秒
        var angle = state.Phase + elapsed * (2 * Math.PI / 30);
        var value = state.BaseValue + state.Amplitude * Math.Sin(angle);
        
        // 添加小幅度噪声
        value += (_random.NextDouble() - 0.5) * state.Amplitude * 0.05;

        return ConvertToType(value, cipType);
    }

    /// <summary>
    /// 锯齿波：从 min 到 max 线性增长，然后重置
    /// </summary>
    private object GenerateRampValue(SimulationState state, string cipType)
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = (now - state.LastUpdate).TotalSeconds;
        
        // 周期约 60 秒
        var progress = (elapsed % 60) / 60;
        var value = state.BaseValue - state.Amplitude + progress * state.Amplitude * 2;

        return ConvertToType(value, cipType);
    }

    /// <summary>
    /// 随机波动：在基准值附近随机波动
    /// </summary>
    private object GenerateRandomValue(SimulationState state, string cipType)
    {
        // 平滑的随机游走
        var delta = (_random.NextDouble() - 0.5) * state.Amplitude * 0.1;
        state.CurrentValue += delta;
        
        // 限制在范围内
        var min = state.BaseValue - state.Amplitude;
        var max = state.BaseValue + state.Amplitude;
        state.CurrentValue = Math.Clamp(state.CurrentValue, min, max);

        // 偶尔回归基准值
        if (_random.NextDouble() < 0.01)
        {
            state.CurrentValue = state.BaseValue + (_random.NextDouble() - 0.5) * state.Amplitude * 0.2;
        }

        return ConvertToType(state.CurrentValue, cipType);
    }

    /// <summary>
    /// Bool 交替：以一定概率切换状态
    /// </summary>
    private object GenerateToggleValue(SimulationState state)
    {
        // 约 5% 的概率切换
        if (_random.NextDouble() < 0.05)
        {
            state.BoolState = !state.BoolState;
        }
        return state.BoolState;
    }

    /// <summary>
    /// 计数器：递增值
    /// </summary>
    private object GenerateCounterValue(SimulationState state, string cipType)
    {
        // 约 20% 的概率递增
        if (_random.NextDouble() < 0.2)
        {
            state.Counter++;
        }
        return ConvertToType(state.Counter, cipType);
    }

    private static object ConvertToType(double value, string cipType)
    {
        return cipType.Trim().ToUpperInvariant() switch
        {
            "BOOL" => value > 0.5,
            "SINT" => (sbyte)Math.Clamp(value, sbyte.MinValue, sbyte.MaxValue),
            "USINT" => (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue),
            "INT" => (short)Math.Clamp(value, short.MinValue, short.MaxValue),
            "UINT" => (ushort)Math.Clamp(value, ushort.MinValue, ushort.MaxValue),
            "DINT" => (int)Math.Clamp(value, int.MinValue, int.MaxValue),
            "UDINT" => (uint)Math.Clamp(value, uint.MinValue, uint.MaxValue),
            "LINT" => (long)value,
            "ULINT" => (ulong)Math.Max(0, value),
            "REAL" => (float)value,
            "LREAL" => value,
            "STRING" => $"SIM_{value:F2}",
            _ => (float)value
        };
    }

    private enum SimMode
    {
        Sine,      // 正弦波
        Ramp,      // 锯齿波
        Random,    // 随机波动
        Toggle,    // Bool 切换
        Counter    // 递增计数
    }

    private sealed class SimulationState
    {
        public double Phase { get; set; }
        public double BaseValue { get; set; }
        public double Amplitude { get; set; }
        public double CurrentValue { get; set; }
        public DateTimeOffset LastUpdate { get; set; }
        public long Counter { get; set; }
        public bool BoolState { get; set; }
    }
}
