using System.Text;

using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Events;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Builds one tool-free side question from the committed transcript.
/// </summary>
internal static class SideQuestion
{
    public const int MemoryLimit = 20;

    public const string Instruction =
        "Answer this side question from the conversation above. Do not call tools.";

    public const string Cancelled = "Side question cancelled.";

    public const string NoAnswer = "The side question returned no answer.";

    public const string CannotUseTools = "Side question cannot use tools.";

    public const string NoneToShow = "No side question to show.";

    public static List<ChatItem> Compose(
        IReadOnlyList<ChatItem> transcript,
        IReadOnlyList<SideExchange> prior,
        string question)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(prior);
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var items = new List<ChatItem>(transcript.Count + (prior.Count * 2) + 1);
        items.AddRange(transcript);
        foreach (var exchange in prior)
        {
            items.Add(new ChatMessage(ChatRole.User, exchange.Question));
            items.Add(new ChatMessage(ChatRole.Assistant, exchange.Answer));
        }

        items.Add(new ChatMessage(ChatRole.User, Instruction + "\n\n" + question));
        return items;
    }

    public static bool TryReadAnswer(
        ChatResponse response,
        out string answer,
        out bool ignoredToolCall)
    {
        ArgumentNullException.ThrowIfNull(response);
        answer = string.Empty;
        ignoredToolCall = false;
        if (response.Candidates.Count == 0)
        {
            return false;
        }

        var text = new StringBuilder();
        foreach (var item in response.Candidates[0].Items)
        {
            if (item is ChatMessage message && message.Role == ChatRole.Assistant)
            {
                text.Append(message.Text);
            }
            else if (item is ToolCall)
            {
                ignoredToolCall = true;
            }
        }

        answer = text.ToString().Trim();
        return answer.Length > 0;
    }
}
