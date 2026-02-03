using FluentAssertions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Infrastructure.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace IntelliMaint.Tests.Unit;

/// <summary>
/// TelemetryPipeline 核心逻辑单元测试
/// 验证有界 Channel、背压、溢出导出、统计等功能
/// </summary>
public class TelemetryPipelineTests : IDisposable
{
    private readonly TelemetryPipeline _pipeline;
    private readonly int _capacity = 100;

    public TelemetryPipelineTests()
    {
        var options = Options.Create(new ChannelCapacityOptions { GlobalCapacity = _capacity });
        _pipeline = new TelemetryPipeline(options, NullLogger<TelemetryPipeline>.Instance);
    }

    public void Dispose()
    {
        _pipeline.Dispose();
    }

    private static TelemetryPoint CreatePoint(string tagId = "tag-1", long ts = 0)
    {
        return new TelemetryPoint
        {
            DeviceId = "device-1",
            TagId = tagId,
            Ts = ts > 0 ? ts : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Seq = 0,
            ValueType = TagValueType.Float64,
            Float64Value = 25.5,
            Quality = 192
        };
    }

    [Fact]
    public async Task WriteAsync_SinglePoint_ReturnsTrue()
    {
        var result = await _pipeline.WriteAsync(CreatePoint(), CancellationToken.None);

        result.Should().BeTrue();
        _pipeline.QueueDepth.Should().Be(1);
    }

    [Fact]
    public async Task WriteAsync_MultiplePoints_AllSucceed()
    {
        for (var i = 0; i < 50; i++)
        {
            await _pipeline.WriteAsync(CreatePoint($"tag-{i}"), CancellationToken.None);
        }

        _pipeline.QueueDepth.Should().Be(50);
    }

    [Fact]
    public async Task WriteBatchAsync_ReturnsBatchCount()
    {
        var batch = Enumerable.Range(0, 10)
            .Select(i => CreatePoint($"tag-{i}"))
            .ToList();

        var written = await _pipeline.WriteBatchAsync(batch, CancellationToken.None);

        written.Should().Be(10);
        _pipeline.QueueDepth.Should().Be(10);
    }

    [Fact]
    public async Task GetStats_ReflectsWrittenData()
    {
        for (var i = 0; i < 5; i++)
        {
            await _pipeline.WriteAsync(CreatePoint($"tag-{i}"), CancellationToken.None);
        }

        var stats = _pipeline.GetStats();

        stats.TotalReceived.Should().Be(5);
        stats.TotalWritten.Should().Be(5);
        stats.TotalDropped.Should().Be(0);
        stats.CurrentQueueDepth.Should().Be(5);
    }

    [Fact]
    public async Task WriteAsync_AtCapacity_TriggersBackpressure()
    {
        // 填满 Channel
        for (var i = 0; i < _capacity; i++)
        {
            await _pipeline.WriteAsync(CreatePoint($"tag-{i}"), CancellationToken.None);
        }

        _pipeline.QueueDepth.Should().Be(_capacity);

        // 再写一个，应触发 DropOldest
        await _pipeline.WriteAsync(CreatePoint("overflow"), CancellationToken.None);

        var stats = _pipeline.GetStats();
        stats.TotalReceived.Should().Be(_capacity + 1);
        stats.TotalDropped.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Reader_CanConsumeWrittenData()
    {
        var point = CreatePoint("tag-read");
        await _pipeline.WriteAsync(point, CancellationToken.None);

        var success = _pipeline.Reader.TryRead(out var read);

        success.Should().BeTrue();
        read.Should().NotBeNull();
        read!.TagId.Should().Be("tag-read");
    }

    [Fact]
    public void QueueCapacity_ReturnsConfiguredValue()
    {
        _pipeline.QueueCapacity.Should().Be(_capacity);
    }

    [Fact]
    public void Complete_PreventsNewWrites()
    {
        _pipeline.Complete();

        var canWrite = _pipeline.Writer.TryWrite(CreatePoint());

        canWrite.Should().BeFalse();
    }
}
