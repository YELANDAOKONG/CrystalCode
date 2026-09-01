namespace CrystalCode.Tools;

/// <summary>
/// Asks the operator a question from a tool call.
/// </summary>
public interface IUserPrompt
{
    ValueTask<QuestionResponse> AskAsync(
        IReadOnlyList<UserQuestion> questions,
        CancellationToken cancellationToken = default);
}
