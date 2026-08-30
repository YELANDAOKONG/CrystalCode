using Crystal.Tools;

namespace CrystalHarness.Tools;

/// <summary>
/// Maps unexpected tool exceptions to model-visible output.
/// </summary>
public static class HarnessExceptionMapper
{
    public static ValueTask<ToolOutput?> MapAsync(
        ToolCall call,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(exception);
        cancellationToken.ThrowIfCancellationRequested();

        var text = exception switch
        {
            IOException or UnauthorizedAccessException =>
                $"Tool {call.Name} failed: {exception.Message}",
            _ =>
                $"Tool {call.Name} failed unexpectedly: {exception.Message}"
        };

        return ValueTask.FromResult<ToolOutput?>(
            new ToolOutput(text, ToolResultStatus.Failure));
    }
}
