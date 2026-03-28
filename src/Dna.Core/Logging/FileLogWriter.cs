using System.Collections.Concurrent;
using System.Globalization;

namespace Dna.Core.Logging;

/// <summary>
/// 文件日志写入器：按天轮转 + 超大文件编号 + 保留 N 天 + 异步写入。
/// 日志目录默认为项目根目录下的 .dna/logs/。
/// 清理策略：每次轮转时扫描并删除超过 RetentionDays 天的旧文件。
/// </summary>
public sealed class FileLogWriter : IDisposable
{
    private const string FilePrefix = "dna-";
    private const string FileExtension = ".log";
    private const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB

    private readonly int _retentionDays;
    private readonly BlockingCollection<string> _queue = new(boundedCapacity: 4096);
    private readonly Thread _writerThread;
    private readonly object _rotateLock = new();

    private string _logDirectory = "";
    private string _currentDate = "";
    private int _currentSequence;
    private StreamWriter? _writer;
    private long _currentFileSize;
    private bool _disposed;

    public FileLogWriter(int retentionDays = 30)
    {
        _retentionDays = retentionDays;
        _writerThread = new Thread(ProcessQueue)
        {
            IsBackground = true,
            Name = "Dna-LogWriter"
        };
        _writerThread.Start();
    }

    /// <summary>
    /// 设置日志目录。可在项目根目录确定后调用。
    /// </summary>
    public void SetLogDirectory(string storePath)
    {
        var dir = Path.Combine(storePath, "logs");
        if (string.Equals(_logDirectory, dir, StringComparison.OrdinalIgnoreCase))
            return;

        lock (_rotateLock)
        {
            _logDirectory = dir;
            Directory.CreateDirectory(dir);
            CloseCurrentWriter();
            EnsureWriter();
            CleanOldFiles();
        }
    }

    public void Write(string line)
    {
        if (_disposed || string.IsNullOrEmpty(_logDirectory)) return;
        _queue.TryAdd(line, millisecondsTimeout: 50);
    }

    private void ProcessQueue()
    {
        try
        {
            foreach (var line in _queue.GetConsumingEnumerable())
            {
                lock (_rotateLock)
                {
                    if (string.IsNullOrEmpty(_logDirectory)) continue;
                    EnsureWriter();
                    if (_writer == null) continue;

                    _writer.WriteLine(line);
                    _writer.Flush();
                    _currentFileSize += System.Text.Encoding.UTF8.GetByteCount(line) + 2;

                    if (_currentFileSize >= MaxFileSizeBytes)
                        RotateBySize();
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void EnsureWriter()
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (_writer != null && _currentDate == today) return;

        CloseCurrentWriter();
        _currentDate = today;
        _currentSequence = 0;
        OpenWriter();

        if (_currentDate != today)
            CleanOldFiles();
    }

    private void RotateBySize()
    {
        CloseCurrentWriter();
        _currentSequence++;
        OpenWriter();
    }

    private void OpenWriter()
    {
        try
        {
            var fileName = _currentSequence == 0
                ? $"{FilePrefix}{_currentDate}{FileExtension}"
                : $"{FilePrefix}{_currentDate}.{_currentSequence:D3}{FileExtension}";

            var filePath = Path.Combine(_logDirectory, fileName);
            var fileInfo = new FileInfo(filePath);
            _currentFileSize = fileInfo.Exists ? fileInfo.Length : 0;
            _writer = new StreamWriter(filePath, append: true, System.Text.Encoding.UTF8)
            {
                AutoFlush = false
            };
        }
        catch
        {
            _writer = null;
        }
    }

    private void CloseCurrentWriter()
    {
        _writer?.Flush();
        _writer?.Dispose();
        _writer = null;
    }

    private void CleanOldFiles()
    {
        try
        {
            var cutoff = DateTime.Now.Date.AddDays(-_retentionDays);
            foreach (var file in Directory.EnumerateFiles(_logDirectory, $"{FilePrefix}*{FileExtension}"))
            {
                var name = Path.GetFileName(file);
                var datePart = ExtractDate(name);
                if (datePart.HasValue && datePart.Value < cutoff)
                {
                    try { File.Delete(file); }
                    catch { /* best effort */ }
                }
            }
        }
        catch { /* best effort */ }
    }

    private static DateTime? ExtractDate(string fileName)
    {
        var span = fileName.AsSpan();
        if (!span.StartsWith(FilePrefix.AsSpan())) return null;
        span = span[FilePrefix.Length..];
        if (span.Length < 10) return null;
        var dateStr = span[..10].ToString();
        return DateTime.TryParseExact(dateStr, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt
            : null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.CompleteAdding();
        _writerThread.Join(timeout: TimeSpan.FromSeconds(3));
        lock (_rotateLock)
        {
            CloseCurrentWriter();
        }
        _queue.Dispose();
    }
}
