using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.Questions;
using CrystalCode.Engine.Tools.Questions.Interfaces;

namespace CrystalCode.Engine.Tests.Tools.Questions;

internal sealed class FixedUserPrompt : IUserPrompt
{
    private readonly QuestionResponse _response;

    public FixedUserPrompt(string answer)
    {
        _response = new QuestionResponse([[answer]], IsRejected: false);
    }

    public FixedUserPrompt(QuestionResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        _response = response;
    }

    public ValueTask<QuestionResponse> AskAsync(
        IReadOnlyList<UserQuestion> questions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastQuestions = questions;
        return ValueTask.FromResult(_response);
    }

    public IReadOnlyList<UserQuestion>? LastQuestions { get; private set; }
}
