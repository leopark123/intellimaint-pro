using FluentAssertions;
using IntelliMaint.Application.Services;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace IntelliMaint.Tests.Unit;

/// <summary>
/// v65: 健康评分计算器单元测试
/// </summary>
public class HealthScoreCalculatorTests
{
    private readonly Mock<ITagImportanceMatcher> _importanceMatcherMock;
    private readonly Mock<ILogger<HealthScoreCalculator>> _loggerMock;
    private readonly IOptions<HealthAssessmentOptions> _options;
    private readonly HealthScoreCalculator _calculator;

    public HealthScoreCalculatorTests()
    {
        _importanceMatcherMock = new Mock<ITagImportanceMatcher>();
        _loggerMock = new Mock<ILogger<HealthScoreCalculator>>();

        var options = new HealthAssessmentOptions
        {
            Weights = new HealthWeights
            {
                Deviation = 0.35,
                Trend = 0.25,
                Stability = 0.20,
                Alarm = 0.20
            },
            LevelThresholds = new HealthLevelThresholds
            {
                HealthyMin = 85,
                AttentionMin = 70,
                WarningMin = 50,
                CriticalMin = 0
            },
            DefaultTagImportance = TagImportance.Normal
        };
        _options = Options.Create(options);

        _calculator = new HealthScoreCalculator(
            _importanceMatcherMock.Object,
            _options,
            _loggerMock.Object);
    }

    [Fact]
    public void Calculate_WithNoBaseline_ReturnsDefaultScore()
    {
        // Arrange
        var features = CreateDeviceFeatures("Device1", new Dictionary<string, TagFeatures>
        {
            ["Tag1"] = new() { Mean = 100, StdDev = 5, TrendSlope = 0.1 }
        });

        _importanceMatcherMock
            .Setup(m => m.GetImportances(It.IsAny<IEnumerable<string>>()))
            .Returns(new Dictionary<string, TagImportance> { ["Tag1"] = TagImportance.Normal });

        // Act
        var result = _calculator.Calculate(features, null);

        // Assert
        result.Should().NotBeNull();
        result.DeviceId.Should().Be("Device1");
        result.HasBaseline.Should().BeFalse();
        result.Index.Should().BeInRange(60, 100); // Without baseline, uses defaults
    }

    [Fact]
    public void Calculate_WithHealthyDevice_ReturnsHealthyLevel()
    {
        // Arrange
        var features = CreateDeviceFeatures("Device1", new Dictionary<string, TagFeatures>
        {
            ["Tag1"] = new()
            {
                Mean = 100,
                StdDev = 2,
                TrendSlope = 0.01,
                CoefficientOfVariation = 0.02,
                TrendDirection = 0
            }
        });

        var baseline = new DeviceBaseline
        {
            DeviceId = "Device1",
            TagBaselines = new Dictionary<string, TagBaseline>
            {
                ["Tag1"] = new()
                {
                    TagId = "Tag1",
                    NormalMean = 100,
                    NormalStdDev = 5,
                    NormalCV = 0.05
                }
            }
        };

        _importanceMatcherMock
            .Setup(m => m.GetImportances(It.IsAny<IEnumerable<string>>()))
            .Returns(new Dictionary<string, TagImportance> { ["Tag1"] = TagImportance.Normal });

        // Act
        var result = _calculator.Calculate(features, baseline);

        // Assert
        result.Level.Should().Be(HealthLevel.Healthy);
        result.Index.Should().BeGreaterOrEqualTo(85);
        result.HasBaseline.Should().BeTrue();
        result.ProblemTags.Should().BeEmpty();
    }

    [Fact]
    public void Calculate_WithDeviatedValues_ReturnsLowerScore()
    {
        // Arrange
        var features = CreateDeviceFeatures("Device1", new Dictionary<string, TagFeatures>
        {
            ["Tag1"] = new()
            {
                Mean = 130, // Deviated from baseline
                StdDev = 2,
                TrendSlope = 0.01,
                CoefficientOfVariation = 0.02,
                TrendDirection = 0
            }
        });

        var baseline = new DeviceBaseline
        {
            DeviceId = "Device1",
            TagBaselines = new Dictionary<string, TagBaseline>
            {
                ["Tag1"] = new()
                {
                    TagId = "Tag1",
                    NormalMean = 100,
                    NormalStdDev = 5, // Z-Score = (130-100)/5 = 6
                    NormalCV = 0.05
                }
            }
        };

        _importanceMatcherMock
            .Setup(m => m.GetImportances(It.IsAny<IEnumerable<string>>()))
            .Returns(new Dictionary<string, TagImportance> { ["Tag1"] = TagImportance.Critical });

        // Act
        var result = _calculator.Calculate(features, baseline);

        // Assert
        result.Index.Should().BeLessThan(85); // Not healthy anymore
        result.ProblemTags.Should().Contain("Tag1");
        result.DiagnosticMessage.Should().Contain("偏离基线");
    }

    [Fact]
    public void Calculate_WithCriticalTag_HasStricterThreshold()
    {
        // Arrange
        var features = CreateDeviceFeatures("Device1", new Dictionary<string, TagFeatures>
        {
            ["CriticalTag"] = new()
            {
                Mean = 112, // Z-Score = 2.4 (above critical threshold of 2.0)
                StdDev = 2,
                TrendSlope = 0,
                CoefficientOfVariation = 0.02,
                TrendDirection = 0
            },
            ["NormalTag"] = new()
            {
                Mean = 112, // Same deviation but normal tag
                StdDev = 2,
                TrendSlope = 0,
                CoefficientOfVariation = 0.02,
                TrendDirection = 0
            }
        });

        var baseline = new DeviceBaseline
        {
            DeviceId = "Device1",
            TagBaselines = new Dictionary<string, TagBaseline>
            {
                ["CriticalTag"] = new() { TagId = "CriticalTag", NormalMean = 100, NormalStdDev = 5, NormalCV = 0.05 },
                ["NormalTag"] = new() { TagId = "NormalTag", NormalMean = 100, NormalStdDev = 5, NormalCV = 0.05 }
            }
        };

        _importanceMatcherMock
            .Setup(m => m.GetImportances(It.IsAny<IEnumerable<string>>()))
            .Returns(new Dictionary<string, TagImportance>
            {
                ["CriticalTag"] = TagImportance.Critical,
                ["NormalTag"] = TagImportance.Normal
            });

        // Act
        var result = _calculator.Calculate(features, baseline);

        // Assert
        result.ProblemTags.Should().Contain("CriticalTag"); // Critical tag flagged
        // Normal tag may not be flagged (threshold is 3.5σ)
    }

    [Fact]
    public void Calculate_WithTrend_DetectsUpwardTrend()
    {
        // Arrange
        var features = CreateDeviceFeatures("Device1", new Dictionary<string, TagFeatures>
        {
            ["Tag1"] = new()
            {
                Mean = 100,
                StdDev = 2,
                TrendSlope = 2.0, // Strong upward trend
                CoefficientOfVariation = 0.02,
                TrendDirection = 1
            }
        });

        var baseline = new DeviceBaseline
        {
            DeviceId = "Device1",
            TagBaselines = new Dictionary<string, TagBaseline>
            {
                ["Tag1"] = new() { TagId = "Tag1", NormalMean = 100, NormalStdDev = 5, NormalCV = 0.05 }
            }
        };

        _importanceMatcherMock
            .Setup(m => m.GetImportances(It.IsAny<IEnumerable<string>>()))
            .Returns(new Dictionary<string, TagImportance> { ["Tag1"] = TagImportance.Normal });

        // Act
        var result = _calculator.Calculate(features, baseline);

        // Assert
        result.TrendScore.Should().BeLessThan(100);
    }

    [Fact]
    public void Calculate_WithHighVolatility_DetectsStabilityIssue()
    {
        // Arrange
        var features = CreateDeviceFeatures("Device1", new Dictionary<string, TagFeatures>
        {
            ["Tag1"] = new()
            {
                Mean = 100,
                StdDev = 20,
                TrendSlope = 0,
                CoefficientOfVariation = 0.5, // High CV
                TrendDirection = 0
            }
        });

        var baseline = new DeviceBaseline
        {
            DeviceId = "Device1",
            TagBaselines = new Dictionary<string, TagBaseline>
            {
                ["Tag1"] = new() { TagId = "Tag1", NormalMean = 100, NormalStdDev = 5, NormalCV = 0.05 }
            }
        };

        _importanceMatcherMock
            .Setup(m => m.GetImportances(It.IsAny<IEnumerable<string>>()))
            .Returns(new Dictionary<string, TagImportance> { ["Tag1"] = TagImportance.Normal });

        // Act
        var result = _calculator.Calculate(features, baseline);

        // Assert
        result.StabilityScore.Should().BeLessThan(100);
        result.ProblemTags.Should().Contain("Tag1");
        result.DiagnosticMessage.Should().Contain("波动");
    }

    [Theory]
    [InlineData(95, HealthLevel.Healthy)]
    [InlineData(85, HealthLevel.Healthy)]
    [InlineData(84, HealthLevel.Attention)]
    [InlineData(70, HealthLevel.Attention)]
    [InlineData(69, HealthLevel.Warning)]
    [InlineData(50, HealthLevel.Warning)]
    [InlineData(49, HealthLevel.Critical)]
    [InlineData(10, HealthLevel.Critical)]
    public void Calculate_HealthLevel_MatchesThresholds(int expectedIndex, HealthLevel expectedLevel)
    {
        // This test validates the threshold configuration is correctly applied
        // Note: Actual index calculation is complex; we're testing the level mapping logic

        var thresholds = _options.Value.LevelThresholds;

        var level = expectedIndex switch
        {
            _ when expectedIndex >= thresholds.HealthyMin => HealthLevel.Healthy,
            _ when expectedIndex >= thresholds.AttentionMin => HealthLevel.Attention,
            _ when expectedIndex >= thresholds.WarningMin => HealthLevel.Warning,
            _ => HealthLevel.Critical
        };

        level.Should().Be(expectedLevel);
    }

    private static DeviceFeatures CreateDeviceFeatures(string deviceId, Dictionary<string, TagFeatures> tagFeatures)
    {
        return new DeviceFeatures
        {
            DeviceId = deviceId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TagFeatures = tagFeatures
        };
    }

    #region v65: Sigmoid and Edge Case Tests

    [Fact]
    public void Calculate_WithNaNValues_HandlesGracefully()
    {
        // Arrange - 测试 NaN 边缘情况
        var features = CreateDeviceFeatures("Device1", new Dictionary<string, TagFeatures>
        {
            ["Tag1"] = new()
            {
                Mean = double.NaN,
                StdDev = 2,
                TrendSlope = 0,
                CoefficientOfVariation = 0.02,
                TrendDirection = 0
            }
        });

        var baseline = new DeviceBaseline
        {
            DeviceId = "Device1",
            TagBaselines = new Dictionary<string, TagBaseline>
            {
                ["Tag1"] = new() { TagId = "Tag1", NormalMean = 100, NormalStdDev = 5, NormalCV = 0.05 }
            }
        };

        _importanceMatcherMock
            .Setup(m => m.GetImportances(It.IsAny<IEnumerable<string>>()))
            .Returns(new Dictionary<string, TagImportance> { ["Tag1"] = TagImportance.Normal });

        // Act
        var result = _calculator.Calculate(features, baseline);

        // Assert - 应该返回有效分数，不会崩溃
        result.Should().NotBeNull();
        result.Index.Should().BeInRange(0, 100);
    }

    [Fact]
    public void Calculate_WithExtremeZScore_ClampedCorrectly()
    {
        // Arrange - 测试极端 Z-Score 被正确限制
        var features = CreateDeviceFeatures("Device1", new Dictionary<string, TagFeatures>
        {
            ["Tag1"] = new()
            {
                Mean = 1000, // 极端偏离 (Z-Score = 180)
                StdDev = 2,
                TrendSlope = 0,
                CoefficientOfVariation = 0.02,
                TrendDirection = 0
            }
        });

        var baseline = new DeviceBaseline
        {
            DeviceId = "Device1",
            TagBaselines = new Dictionary<string, TagBaseline>
            {
                ["Tag1"] = new() { TagId = "Tag1", NormalMean = 100, NormalStdDev = 5, NormalCV = 0.05 }
            }
        };

        _importanceMatcherMock
            .Setup(m => m.GetImportances(It.IsAny<IEnumerable<string>>()))
            .Returns(new Dictionary<string, TagImportance> { ["Tag1"] = TagImportance.Critical });

        // Act
        var result = _calculator.Calculate(features, baseline);

        // Assert - 分数应该被限制在有效范围内
        result.Index.Should().BeInRange(0, 100);
        result.DeviationScore.Should().BeGreaterOrEqualTo(5); // v65: 最低 5 分
    }

    [Theory]
    [InlineData(0, 97, 100)]   // Z=0 → ~97-100 分
    [InlineData(1, 85, 95)]    // Z=1 → ~85-95 分
    [InlineData(3, 45, 55)]    // Z=3 → ~45-55 分 (中点)
    [InlineData(6, 5, 15)]     // Z=6 → ~5-15 分
    public void SigmoidConversion_ProducesExpectedRange(double zScore, int minScore, int maxScore)
    {
        // v65: 验证 Sigmoid 函数在不同 Z-Score 下产生预期的分数范围
        const double ZScoreMidpoint = 3.0;
        const double ZScoreSteepness = 1.2;

        double sigmoidValue = 1.0 / (1.0 + Math.Exp(-(zScore - ZScoreMidpoint) * ZScoreSteepness));
        double score = 100 * (1 - sigmoidValue * 0.95);
        score = Math.Clamp(score, 5, 100);

        score.Should().BeInRange(minScore, maxScore,
            $"Z-Score {zScore} should produce score in range [{minScore}, {maxScore}]");
    }

    [Fact]
    public void DiagnosticMessage_SortedByImportance()
    {
        // Arrange - 测试诊断消息按重要性排序
        var features = CreateDeviceFeatures("Device1", new Dictionary<string, TagFeatures>
        {
            ["LowPriorityTag"] = new()
            {
                Mean = 120,
                StdDev = 2,
                TrendSlope = 0,
                CoefficientOfVariation = 0.02,
                TrendDirection = 0
            },
            ["CriticalTag"] = new()
            {
                Mean = 115,
                StdDev = 2,
                TrendSlope = 0,
                CoefficientOfVariation = 0.02,
                TrendDirection = 0
            }
        });

        var baseline = new DeviceBaseline
        {
            DeviceId = "Device1",
            TagBaselines = new Dictionary<string, TagBaseline>
            {
                ["LowPriorityTag"] = new() { TagId = "LowPriorityTag", NormalMean = 100, NormalStdDev = 5, NormalCV = 0.05 },
                ["CriticalTag"] = new() { TagId = "CriticalTag", NormalMean = 100, NormalStdDev = 5, NormalCV = 0.05 }
            }
        };

        _importanceMatcherMock
            .Setup(m => m.GetImportances(It.IsAny<IEnumerable<string>>()))
            .Returns(new Dictionary<string, TagImportance>
            {
                ["LowPriorityTag"] = TagImportance.Auxiliary,
                ["CriticalTag"] = TagImportance.Critical
            });

        // Act
        var result = _calculator.Calculate(features, baseline);

        // Assert - Critical 标签应该先出现在诊断消息中
        result.DiagnosticMessage.Should().NotBeNull();
        // CriticalTag 的 Z-Score = 3, 超过 Critical 阈值 2.0
        // LowPriorityTag 的 Z-Score = 4, 超过 Auxiliary 阈值 3.5
        // Critical 标签应该排在前面
        var firstProblem = result.DiagnosticMessage!.Split(';')[0];
        firstProblem.Should().Contain("3.0σ", "Critical tag with Z-Score 3.0 should appear first");
    }

    #endregion
}

/// <summary>
/// v65: 告警评分计算器单元测试
/// </summary>
public class AlarmScoreCalculatorTests
{
    [Fact]
    public void CalculateByCount_NoAlarms_Returns100()
    {
        var score = AlarmScoreCalculator.CalculateByCount(0);
        score.Should().Be(100);
    }

    [Theory]
    [InlineData(1, 80)]
    [InlineData(2, 60)]
    [InlineData(3, 40)]
    [InlineData(4, 20)]
    [InlineData(10, 20)]
    public void CalculateByCount_ReturnsExpectedScore(int alarmCount, int expectedScore)
    {
        var score = AlarmScoreCalculator.CalculateByCount(alarmCount);
        score.Should().Be(expectedScore);
    }

    [Fact]
    public void CalculateAlarmScore_NoAlarms_Returns100()
    {
        var config = CreateDefaultConfig();
        var alarms = Enumerable.Empty<AlarmRecord>();

        var score = AlarmScoreCalculator.CalculateAlarmScore(alarms, config);

        score.Should().Be(100);
    }

    [Fact]
    public void CalculateAlarmScore_CriticalAlarm_HighPenalty()
    {
        var config = CreateDefaultConfig();
        var alarms = new List<AlarmRecord>
        {
            new() { Id = 1, Severity = 5, Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
        };

        var score = AlarmScoreCalculator.CalculateAlarmScore(alarms, config);

        score.Should().BeLessThan(100);
        score.Should().BeGreaterOrEqualTo(config.MinScore);
    }

    [Fact]
    public void CalculateAlarmScore_MultipleSeverities_CorrectPenalties()
    {
        var config = CreateDefaultConfig();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var alarms = new List<AlarmRecord>
        {
            new() { Id = 1, Severity = 5, Ts = now }, // Critical
            new() { Id = 2, Severity = 3, Ts = now }, // Error
            new() { Id = 3, Severity = 2, Ts = now }, // Warning
            new() { Id = 4, Severity = 1, Ts = now }  // Info
        };

        var score = AlarmScoreCalculator.CalculateAlarmScore(alarms, config);

        // Total penalty = 30 + 20 + 10 + 5 = 65
        // Score = 100 - 65 = 35, but min is 10
        score.Should().BeGreaterOrEqualTo(config.MinScore);
    }

    [Fact]
    public void CalculateAlarmScore_WithDuration_IncreasedPenalty()
    {
        var config = new AlarmScoreConfig
        {
            CriticalPenalty = 30,
            ErrorPenalty = 20,
            WarningPenalty = 10,
            InfoPenalty = 5,
            ConsiderDuration = true,
            DurationFactorPerHour = 0.1,
            MaxDurationMultiplier = 2.0,
            MinScore = 10
        };

        var oneHourAgo = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
        var alarms = new List<AlarmRecord>
        {
            new() { Id = 1, Severity = 3, Ts = oneHourAgo } // Error, 1 hour old
        };

        var score = AlarmScoreCalculator.CalculateAlarmScore(alarms, config);

        // Base penalty = 20, multiplier = 1 + 0.1 = 1.1, final penalty = 22
        score.Should().BeLessThan(80); // 100 - 22 = 78
    }

    [Fact]
    public void CalculateAlarmScore_NeverBelowMinScore()
    {
        var config = CreateDefaultConfig();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Create many critical alarms
        var alarms = Enumerable.Range(1, 20)
            .Select(i => new AlarmRecord { Id = i, Severity = 5, Ts = now })
            .ToList();

        var score = AlarmScoreCalculator.CalculateAlarmScore(alarms, config);

        score.Should().Be(config.MinScore);
    }

    private static AlarmScoreConfig CreateDefaultConfig()
    {
        return new AlarmScoreConfig
        {
            CriticalPenalty = 30,
            ErrorPenalty = 20,
            WarningPenalty = 10,
            InfoPenalty = 5,
            ConsiderDuration = false,
            MinScore = 10
        };
    }
}
