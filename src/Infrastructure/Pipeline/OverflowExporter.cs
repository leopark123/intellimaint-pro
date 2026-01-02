using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Infrastructure.Pipeline;

/// <summary>
/// 溢出导出器实现
/// 将被丢弃的数据点导出到 CSV 文件
/// </summary>
public sealed class OverflowExporter : BackgroundService, IOverflowExporter
{
    private readonly OverflowOptions _options;
    private readonly ILogger<OverflowExporter> _logger;
    
    private readonly object _lock = new();
    private StreamWriter? _currentWriter;
    private string? _currentFilePath;
    private long _currentFileSize;
    private int _fileCount;
    private long _totalExported;
    
    public OverflowExporter(
        IOptions<EdgeOptions> options,
        ILogger<OverflowExporter> logger)
    {
        _options = options.Value.Overflow;
        _logger = logger;
        
        // 确保目录存在
        if (!Directory.Exists(_options.Directory))
        {
            Directory.CreateDirectory(_options.Directory);
        }
    }
    
    /// <summary>
    /// 导出单个数据点
    /// </summary>
    public Task ExportAsync(TelemetryPoint point, CancellationToken ct)
    {
        if (!_options.Enabled) return Task.CompletedTask;
        
        var line = FormatCsvLine(point);
        
        lock (_lock)
        {
            EnsureWriter();
            _currentWriter!.WriteLine(line);
            _currentFileSize += Encoding.UTF8.GetByteCount(line) + 2;
            _totalExported++;
            
            // 检查是否需要滚动文件
            if (_currentFileSize >= _options.RollSizeMB * 1024 * 1024)
            {
                RollFile();
            }
        }
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// 批量导出
    /// </summary>
    public Task ExportBatchAsync(IReadOnlyList<TelemetryPoint> points, CancellationToken ct)
    {
        if (!_options.Enabled || points.Count == 0) return Task.CompletedTask;
        
        lock (_lock)
        {
            EnsureWriter();
            
            foreach (var point in points)
            {
                var line = FormatCsvLine(point);
                _currentWriter!.WriteLine(line);
                _currentFileSize += Encoding.UTF8.GetByteCount(line) + 2;
                _totalExported++;
            }
            
            // 检查是否需要滚动文件
            if (_currentFileSize >= _options.RollSizeMB * 1024 * 1024)
            {
                RollFile();
            }
        }
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// 获取统计
    /// </summary>
    public OverflowStats GetStats()
    {
        lock (_lock)
        {
            return new OverflowStats
            {
                TotalExported = _totalExported,
                CurrentFileSize = _currentFileSize,
                FileCount = _fileCount,
                CurrentFilePath = _currentFilePath
            };
        }
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 定期清理旧文件
        var cleanupInterval = TimeSpan.FromHours(1);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(cleanupInterval, stoppingToken);
                CleanupOldFiles();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Overflow cleanup error");
            }
        }
        
        // 关闭当前文件
        lock (_lock)
        {
            CloseCurrentFile();
        }
    }
    
    /// <summary>
    /// 确保有可用的 Writer
    /// </summary>
    private void EnsureWriter()
    {
        if (_currentWriter != null) return;
        
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"overflow_{timestamp}.csv";
        _currentFilePath = Path.Combine(_options.Directory, fileName);
        
        _currentWriter = new StreamWriter(_currentFilePath, append: false, Encoding.UTF8);
        _currentWriter.AutoFlush = true;
        
        // 写入 CSV 头
        _currentWriter.WriteLine("DeviceId,TagId,Ts,Seq,ValueType,Value,Quality,Source,Protocol");
        _currentFileSize = 0;
        _fileCount++;
        
        _logger.LogInformation("Created overflow file: {Path}", _currentFilePath);
    }
    
    /// <summary>
    /// 滚动文件
    /// </summary>
    private void RollFile()
    {
        CloseCurrentFile();
        
        // 压缩旧文件（如果启用）
        if (_options.Compress && !string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath))
        {
            Task.Run(() => CompressFile(_currentFilePath));
        }
        
        _currentWriter = null;
        _currentFilePath = null;
        _currentFileSize = 0;
    }
    
    /// <summary>
    /// 关闭当前文件
    /// </summary>
    private void CloseCurrentFile()
    {
        if (_currentWriter != null)
        {
            _currentWriter.Flush();
            _currentWriter.Dispose();
            _currentWriter = null;
            
            _logger.LogInformation("Closed overflow file: {Path}", _currentFilePath);
        }
    }
    
    /// <summary>
    /// 压缩文件
    /// </summary>
    private void CompressFile(string filePath)
    {
        try
        {
            var gzPath = filePath + ".gz";
            
            using (var sourceStream = File.OpenRead(filePath))
            using (var targetStream = File.Create(gzPath))
            using (var gzipStream = new GZipStream(targetStream, CompressionLevel.Optimal))
            {
                sourceStream.CopyTo(gzipStream);
            }
            
            // 删除原文件
            File.Delete(filePath);
            
            _logger.LogInformation("Compressed overflow file: {Path}", gzPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compress file: {Path}", filePath);
        }
    }
    
    /// <summary>
    /// 清理旧文件
    /// </summary>
    private void CleanupOldFiles()
    {
        var cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);
        var files = Directory.GetFiles(_options.Directory, "overflow_*.csv*");
        
        foreach (var file in files)
        {
            var fileInfo = new FileInfo(file);
            if (fileInfo.CreationTimeUtc < cutoff)
            {
                try
                {
                    File.Delete(file);
                    _logger.LogInformation("Deleted old overflow file: {Path}", file);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to delete old file: {Path}", file);
                }
            }
        }
    }
    
    /// <summary>
    /// 格式化 CSV 行
    /// </summary>
    private static string FormatCsvLine(TelemetryPoint point)
    {
        var value = point.GetValue()?.ToString() ?? "";
        
        // 转义 CSV 特殊字符
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            value = "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        
        return $"{point.DeviceId},{point.TagId},{point.Ts},{point.Seq},{point.ValueType},{value},{point.Quality},{point.Source},{point.Protocol}";
    }
}
