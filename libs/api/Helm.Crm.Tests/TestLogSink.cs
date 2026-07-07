using Microsoft.Extensions.Logging;

namespace Helm.Crm.Tests;

/// <summary>Captures every log message across the running app into an in-memory, thread-safe list
/// so a test can assert a specific message was logged (e.g. the ADR-003 cross-company AUDIT
/// warning), without any production code changes to make logging test-observable. Registered
/// unconditionally by <see cref="HelmApiFactory"/> as an <see cref="ILoggerProvider"/> singleton.</summary>
public sealed class TestLogSink : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages.ToArray();

    public ILogger CreateLogger(string categoryName) => new SinkLogger(this);

    public void Dispose()
    {
    }

    private sealed class SinkLogger(TestLogSink sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink._messages.Enqueue(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
