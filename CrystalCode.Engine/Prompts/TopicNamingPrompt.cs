namespace CrystalCode.Engine.Prompts;

/// <summary>
/// Topic naming: the system text (operator-replaceable as <c>topic.md</c>)
/// and the fixed user turn that carries the conversation.
/// </summary>
public static class TopicNamingPrompt
{
    public const string Text =
        """
        You generate a concise topic name for a coding session.

        Based on the supplied conversation, return only one descriptive title.
        Do not include explanations, quotation marks, Markdown, bullets, or a trailing period.

        Rules:
        - Prefer 3 to 8 English words and no more than 80 characters.
        - Describe the user's main goal, not implementation details.
        - Use stable, searchable wording.
        - Do not include secrets, credentials, full file contents, or personal data.
        - Do not invent a goal that is not present in the conversation.
        - If the intent is unclear, return exactly the two words New conversation
        """;

    /// <summary>Characters of conversation sent; the rest is cut so naming stays cheap.</summary>
    public const int ConversationMaxChars = 8_000;

    public static string UserText(string conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        var text = conversation.Length <= ConversationMaxChars
            ? conversation
            : conversation[..ConversationMaxChars] + "\n[Conversation cut]";
        return "Name this coding session.\n\n<conversation>\n" + text + "\n</conversation>";
    }
}
