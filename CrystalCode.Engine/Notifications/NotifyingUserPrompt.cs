using System.Globalization;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Notifications;

/// <summary>
/// Sends the question notification while the operator is asked, for the
/// question tool and MCP elicitation alike.
/// </summary>
internal sealed class NotifyingUserPrompt(IUserPrompt inner, OperatorNotifier notifier) : IUserPrompt
{
    public async ValueTask<QuestionResponse> AskAsync(
        IReadOnlyList<UserQuestion> questions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(questions);
        var first = questions.Count > 0 ? questions[0] : null;
        var header = first?.Header ?? string.Empty;
        var question = first?.Text ?? string.Empty;
        var lead = string.IsNullOrWhiteSpace(question) ? header : question;
        using var pending = notifier.Begin(new OperatorNotice(
            OperatorNotifyEvent.Question,
            string.IsNullOrWhiteSpace(lead) ? "Question waiting." : "Question: " + lead,
            [
                new("HEADER", header),
                new("QUESTION", question),
                new("COUNT", questions.Count.ToString(CultureInfo.InvariantCulture))
            ]));
        return await inner.AskAsync(questions, cancellationToken);
    }
}
