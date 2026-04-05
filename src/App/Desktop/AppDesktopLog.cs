using Dna.Core.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dna.App.Desktop;

internal static class AppDesktopLog
{
    private static readonly object Gate = new();

    private static FileLogWriter? _writer;
    private static ILoggerFactory? _loggerFactory;
    private static string? _metadataRootPath;

    public static string? CurrentLogDirectory
    {
        get
        {
            lock (Gate)
                return _metadataRootPath is null ? null : Path.Combine(_metadataRootPath, "logs");
        }
    }

    public static void ConfigureProject(DesktopProjectConfig project)
        => ConfigureMetadataRoot(project.MetadataRootPath);

    public static void ConfigureMetadataRoot(string metadataRootPath)
    {
        var normalized = Path.GetFullPath(metadataRootPath);

        lock (Gate)
        {
            if (string.Equals(_metadataRootPath, normalized, StringComparison.OrdinalIgnoreCase) &&
                _writer is not null &&
                _loggerFactory is not null)
            {
                return;
            }

            _loggerFactory?.Dispose();
            _writer?.Dispose();

            Directory.CreateDirectory(normalized);

            _writer = new FileLogWriter();
            _writer.SetLogDirectory(normalized);

            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.ClearProviders();
                builder.AddProvider(new DnaLoggerProvider(_writer, useStdErr: false));
                builder.SetMinimumLevel(LogLevel.Information);
                builder.AddFilter("Microsoft", LogLevel.Warning);
                builder.AddFilter("System", LogLevel.Warning);
            });

            _metadataRootPath = normalized;
        }
    }

    public static void ConfigureAspNetLogging(ILoggingBuilder logging)
    {
        lock (Gate)
        {
            if (_writer is null)
                return;

            logging.ClearProviders();
            logging.AddProvider(new DnaLoggerProvider(_writer, useStdErr: false));
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddFilter("Microsoft", LogLevel.Warning);
            logging.AddFilter("System", LogLevel.Warning);
        }
    }

    public static ILogger CreateLogger<T>()
    {
        lock (Gate)
            return (_loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<T>();
    }

    public static void Shutdown()
    {
        lock (Gate)
        {
            _loggerFactory?.Dispose();
            _writer?.Dispose();
            _loggerFactory = null;
            _writer = null;
            _metadataRootPath = null;
        }
    }
}
