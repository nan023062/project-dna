using System.Text;
using Avalonia.Interactivity;

namespace Dna.App.Desktop;

public partial class MainWindow
{
    private const int LogTailLineLimit = 200;
    private const int LogTailReadWindowBytes = 64 * 1024;

    private bool _isRefreshingLogTail;

    private async void RefreshLogTail_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshLogTailAsync();
    }

    private async Task RefreshLogTailAsync()
    {
        if (_isRefreshingLogTail)
            return;

        _isRefreshingLogTail = true;

        try
        {
            if (_project is null)
            {
                ResetLogTailState(
                    fileLabel: "No workspace selected.",
                    statusText: "Open a workspace to view runtime logs.",
                    content: string.Empty);
                return;
            }

            var logDirectory = ResolveLogDirectory();
            if (string.IsNullOrWhiteSpace(logDirectory) || !Directory.Exists(logDirectory))
            {
                ResetLogTailState(
                    fileLabel: "No log directory.",
                    statusText: $"Log directory not found: {logDirectory ?? "-"}",
                    content: string.Empty);
                return;
            }

            var latestLogFile = Directory
                .EnumerateFiles(logDirectory, "dna-*.log", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (latestLogFile is null)
            {
                ResetLogTailState(
                    fileLabel: Path.GetFileName(logDirectory),
                    statusText: "No runtime log file has been created yet.",
                    content: string.Empty);
                return;
            }

            var tail = await ReadLogTailAsync(latestLogFile.FullName, LogTailLineLimit, LogTailReadWindowBytes);

            LogFileText.Text = latestLogFile.Name;
            LogStatusText.Text = tail.LineCount == 0
                ? $"Log file loaded | updated {latestLogFile.LastWriteTime:HH:mm:ss} | empty"
                : $"Showing last {tail.LineCount} lines | updated {latestLogFile.LastWriteTime:HH:mm:ss}";
            LogTailBox.Text = tail.Content;
        }
        catch (Exception ex)
        {
            ResetLogTailState(
                fileLabel: "Log tail unavailable",
                statusText: $"Failed to read runtime log: {ex.Message}",
                content: string.Empty);
        }
        finally
        {
            _isRefreshingLogTail = false;
        }
    }

    private string? ResolveLogDirectory()
    {
        if (_project is not null && !string.IsNullOrWhiteSpace(_project.LogDirectoryPath))
            return _project.LogDirectoryPath;

        return AppDesktopLog.CurrentLogDirectory;
    }

    private void ResetLogTailState(string fileLabel, string statusText, string content)
    {
        LogFileText.Text = fileLabel;
        LogStatusText.Text = statusText;
        LogTailBox.Text = content;
    }

    private static async Task<LogTailSnapshot> ReadLogTailAsync(
        string filePath,
        int maxLines,
        int maxBytes)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        if (stream.Length <= 0)
            return LogTailSnapshot.Empty;

        var bytesToRead = (int)Math.Min(stream.Length, maxBytes);
        if (stream.Length > bytesToRead)
            stream.Seek(-bytesToRead, SeekOrigin.End);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync();
        if (string.IsNullOrEmpty(content))
            return LogTailSnapshot.Empty;

        if (stream.Position > bytesToRead)
        {
            var firstLineBreak = content.IndexOf('\n');
            if (firstLineBreak >= 0 && firstLineBreak + 1 < content.Length)
                content = content[(firstLineBreak + 1)..];
        }

        var normalizedLines = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(maxLines)
            .ToList();

        return normalizedLines.Count == 0
            ? LogTailSnapshot.Empty
            : new LogTailSnapshot(string.Join(Environment.NewLine, normalizedLines), normalizedLines.Count);
    }

    private sealed record LogTailSnapshot(string Content, int LineCount)
    {
        public static LogTailSnapshot Empty { get; } = new(string.Empty, 0);
    }
}
