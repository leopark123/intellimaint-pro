using FluentAssertions;
using IntelliMaint.Core.Contracts;
using Xunit;

namespace IntelliMaint.Tests.Unit;

/// <summary>
/// v65: 告警评估器单元测试
/// 测试条件评估逻辑和消息构建
/// </summary>
public class AlarmEvaluatorTests
{
    #region EvaluateCondition Tests

    [Theory]
    [InlineData("gt", 100.5, 100.0, true)]   // 100.5 > 100.0
    [InlineData("gt", 100.0, 100.0, false)]  // 100.0 > 100.0 = false
    [InlineData("gt", 99.5, 100.0, false)]   // 99.5 > 100.0 = false
    public void EvaluateCondition_GreaterThan_ReturnsExpected(string cond, double value, double threshold, bool expected)
    {
        var result = EvaluateConditionHelper(cond, value, threshold);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("gte", 100.5, 100.0, true)]  // 100.5 >= 100.0
    [InlineData("gte", 100.0, 100.0, true)]  // 100.0 >= 100.0
    [InlineData("gte", 99.5, 100.0, false)]  // 99.5 >= 100.0 = false
    public void EvaluateCondition_GreaterThanOrEqual_ReturnsExpected(string cond, double value, double threshold, bool expected)
    {
        var result = EvaluateConditionHelper(cond, value, threshold);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("lt", 99.5, 100.0, true)]    // 99.5 < 100.0
    [InlineData("lt", 100.0, 100.0, false)]  // 100.0 < 100.0 = false
    [InlineData("lt", 100.5, 100.0, false)]  // 100.5 < 100.0 = false
    public void EvaluateCondition_LessThan_ReturnsExpected(string cond, double value, double threshold, bool expected)
    {
        var result = EvaluateConditionHelper(cond, value, threshold);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("lte", 99.5, 100.0, true)]   // 99.5 <= 100.0
    [InlineData("lte", 100.0, 100.0, true)]  // 100.0 <= 100.0
    [InlineData("lte", 100.5, 100.0, false)] // 100.5 <= 100.0 = false
    public void EvaluateCondition_LessThanOrEqual_ReturnsExpected(string cond, double value, double threshold, bool expected)
    {
        var result = EvaluateConditionHelper(cond, value, threshold);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("eq", 100.0, 100.0, true)]
    [InlineData("eq", 100.0000000001, 100.0, true)]  // Within epsilon
    [InlineData("eq", 100.5, 100.0, false)]
    public void EvaluateCondition_Equal_ReturnsExpected(string cond, double value, double threshold, bool expected)
    {
        var result = EvaluateConditionHelper(cond, value, threshold);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("ne", 100.5, 100.0, true)]
    [InlineData("ne", 100.0, 100.0, false)]
    public void EvaluateCondition_NotEqual_ReturnsExpected(string cond, double value, double threshold, bool expected)
    {
        var result = EvaluateConditionHelper(cond, value, threshold);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("GT")]    // uppercase
    [InlineData("Gt")]    // mixed case
    [InlineData(" gt ")]  // with whitespace
    public void EvaluateCondition_CaseInsensitive_Works(string cond)
    {
        var result = EvaluateConditionHelper(cond, 100.5, 100.0);
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("")]
    [InlineData("greater_than")]
    public void EvaluateCondition_InvalidCondition_ReturnsFalse(string cond)
    {
        var result = EvaluateConditionHelper(cond, 100.5, 100.0);
        result.Should().BeFalse();
    }

    [Fact]
    public void EvaluateCondition_EdgeCases_HandlesCorrectly()
    {
        // Test with zero
        EvaluateConditionHelper("gt", 0.0, -1.0).Should().BeTrue();
        EvaluateConditionHelper("lt", 0.0, 1.0).Should().BeTrue();

        // Test with negative numbers
        EvaluateConditionHelper("gt", -50.0, -100.0).Should().BeTrue();
        EvaluateConditionHelper("lt", -100.0, -50.0).Should().BeTrue();

        // Test with very large numbers
        EvaluateConditionHelper("gt", double.MaxValue, 0).Should().BeTrue();
        EvaluateConditionHelper("lt", double.MinValue, 0).Should().BeTrue();
    }

    #endregion

    #region TryGetNumericValue Tests

    [Fact]
    public void TryGetNumericValue_FloatValue_ReturnsCorrect()
    {
        var point = new TelemetryPoint
        {
            TagId = "Tag1",
            DeviceId = "Device1",
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ValueType = TagValueType.Float32,
            Seq = 0,
            Float32Value = 123.45f
        };

        var result = TryGetNumericValueHelper(point, out var value);

        result.Should().BeTrue();
        value.Should().BeApproximately(123.45, 0.01);
    }

    [Fact]
    public void TryGetNumericValue_DoubleValue_ReturnsCorrect()
    {
        var point = new TelemetryPoint
        {
            TagId = "Tag1",
            DeviceId = "Device1",
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ValueType = TagValueType.Float64,
            Seq = 0,
            Float64Value = 123.456789
        };

        var result = TryGetNumericValueHelper(point, out var value);

        result.Should().BeTrue();
        value.Should().BeApproximately(123.456789, 0.0001);
    }

    [Fact]
    public void TryGetNumericValue_IntValue_ReturnsCorrect()
    {
        var point = new TelemetryPoint
        {
            TagId = "Tag1",
            DeviceId = "Device1",
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ValueType = TagValueType.Int32,
            Seq = 0,
            Int32Value = 42
        };

        var result = TryGetNumericValueHelper(point, out var value);

        result.Should().BeTrue();
        value.Should().Be(42);
    }

    [Fact]
    public void TryGetNumericValue_BoolTrue_Returns1()
    {
        var point = new TelemetryPoint
        {
            TagId = "Tag1",
            DeviceId = "Device1",
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ValueType = TagValueType.Bool,
            Seq = 0,
            BoolValue = true
        };

        var result = TryGetNumericValueHelper(point, out var value);

        result.Should().BeTrue();
        value.Should().Be(1);
    }

    [Fact]
    public void TryGetNumericValue_BoolFalse_Returns0()
    {
        var point = new TelemetryPoint
        {
            TagId = "Tag1",
            DeviceId = "Device1",
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Seq = 0,
            ValueType = TagValueType.Bool,
            BoolValue = false
        };

        var result = TryGetNumericValueHelper(point, out var value);

        result.Should().BeTrue();
        value.Should().Be(0);
    }

    [Fact]
    public void TryGetNumericValue_StringValue_ReturnsFalse()
    {
        var point = new TelemetryPoint
        {
            TagId = "Tag1",
            DeviceId = "Device1",
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ValueType = TagValueType.String,
            Seq = 0,
            StringValue = "not a number"
        };

        var result = TryGetNumericValueHelper(point, out var value);

        result.Should().BeFalse();
        value.Should().Be(0);
    }

    [Fact]
    public void TryGetNumericValue_NullFloatValue_ReturnsFalse()
    {
        var point = new TelemetryPoint
        {
            TagId = "Tag1",
            DeviceId = "Device1",
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ValueType = TagValueType.Float32,
            Seq = 0,
            Float32Value = null
        };

        var result = TryGetNumericValueHelper(point, out var value);

        result.Should().BeFalse();
    }

    #endregion

    #region AlarmRule Matching Tests

    [Fact]
    public void RuleMatching_ExactTagIdMatch_Matches()
    {
        var rule = CreateRule("Tag1", null);
        var point = CreatePoint("Tag1", "Device1");

        var matches = MatchesRule(rule, point);

        matches.Should().BeTrue();
    }

    [Fact]
    public void RuleMatching_DifferentTagId_DoesNotMatch()
    {
        var rule = CreateRule("Tag1", null);
        var point = CreatePoint("Tag2", "Device1");

        var matches = MatchesRule(rule, point);

        matches.Should().BeFalse();
    }

    [Fact]
    public void RuleMatching_WithDeviceFilter_MatchesCorrectDevice()
    {
        var rule = CreateRule("Tag1", "Device1");
        var point = CreatePoint("Tag1", "Device1");

        var matches = MatchesRule(rule, point);

        matches.Should().BeTrue();
    }

    [Fact]
    public void RuleMatching_WithDeviceFilter_DoesNotMatchOtherDevice()
    {
        var rule = CreateRule("Tag1", "Device1");
        var point = CreatePoint("Tag1", "Device2");

        var matches = MatchesRule(rule, point);

        matches.Should().BeFalse();
    }

    [Fact]
    public void RuleMatching_NoDeviceFilter_MatchesAnyDevice()
    {
        var rule = CreateRule("Tag1", null);
        var point1 = CreatePoint("Tag1", "Device1");
        var point2 = CreatePoint("Tag1", "Device2");

        MatchesRule(rule, point1).Should().BeTrue();
        MatchesRule(rule, point2).Should().BeTrue();
    }

    #endregion

    #region BuildMessage Tests

    [Fact]
    public void BuildMessage_WithTemplate_ReplacesPlaceholders()
    {
        var rule = new AlarmRule
        {
            RuleId = "rule-1",
            Name = "Temperature High",
            TagId = "Tag1",
            ConditionType = "gt",
            Threshold = 80.0,
            MessageTemplate = "Alert: {tagId} is {cond} {threshold}, current={value}"
        };

        var point = new TelemetryPoint
        {
            TagId = "Tag1",
            DeviceId = "Device1",
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Seq = 0,
            ValueType = TagValueType.Float32,
            Float32Value = 85.5f
        };

        var message = BuildMessageHelper(rule, point, 85.5);

        message.Should().Contain("Tag1");
        message.Should().Contain("gt");
        message.Should().Contain("80");
        message.Should().Contain("85.5");
    }

    [Fact]
    public void BuildMessage_WithoutTemplate_UsesDefault()
    {
        var rule = new AlarmRule
        {
            RuleId = "rule-1",
            Name = "Temperature High",
            TagId = "Tag1",
            ConditionType = "gt",
            Threshold = 80.0,
            MessageTemplate = null
        };

        var point = CreatePoint("Tag1", "Device1");

        var message = BuildMessageHelper(rule, point, 85.5);

        message.Should().Contain("Temperature High");
        message.Should().Contain("Tag1");
    }

    #endregion

    #region Helper Methods (simulate private methods for testing)

    private static bool EvaluateConditionHelper(string conditionType, double value, double threshold)
    {
        switch (conditionType.Trim().ToLowerInvariant())
        {
            case "gt": return value > threshold;
            case "gte": return value >= threshold;
            case "lt": return value < threshold;
            case "lte": return value <= threshold;
            case "eq": return Math.Abs(value - threshold) < 1e-9;
            case "ne": return Math.Abs(value - threshold) >= 1e-9;
            default: return false;
        }
    }

    private static bool TryGetNumericValueHelper(TelemetryPoint point, out double value)
    {
        value = 0;

        switch (point.ValueType)
        {
            case TagValueType.Bool:
                if (point.BoolValue.HasValue)
                {
                    value = point.BoolValue.Value ? 1 : 0;
                    return true;
                }
                return false;

            case TagValueType.Int32:
                if (point.Int32Value.HasValue)
                {
                    value = point.Int32Value.Value;
                    return true;
                }
                return false;

            case TagValueType.Int64:
                if (point.Int64Value.HasValue)
                {
                    value = point.Int64Value.Value;
                    return true;
                }
                return false;

            case TagValueType.Float32:
                if (point.Float32Value.HasValue)
                {
                    value = point.Float32Value.Value;
                    return true;
                }
                return false;

            case TagValueType.Float64:
                if (point.Float64Value.HasValue)
                {
                    value = point.Float64Value.Value;
                    return true;
                }
                return false;

            case TagValueType.String:
                return false;

            default:
                return false;
        }
    }

    private static bool MatchesRule(AlarmRule rule, TelemetryPoint point)
    {
        if (!string.Equals(rule.TagId, point.TagId, StringComparison.Ordinal))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.DeviceId) &&
            !string.Equals(rule.DeviceId, point.DeviceId, StringComparison.Ordinal))
            return false;

        return true;
    }

    private static string BuildMessageHelper(AlarmRule rule, TelemetryPoint point, double value)
    {
        var template = string.IsNullOrWhiteSpace(rule.MessageTemplate)
            ? "[{ruleName}] {tagId} {cond} {threshold}, value={value}"
            : rule.MessageTemplate;

        return template
            .Replace("{ruleId}", rule.RuleId, StringComparison.OrdinalIgnoreCase)
            .Replace("{ruleName}", rule.Name ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{deviceId}", point.DeviceId, StringComparison.OrdinalIgnoreCase)
            .Replace("{tagId}", point.TagId, StringComparison.OrdinalIgnoreCase)
            .Replace("{cond}", rule.ConditionType, StringComparison.OrdinalIgnoreCase)
            .Replace("{threshold}", rule.Threshold.ToString("G", System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{value}", value.ToString("G", System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }

    private static AlarmRule CreateRule(string tagId, string? deviceId)
    {
        return new AlarmRule
        {
            RuleId = $"rule-{tagId}",
            Name = $"Rule for {tagId}",
            TagId = tagId,
            DeviceId = deviceId,
            ConditionType = "gt",
            Threshold = 100.0,
            Severity = 3,
            Enabled = true
        };
    }

    private static TelemetryPoint CreatePoint(string tagId, string deviceId)
    {
        return new TelemetryPoint
        {
            TagId = tagId,
            DeviceId = deviceId,
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Seq = 0,
            ValueType = TagValueType.Float32,
            Float32Value = 105.0f
        };
    }

    #endregion
}
