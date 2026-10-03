using CrystalCode.Engine.Tools;

namespace CrystalCode.Run;

/// <summary>
/// Dismisses the built-in question tool. There is no operator to answer it.
/// </summary>
internal sealed class UnattendedUserPrompt : IUserPrompt
{
    public int Dismissed { get; private set; }

    public ValueTask<QuestionResponse> AskAsync(
        IReadOnlyList<UserQuestion> questions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(questions);
        cancellationToken.ThrowIfCancellationRequested();
        Dismissed++;
        return ValueTask.FromResult(new QuestionResponse([], true));
    }
}
