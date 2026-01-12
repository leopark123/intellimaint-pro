using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IntelliMaint.Core.Abstractions;
using IntelliMaint.Core.Contracts;

namespace IntelliMaint.Infrastructure.Pipeline;

public sealed class DbWriterLoop : BackgroundService
{
    private readonly ChannelReader<TelemetryPoint> _reader;
    private readonly ITelemetryRepository _repository;
    private readonly IOverflowExporter? _overflowExporter;
    private readonly ILogger<DbWriterLoop> _logger;
    private readonly int _batchSize;
    private readonly int _flushMs;
    private readonly int _maxRetries;
    private readonly int _retryDelayMs;

    private long _totalWritten;
    private long _totalBatches;
    private long _totalRetries;
    private long _totalFailedToOverflow;
    private double _lastWriteMs;
    private readonly Stopwatch _latencyStopwatch = new();
    private readonly List<double> _latencySamples = new(100);

    public DbWriterLoop(
        ChannelReader<TelemetryPoint> reader,
        ITelemetryRepository repository,
        IOptions<EdgeOptions> options,
        ILogger<DbWriterLoop> logger,
        IOverflowExporter? overflowExporter = null)
    {
        _reader = reader;
        _repository = repository;
        _overflowExporter = overflowExporter;
        _logger = logger;
        _batchSize = options.Value.WriterBatchSize;
        _flushMs = options.Value.WriterFlushMs;
        _maxRetries = options.Value.WriterMaxRetries;
        _retryDelayMs = options.Value.WriterRetryDelayMs;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DbWriterLoop started. BatchSize={BatchSize}, FlushMs={FlushMs}, MaxRetries={MaxRetries}", _batchSize, _flushMs, _maxRetries);
        var batch = new List<TelemetryPoint>(_batchSize);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                batch.Clear();
                var deadline = DateTime.UtcNow.AddMilliseconds(_flushMs);
                while (batch.Count < _batchSize && DateTime.UtcNow < deadline)
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;
                    cts.CancelAfter(remaining);
                    try { if (await _reader.WaitToReadAsync(cts.Token)) { while (batch.Count < _batchSize && _reader.TryRead(out var point)) { batch.Add(point); } } }
                    catch (OperationCanceledException) { break; }
                }
                if (batch.Count > 0)
                {
                    var writeToken = stoppingToken.IsCancellationRequested ? CancellationToken.None : stoppingToken;
                    await WriteBatchWithRetryAsync(batch, writeToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("DbWriterLoop stopping...");
            if (batch.Count > 0) { _logger.LogDebug("Writing final batch of {Count} points", batch.Count); await WriteBatchWithRetryAsync(batch, CancellationToken.None); }
        }
        catch (Exception ex) { _logger.LogError(ex, "DbWriterLoop error"); throw; }
        finally
        {
            await DrainAsync();
            _logger.LogInformation("DbWriterLoop stopped. Written={Total}, Batches={Batches}, Retries={Retries}, Failed={Failed}", _totalWritten, _totalBatches, _totalRetries, _totalFailedToOverflow);
        }
    }

    private async Task WriteBatchWithRetryAsync(List<TelemetryPoint> batch, CancellationToken ct)
    {
        var attempt = 0;
        var delay = _retryDelayMs;
        while (attempt <= _maxRetries)
        {
            _latencyStopwatch.Restart();
            try
            {
                var written = await _repository.AppendBatchAsync(batch, ct);
                _latencyStopwatch.Stop();
                _lastWriteMs = _latencyStopwatch.Elapsed.TotalMilliseconds;
                Interlocked.Add(ref _totalWritten, written);
                Interlocked.Increment(ref _totalBatches);
                lock (_latencySamples) { if (_latencySamples.Count >= 100) _latencySamples.RemoveAt(0); _latencySamples.Add(_lastWriteMs); }
                if (attempt > 0) _logger.LogInformation("Batch written after {Attempts} retries: {Count} points in {Ms:F2}ms", attempt, written, _lastWriteMs);
                else _logger.LogDebug("Wrote {Count} points in {Ms:F2}ms", written, _lastWriteMs);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { _logger.LogWarning("Write batch cancelled, {Count} points not written", batch.Count); return; }
            catch (Exception ex)
            {
                attempt++;
                if (attempt <= _maxRetries)
                {
                    Interlocked.Increment(ref _totalRetries);
                    _logger.LogWarning(ex, "Write attempt {Attempt}/{MaxRetries} failed for {Count} points, retrying in {Delay}ms", attempt, _maxRetries, batch.Count, delay);
                    try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { await ExportToOverflowAsync(batch, CancellationToken.None); return; }
                    delay = Math.Min(delay * 2, 30000);
                }
                else { _logger.LogError(ex, "All {MaxRetries} retries failed for {Count} points", _maxRetries, batch.Count); await ExportToOverflowAsync(batch, CancellationToken.None); }
            }
        }
    }

    private async Task ExportToOverflowAsync(List<TelemetryPoint> batch, CancellationToken ct)
    {
        if (_overflowExporter == null) { _logger.LogError("No overflow exporter, {Count} points lost!", batch.Count); return; }
        try { await _overflowExporter.ExportBatchAsync(batch, ct); Interlocked.Add(ref _totalFailedToOverflow, batch.Count); _logger.LogWarning("Exported {Count} failed points to overflow", batch.Count); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to export {Count} points to overflow", batch.Count); }
    }

    private async Task DrainAsync()
    {
        var batch = new List<TelemetryPoint>(_batchSize);
        while (_reader.TryRead(out var point)) { batch.Add(point); if (batch.Count >= _batchSize) { await WriteBatchWithRetryAsync(batch, CancellationToken.None); batch.Clear(); } }
        if (batch.Count > 0) await WriteBatchWithRetryAsync(batch, CancellationToken.None);
        _logger.LogInformation("Drained remaining data");
    }

    public double GetLatencyP95() { lock (_latencySamples) { if (_latencySamples.Count == 0) return 0; var sorted = _latencySamples.OrderBy(x => x).ToList(); var index = (int)(sorted.Count * 0.95); return sorted[Math.Min(index, sorted.Count - 1)]; } }

    public (long TotalWritten, long TotalBatches, long TotalRetries, long TotalFailedToOverflow, double LastWriteMs, double P95Ms) GetStats()
    {
        return (Interlocked.Read(ref _totalWritten), Interlocked.Read(ref _totalBatches), Interlocked.Read(ref _totalRetries), Interlocked.Read(ref _totalFailedToOverflow), _lastWriteMs, GetLatencyP95());
    }
}
