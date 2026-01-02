using FluentAssertions;
using IntelliMaint.Core.Contracts;
using Xunit;

namespace IntelliMaint.Tests.Unit;

public class TelemetryPointTests
{
    [Fact]
    public void FromInt32_ShouldCreateValidPoint()
    {
        // Arrange
        var deviceId = "PLC-001";
        var tagId = "Motor.Speed";
        var value = 1500;

        // Act
        var point = TelemetryPoint.FromInt32(deviceId, tagId, value);

        // Assert
        point.DeviceId.Should().Be(deviceId);
        point.TagId.Should().Be(tagId);
        point.ValueType.Should().Be(TagValueType.Int32);
        point.Int32Value.Should().Be(value);
        point.Quality.Should().Be(192);
        point.IsValid().Should().BeTrue();
    }

    [Fact]
    public void FromFloat32_ShouldCreateValidPoint()
    {
        // Arrange
        var deviceId = "PLC-001";
        var tagId = "Motor.Current";
        var value = 12.5f;

        // Act
        var point = TelemetryPoint.FromFloat32(deviceId, tagId, value);

        // Assert
        point.DeviceId.Should().Be(deviceId);
        point.TagId.Should().Be(tagId);
        point.ValueType.Should().Be(TagValueType.Float32);
        point.Float32Value.Should().Be(value);
        point.IsValid().Should().BeTrue();
    }

    [Fact]
    public void FromBool_ShouldCreateValidPoint()
    {
        // Arrange
        var deviceId = "PLC-001";
        var tagId = "Motor.Running";
        var value = true;

        // Act
        var point = TelemetryPoint.FromBool(deviceId, tagId, value);

        // Assert
        point.ValueType.Should().Be(TagValueType.Bool);
        point.BoolValue.Should().Be(true);
        point.IsValid().Should().BeTrue();
    }

    [Fact]
    public void IsValid_ShouldReturnFalse_WhenTypeMismatch()
    {
        // Arrange - create a point with Int32 type but no Int32Value
        var point = new TelemetryPoint
        {
            DeviceId = "PLC-001",
            TagId = "Test",
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Seq = 1,
            ValueType = TagValueType.Int32,
            // No Int32Value set
            Quality = 192
        };

        // Act & Assert
        point.IsValid().Should().BeFalse();
    }

    [Fact]
    public void GenerateSeq_ShouldBeMonotonicallyIncreasing()
    {
        // Act
        var seq1 = TelemetryPoint.GenerateSeq();
        var seq2 = TelemetryPoint.GenerateSeq();
        var seq3 = TelemetryPoint.GenerateSeq();

        // Assert
        seq2.Should().BeGreaterThan(seq1);
        seq3.Should().BeGreaterThan(seq2);
    }

    [Fact]
    public void GetValue_ShouldReturnCorrectValue()
    {
        // Arrange
        var point = TelemetryPoint.FromInt32("PLC-001", "Tag", 42);

        // Act
        var value = point.GetValue();

        // Assert
        value.Should().Be(42);
    }
}

public class PageTokenTests
{
    [Fact]
    public void Parse_ShouldParseValidToken()
    {
        // Arrange
        var tokenStr = "1234567890_100";

        // Act
        var token = PageToken.Parse(tokenStr);

        // Assert
        token.Should().NotBeNull();
        token!.LastTs.Should().Be(1234567890);
        token.LastSeq.Should().Be(100);
    }

    [Fact]
    public void Parse_ShouldReturnNull_ForInvalidToken()
    {
        // Arrange
        var invalidToken = "invalid";

        // Act
        var token = PageToken.Parse(invalidToken);

        // Assert
        token.Should().BeNull();
    }

    [Fact]
    public void ToString_ShouldFormatCorrectly()
    {
        // Arrange
        var token = new PageToken(1234567890, 100);

        // Act
        var str = token.ToString();

        // Assert
        str.Should().Be("1234567890_100");
    }
}
