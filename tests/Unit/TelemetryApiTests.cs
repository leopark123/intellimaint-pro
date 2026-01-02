using FluentAssertions;
using IntelliMaint.Core.Contracts;
using IntelliMaint.Host.Api.Models;
using Xunit;

namespace IntelliMaint.Tests.Unit;

/// <summary>
/// v48: 遥测数据模型测试
/// </summary>
public class TelemetryModelTests
{
    [Fact]
    public void TelemetryQueryRequest_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var request = new TelemetryQueryRequest();

        // Assert
        request.DeviceId.Should().BeNull();
        request.TagId.Should().BeNull();
        request.StartTs.Should().BeNull();
        request.EndTs.Should().BeNull();
        request.Limit.Should().Be(1000);
        request.CursorTs.Should().BeNull();
        request.CursorSeq.Should().BeNull();
    }

    [Fact]
    public void TelemetryQueryRequest_ShouldAcceptCursorParameters()
    {
        // Arrange & Act
        var request = new TelemetryQueryRequest
        {
            DeviceId = "device-1",
            TagId = "tag-1",
            Limit = 100,
            CursorTs = 1704067200000,
            CursorSeq = 5
        };

        // Assert
        request.DeviceId.Should().Be("device-1");
        request.TagId.Should().Be("tag-1");
        request.Limit.Should().Be(100);
        request.CursorTs.Should().Be(1704067200000);
        request.CursorSeq.Should().Be(5);
    }

    [Fact]
    public void PagedApiResponse_ShouldContainPaginationInfo()
    {
        // Arrange
        var data = new List<TelemetryDataPoint>
        {
            new() { DeviceId = "d1", TagId = "t1", Ts = 1000, Seq = 1, Value = 42.0, ValueType = "Float64", Quality = 192 },
            new() { DeviceId = "d1", TagId = "t1", Ts = 999, Seq = 1, Value = 41.0, ValueType = "Float64", Quality = 192 }
        };

        // Act
        var response = new PagedApiResponse<IReadOnlyList<TelemetryDataPoint>>
        {
            Success = true,
            Data = data,
            Count = 2,
            HasMore = true,
            NextCursor = "999:1"
        };

        // Assert
        response.Success.Should().BeTrue();
        response.Data.Should().HaveCount(2);
        response.Count.Should().Be(2);
        response.HasMore.Should().BeTrue();
        response.NextCursor.Should().Be("999:1");
        response.Timestamp.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TelemetryDataPoint_ShouldIncludeSeqField()
    {
        // Arrange & Act
        var point = new TelemetryDataPoint
        {
            DeviceId = "device-1",
            TagId = "tag-1",
            Ts = 1704067200000,
            Seq = 3,
            Value = 123.45,
            ValueType = "Float64",
            Quality = 192,
            Unit = "°C"
        };

        // Assert
        point.Seq.Should().Be(3);
        point.Ts.Should().Be(1704067200000);
    }
}

/// <summary>
/// v48: 聚合查询模型测试
/// </summary>
public class AggregateModelTests
{
    [Fact]
    public void AggregateQueryRequest_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var request = new AggregateQueryRequest();

        // Assert
        request.IntervalMs.Should().Be(60000);
        request.Function.Should().Be("avg");
    }

    [Theory]
    [InlineData("avg")]
    [InlineData("min")]
    [InlineData("max")]
    [InlineData("sum")]
    [InlineData("count")]
    public void AggregateQueryRequest_ShouldAcceptValidFunctions(string function)
    {
        // Arrange & Act
        var request = new AggregateQueryRequest
        {
            DeviceId = "device-1",
            TagId = "tag-1",
            StartTs = 1000,
            EndTs = 2000,
            Function = function
        };

        // Assert
        request.Function.Should().Be(function);
    }
}

/// <summary>
/// v48: API 响应模型测试
/// </summary>
public class ApiResponseTests
{
    [Fact]
    public void ApiResponse_ShouldHaveTimestamp()
    {
        // Arrange
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Act
        var response = new ApiResponse<string>
        {
            Success = true,
            Data = "test"
        };

        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Assert
        response.Timestamp.Should().BeGreaterOrEqualTo(before);
        response.Timestamp.Should().BeLessOrEqualTo(after);
    }

    [Fact]
    public void ApiResponse_ShouldDefaultToSuccess()
    {
        // Arrange & Act
        var response = new ApiResponse<object>();

        // Assert
        response.Success.Should().BeTrue();
    }

    [Fact]
    public void ApiResponse_ShouldContainError()
    {
        // Arrange & Act
        var response = new ApiResponse<object>
        {
            Success = false,
            Error = "Something went wrong"
        };

        // Assert
        response.Success.Should().BeFalse();
        response.Error.Should().Be("Something went wrong");
    }
}
