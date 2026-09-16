using IntelliMaint.Application.Services;
using IntelliMaint.Host.Api.Services;
using Xunit;

namespace IntelliMaint.Tests.Unit;

public sealed class DemoAndFftTests
{
    [Fact]
    public async Task NativeSqliteIncludesAggregateOverflowSecurityFix()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version()";
        var version = Version.Parse((string)(await command.ExecuteScalarAsync())!);
        Assert.True(version >= new Version(3, 50, 2), $"Native SQLite {version} predates the CVE-2025-6965 fix.");
    }

    [Fact]
    public void DemoProducesFiveLabelledSignalsAndARepeatableAlarmCycle()
    {
        var hot = SyntheticDemoService.CreateSample(1000, 0);
        var normal = SyntheticDemoService.CreateSample(2000, 30);
        Assert.Equal(5, hot.Count);
        Assert.All(hot, p => Assert.Equal("Synthetic Demo Data", p.Source));
        Assert.All(hot, p => Assert.Equal("synthetic", p.Protocol));
        Assert.True(hot.Single(p => p.TagId.EndsWith("Temperature")).Float64Value > 85);
        Assert.True(normal.Single(p => p.TagId.EndsWith("Temperature")).Float64Value < 85);
        Assert.Equal(5, hot.Select(p => p.TagId).Distinct().Count());
    }

    [Fact]
    public void FftFindsKnownSinusoidFrequency()
    {
        const int count = 1024;
        const double rate = 1024;
        var samples = Enumerable.Range(0, count).Select(i => Math.Sin(2 * Math.PI * 64 * i / rate)).ToArray();
        var result = new MotorFftAnalyzer().Analyze(samples, rate, new MotorFftParams { SupplyFrequency = 64 });
        Assert.InRange(result.PeakFrequency, 63, 65);
        Assert.True(result.PeakAmplitude > 0);
    }
}
