namespace CrystalHarness.Tools;

/// <summary>
/// Asks the operator a question from a tool call.
/// </summary>
public interface IUserPrompt
{
    ValueTask<string> AskAsync(
        string question,
        IReadOnlyList<string>? options,
        CancellationToken cancellationToken = default);
}
