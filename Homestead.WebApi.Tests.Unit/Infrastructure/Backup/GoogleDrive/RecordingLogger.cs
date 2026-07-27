using Microsoft.Extensions.Logging;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Backup.GoogleDrive;

/// <summary>
/// Captures formatted log messages so a test can assert on what was — and was not — written.
/// </summary>
/// <remarks>
/// Exists for one rule the Google plumbing has to keep: token-endpoint responses and device codes are
/// credentials, and nothing here may ever log a body. Asserting the negative needs the text.
/// </remarks>
public sealed class RecordingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Messages.Add(formatter(state, exception));

        if (exception is not null)
            Messages.Add(exception.ToString());
    }
}
