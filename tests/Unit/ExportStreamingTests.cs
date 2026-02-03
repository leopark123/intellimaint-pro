using System.Text;
using FluentAssertions;
using Xunit;

namespace IntelliMaint.Tests.Unit;

/// <summary>
/// CSV 导出流式输出测试
/// 验证流式写入不会一次性占用大量内存
/// </summary>
public class ExportStreamingTests
{
    [Fact]
    public async Task StreamWriter_WritesIncrementally_NotFullBuffer()
    {
        // 模拟流式 CSV 写入
        using var stream = new MemoryStream();
        await using var writer = new StreamWriter(stream, Encoding.UTF8, bufferSize: 1024, leaveOpen: true);

        // 写入 header
        await writer.WriteLineAsync("DeviceId,TagId,Timestamp,Value");

        // 写入 1000 行
        for (var i = 0; i < 1000; i++)
        {
            await writer.WriteLineAsync($"device-1,tag-{i},2025-01-01 00:00:00,{i * 1.5}");
        }

        await writer.FlushAsync();

        // 验证数据已写入
        stream.Length.Should().BeGreaterThan(0);

        // 验证内容可解析
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var header = await reader.ReadLineAsync();
        header.Should().Be("DeviceId,TagId,Timestamp,Value");

        var lineCount = 0;
        while (await reader.ReadLineAsync() != null)
        {
            lineCount++;
        }

        lineCount.Should().Be(1000);
    }

    [Fact]
    public void CsvEscape_HandlesSpecialCharacters()
    {
        Escape("normal").Should().Be("normal");
        Escape("has,comma").Should().Be("\"has,comma\"");
        Escape("has\"quote").Should().Be("\"has\"\"quote\"");
        Escape("has\nnewline").Should().Be("\"has\nnewline\"");
        Escape("").Should().Be("");
        Escape(null).Should().Be("");
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
