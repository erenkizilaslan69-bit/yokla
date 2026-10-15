using Microsoft.Extensions.Logging;

namespace BudgetApp.Tests;

/// <summary>Keeps every formatted log line, so tests can check what is (and is not) written to the log.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception) + (exception is null ? "" : " " + exception)));
}
