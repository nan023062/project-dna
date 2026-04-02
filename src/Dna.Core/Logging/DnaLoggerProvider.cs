using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Dna.Core.Logging;

/// <summary>
/// Agentic OS 统一日志 Provider：Console（带颜色 + emoji）+ File（纯文本按天轮转）。
/// 通过 EventId.Name 区分业务领域（MCP / TOPO / MODULE …），自动匹配 emoji 和颜色。
/// stdio 模式下输出到 stderr 且不带颜色，避免污染 MCP JSON-RPC 通道。
/// </summary>
public sealed class DnaLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, Logger> _loggers = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileLogWriter _fileWriter;
    private readonly bool _useStdErr;

    public DnaLoggerProvider(FileLogWriter fileWriter, bool useStdErr = false)
    {
        _fileWriter = fileWriter;
        _useStdErr = useStdErr;
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, _ => new Logger(_fileWriter, _useStdErr));

    public void Dispose() => _loggers.Clear();

    // ────────────────────────────────────────────────────────────────

    private sealed class Logger(FileLogWriter fileWriter, bool useStdErr) : ILogger
    {
        private static readonly object ConsoleLock = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message)) return;

            if (exception != null)
                message = $"{message} — {exception.GetType().Name}: {exception.Message}";

            var (emoji, tag, color) = ResolveStyle(eventId, logLevel);
            var now = DateTime.Now;
            var time = now.ToString("HH:mm:ss.fff");

            if (useStdErr)
            {
                Console.Error.WriteLine($"  {time} {emoji} [{tag}] {message}");
            }
            else
            {
                lock (ConsoleLock)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"  {time} ");
                    Console.ForegroundColor = color;
                    Console.Write($"{emoji} [{tag}] ");
                    Console.ResetColor();
                    Console.WriteLine(message);
                }
            }

            fileWriter.Write($"{now:yyyy-MM-dd HH:mm:ss.fff} [{tag}] {message}");
        }

        private static (string Emoji, string Tag, ConsoleColor Color) ResolveStyle(EventId eventId, LogLevel level)
        {
            if (!string.IsNullOrEmpty(eventId.Name))
            {
                return eventId.Name.ToUpperInvariant() switch
                {
                    "MCP"       => ("🤖", "MCP",       ConsoleColor.Magenta),
                    "API"       => ("🌐", "API",       ConsoleColor.Cyan),
                    "EVOLVE"    => ("🧬", "EVOLVE",    ConsoleColor.Green),
                    "FEEDBACK"  => ("📣", "FEEDBACK",  ConsoleColor.Yellow),
                    "TOPO"      => ("🗺️", "TOPO",      ConsoleColor.Blue),
                    "MODULE"    => ("📦", "MODULE",    ConsoleColor.DarkCyan),
                    "STACK"     => ("📚", "STACK",     ConsoleColor.DarkYellow),
                    "DNA"       => ("🧪", "DNA",       ConsoleColor.DarkGreen),
                    "WORKSPACE" => ("🔧", "WORKSPACE", ConsoleColor.DarkMagenta),
                    _           => ("ℹ️", eventId.Name, ConsoleColor.Gray)
                };
            }

            return level switch
            {
                LogLevel.Warning              => ("⚠️", "WARN",  ConsoleColor.Yellow),
                LogLevel.Error or
                LogLevel.Critical             => ("❌", "ERROR", ConsoleColor.Red),
                LogLevel.Debug or
                LogLevel.Trace                => ("🔍", "DEBUG", ConsoleColor.DarkGray),
                _                             => ("ℹ️", "INFO",  ConsoleColor.Gray)
            };
        }
    }
}
