using Microsoft.Extensions.Logging;

namespace ZombieNightProtocol.Infrastructure;

public sealed class DailyFileLoggerProvider(ApplicationPaths paths) : ILoggerProvider
{
    private readonly object _sync = new();

    public ILogger CreateLogger(string categoryName) => new DailyFileLogger(categoryName, paths.Logs, _sync);
    public void Dispose() { }

    private sealed class DailyFileLogger(string category, string folder, object sync) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"{DateTimeOffset.Now:O} [{logLevel}] {category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }
            lock (sync)
            {
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, $"znp-{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine);
            }
        }
    }
}
