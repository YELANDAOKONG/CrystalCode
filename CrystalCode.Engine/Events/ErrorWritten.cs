using Crystal.Chat;

namespace CrystalCode.Engine.Events;

/// <summary>
/// Plain error text for the transcript.
/// </summary>
public sealed record ErrorWritten(string Text) : SessionEvent
{
    private const string ArchivePrefix = "## Turn failed\n";

    /// <summary>Whether this host-authored error belongs to the conversation.</summary>
    public bool TurnFailure { get; init; }

    internal ChatMessage ToArchiveMessage() => new(ChatRole.System, ArchivePrefix + Text);

    public static string? ArchivedTurnFailure(ChatItem item) =>
        item is ChatMessage message
        && message.Role == ChatRole.System
        && message.Text.StartsWith(ArchivePrefix, StringComparison.Ordinal)
            ? message.Text[ArchivePrefix.Length..]
            : null;
}
