using Microsoft.Extensions.Logging;

namespace TfLens.Core.Tests.AppManager;

/// <summary>
/// An <see cref="ILogger{TCategoryName}"/> that keeps everything a component asked to write.
/// </summary>
/// <typeparam name="TCategory">The logging category, so this substitutes for the real logger directly.</typeparam>
/// <remarks>
/// A leak can hide in three places, so all three are captured: the formatted message, the structured
/// state (a template plus its named values, which is what a structured sink actually serialises) and
/// the exception's full text including every inner exception. Asserting only on the formatted message
/// would miss a value that reaches a JSON sink as a property but never appears in the rendered line.
/// </remarks>
public sealed class CapturingLogger<TCategory> : ILogger<TCategory>
{
    /// <summary>Every fragment of text this logger was handed, in order.</summary>
    public List<string> Lines { get; } = [];

    /// <summary>Everything captured, joined into one searchable document.</summary>
    public string Everything => string.Join('\n', Lines);

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState aState)
        where TState : notnull
    {
        Lines.Add(aState.ToString() ?? string.Empty);
        return null;
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel aLogLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel aLogLevel,
        EventId aEventId,
        TState aState,
        Exception? aException,
        Func<TState, Exception?, string> aFormatter)
    {
        Lines.Add(aFormatter(aState, aException));
        Lines.Add(aState?.ToString() ?? string.Empty);

        if (aState is IEnumerable<KeyValuePair<string, object?>> vValues)
        {
            Lines.AddRange(vValues.Select(aValue => $"{aValue.Key}={aValue.Value}"));
        }

        if (aException is not null)
        {
            Lines.Add(aException.ToString());
        }
    }
}
