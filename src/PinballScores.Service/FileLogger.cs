using System.Text;
using Microsoft.Extensions.Logging;

namespace PinballScores.Service;

/// <summary>
/// Minimal rolling file logger.
///
/// The old build wrote a fresh timestamped .log file next to the executable on every
/// run, which both littered the install directory and needed write access to it.
/// This keeps one file per day under ProgramData and prunes old ones.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int RetainedDays = 14;

    private readonly string? _directory;
    private readonly Lock _gate = new();
    private DateOnly _currentDay;

    public FileLoggerProvider(string directory)
    {
        // Never let logging setup take the service down. If the directory cannot be
        // created — locked-down ProgramData, a bad override path — fall back to temp,
        // and if even that fails carry on with file logging disabled. The Event Log
        // still captures anything important on Windows.
        _directory = TryCreate(directory) ?? TryCreate(Path.Combine(Path.GetTempPath(), "PinballScores", "logs"));
    }

    private static string? TryCreate(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            return directory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Where logs are actually being written, or null if file logging is off.</summary>
    public string? LogDirectory => _directory;

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Write(string line)
    {
        if (_directory is null) return;

        lock (_gate)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today != _currentDay)
            {
                _currentDay = today;
                Prune();
            }

            try
            {
                File.AppendAllText(PathFor(today), line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Logging must never break a run.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private string PathFor(DateOnly day) =>
        Path.Combine(_directory!, $"pinballscores-{day:yyyy-MM-dd}.log");

    private void Prune()
    {
        if (_directory is null) return;

        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-RetainedDays);
            foreach (var file in Directory.EnumerateFiles(_directory, "pinballscores-*.log"))
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // Filtering is left to the logging factory so the standard "Logging" config
        // section applies. Set Logging:LogLevel:Default=Debug to see the write-back
        // slot plan when diagnosing a machine.
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var shortCategory = _category[(_category.LastIndexOf('.') + 1)..];
            var line = new StringBuilder()
                .Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss'Z'"))
                .Append(" [").Append(logLevel).Append("] ")
                .Append(shortCategory).Append(": ")
                .Append(formatter(state, exception));

            if (exception is not null) line.Append(Environment.NewLine).Append(exception);

            _provider.Write(line.ToString());
        }
    }
}
