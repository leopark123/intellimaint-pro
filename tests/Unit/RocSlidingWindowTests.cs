using System;
using FluentAssertions;
using IntelliMaint.Infrastructure.Pipeline;
using Xunit;

namespace IntelliMaint.Tests.Unit;

/// <summary>
/// v56: 变化率滑动窗口单元测试
/// </summary>
public class RocSlidingWindowTests
{
    private readonly RocSlidingWindow _window;

    public RocSlidingWindowTests()
    {
        _window = new RocSlidingWindow();
    }

    #region 基础功能测试

    [Fact]
    public void Add_SinglePoint_IncrementsCount()
    {
        _window.Add("device1", "tag1", 1000, 100.0);

        _window.TagCount.Should().Be(1);
        _window.TotalPointCount.Should().Be(1);
    }

    [Fact]
    public void Add_MultiplePoints_SameTag_AccumulatesCount()
    {
        _window.Add("device1", "tag1", 1000, 100.0);
        _window.Add("device1", "tag1", 2000, 110.0);
        _window.Add("device1", "tag1", 3000, 120.0);

        _window.TagCount.Should().Be(1);
        _window.TotalPointCount.Should().Be(3);
    }

    [Fact]
    public void Add_MultiplePoints_DifferentTags_CreatesMultipleWindows()
    {
        _window.Add("device1", "tag1", 1000, 100.0);
        _window.Add("device1", "tag2", 1000, 200.0);
        _window.Add("device2", "tag1", 1000, 300.0);

        _window.TagCount.Should().Be(3);
        _window.TotalPointCount.Should().Be(3);
    }

    #endregion

    #region 窗口统计测试

    [Fact]
    public void GetWindowStats_NoData_ReturnsNull()
    {
        var stats = _window.GetWindowStats("device1", "tag1", 60000);

        stats.Should().BeNull();
    }

    [Fact]
    public void GetWindowStats_WithData_ReturnsCorrectStats()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _window.Add("device1", "tag1", now - 30000, 100.0);  // 30秒前
        _window.Add("device1", "tag1", now - 20000, 150.0);  // 20秒前
        _window.Add("device1", "tag1", now - 10000, 120.0);  // 10秒前

        var stats = _window.GetWindowStats("device1", "tag1", 60000);  // 60秒窗口

        stats.Should().NotBeNull();
        stats!.Count.Should().Be(3);
        stats.Min.Should().Be(100.0);
        stats.Max.Should().Be(150.0);
        stats.First.Should().Be(100.0);
        stats.Last.Should().Be(120.0);
    }

    [Fact]
    public void GetWindowStats_OldDataOutsideWindow_ExcludesOldData()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _window.Add("device1", "tag1", now - 120000, 50.0);   // 2分钟前（窗口外）
        _window.Add("device1", "tag1", now - 30000, 100.0);   // 30秒前（窗口内）
        _window.Add("device1", "tag1", now - 10000, 150.0);   // 10秒前（窗口内）

        var stats = _window.GetWindowStats("device1", "tag1", 60000);  // 60秒窗口

        stats.Should().NotBeNull();
        stats!.Count.Should().Be(2);  // 只有2个点在窗口内
        stats.Min.Should().Be(100.0);
        stats.Max.Should().Be(150.0);
    }

    #endregion

    #region 变化率计算测试

    [Fact]
    public void GetRateOfChange_InsufficientData_ReturnsNull()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _window.Add("device1", "tag1", now, 100.0);

        var roc = _window.GetRateOfChange("device1", "tag1", 60000);

        roc.Should().BeNull();  // 需要至少2个数据点
    }

    [Fact]
    public void GetRateOfChange_TwoPoints_CalculatesCorrectly()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _window.Add("device1", "tag1", now - 30000, 100.0);
        _window.Add("device1", "tag1", now - 10000, 150.0);

        var roc = _window.GetRateOfChange("device1", "tag1", 60000);

        roc.Should().NotBeNull();
        roc!.Count.Should().Be(2);
        roc.First.Should().Be(100.0);
        roc.Last.Should().Be(150.0);
        roc.Min.Should().Be(100.0);
        roc.Max.Should().Be(150.0);
        roc.AbsoluteChange.Should().Be(50.0);  // max - min
        roc.PercentChange.Should().Be(50.0);   // |50/100| * 100 = 50%
    }

    [Fact]
    public void GetRateOfChange_DecreasingValues_CalculatesCorrectly()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _window.Add("device1", "tag1", now - 30000, 200.0);
        _window.Add("device1", "tag1", now - 10000, 100.0);

        var roc = _window.GetRateOfChange("device1", "tag1", 60000);

        roc.Should().NotBeNull();
        roc!.AbsoluteChange.Should().Be(100.0);  // max - min = 200 - 100
        roc.PercentChange.Should().Be(50.0);     // |100/200| * 100 = 50%
    }

    [Fact]
    public void GetRateOfChange_ZeroBaseline_ReturnsZeroPercent()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _window.Add("device1", "tag1", now - 30000, 0.0);
        _window.Add("device1", "tag1", now - 10000, 100.0);

        var roc = _window.GetRateOfChange("device1", "tag1", 60000);

        roc.Should().NotBeNull();
        roc!.AbsoluteChange.Should().Be(100.0);
        roc.PercentChange.Should().Be(0.0);  // 基线为0时，百分比变化为0
    }

    [Fact]
    public void GetRateOfChange_NoChange_ReturnsZero()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _window.Add("device1", "tag1", now - 30000, 100.0);
        _window.Add("device1", "tag1", now - 20000, 100.0);
        _window.Add("device1", "tag1", now - 10000, 100.0);

        var roc = _window.GetRateOfChange("device1", "tag1", 60000);

        roc.Should().NotBeNull();
        roc!.AbsoluteChange.Should().Be(0.0);
        roc.PercentChange.Should().Be(0.0);
    }

    [Fact]
    public void GetRateOfChange_WithSpike_CapturesMaxChange()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _window.Add("device1", "tag1", now - 40000, 100.0);
        _window.Add("device1", "tag1", now - 30000, 200.0);  // 峰值
        _window.Add("device1", "tag1", now - 20000, 50.0);   // 低谷
        _window.Add("device1", "tag1", now - 10000, 110.0);

        var roc = _window.GetRateOfChange("device1", "tag1", 60000);

        roc.Should().NotBeNull();
        roc!.Min.Should().Be(50.0);
        roc.Max.Should().Be(200.0);
        roc.AbsoluteChange.Should().Be(150.0);  // 200 - 50
    }

    #endregion

    #region 清理测试

    [Fact]
    public void CleanupExpired_RemovesOldData()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var oneHourAgo = now - 3600_000;
        var twoHoursAgo = now - 7200_000;

        // 添加一些旧数据（超过1小时）
        _window.Add("device1", "tag1", twoHoursAgo, 100.0);
        _window.Add("device1", "tag1", twoHoursAgo + 1000, 110.0);

        // 添加一些新数据（在1小时内）
        _window.Add("device1", "tag1", now - 30000, 150.0);

        _window.TotalPointCount.Should().Be(3);

        // 执行清理
        _window.CleanupExpired();

        // 只剩下新数据
        _window.TotalPointCount.Should().Be(1);
    }

    [Fact]
    public void CleanupExpired_RemovesEmptyWindows()
    {
        var twoHoursAgo = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 7200_000;

        // 添加过期数据
        _window.Add("device1", "tag1", twoHoursAgo, 100.0);

        _window.TagCount.Should().Be(1);

        // 执行清理
        _window.CleanupExpired();

        // 窗口应该被移除
        _window.TagCount.Should().Be(0);
    }

    #endregion

    #region 边界条件测试

    [Fact]
    public void GetRateOfChange_SmallValues_HandlesFloatingPointCorrectly()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _window.Add("device1", "tag1", now - 30000, 0.001);
        _window.Add("device1", "tag1", now - 10000, 0.002);

        var roc = _window.GetRateOfChange("device1", "tag1", 60000);

        roc.Should().NotBeNull();
        roc!.AbsoluteChange.Should().BeApproximately(0.001, 1e-9);
        roc.PercentChange.Should().BeApproximately(100.0, 0.1);  // 100% 变化
    }

    [Fact]
    public void GetRateOfChange_LargeValues_HandlesCorrectly()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _window.Add("device1", "tag1", now - 30000, 1_000_000.0);
        _window.Add("device1", "tag1", now - 10000, 1_100_000.0);

        var roc = _window.GetRateOfChange("device1", "tag1", 60000);

        roc.Should().NotBeNull();
        roc!.AbsoluteChange.Should().Be(100_000.0);
        roc.PercentChange.Should().BeApproximately(10.0, 0.1);  // 10% 变化
    }

    [Fact]
    public void GetRateOfChange_NegativeValues_HandlesCorrectly()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _window.Add("device1", "tag1", now - 30000, -100.0);
        _window.Add("device1", "tag1", now - 10000, -50.0);

        var roc = _window.GetRateOfChange("device1", "tag1", 60000);

        roc.Should().NotBeNull();
        roc!.Min.Should().Be(-100.0);
        roc.Max.Should().Be(-50.0);
        roc.AbsoluteChange.Should().Be(50.0);
    }

    #endregion
}
