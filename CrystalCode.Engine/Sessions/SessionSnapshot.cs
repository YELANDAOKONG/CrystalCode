using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Everything a client needs to draw a session without replaying history.
/// Produced by <see cref="SessionState"/>.
/// </summary>
/// <param name="Conversation">Committed conversation items, oldest first.</param>
/// <param name="Live">Text, reasoning, and tool calls of the model round still streaming.</param>
/// <param name="Children">Child sessions with live usage and todos folded in.</param>
/// <param name="Approvals">
/// Calls that passed approval in this conversation, in order, until it is
/// cleared or replaced. A child's passes stay in its own events.
/// </param>
public sealed record SessionSnapshot(
    SessionChrome? Chrome,
    SessionPreferences? Preferences,
    IReadOnlyList<SlashCompletion> Commands,
    IReadOnlyList<string> PromptHistory,
    IReadOnlyList<ChatItem> Conversation,
    LiveRound? Live,
    bool TurnActive,
    SessionActivity Activity,
    IReadOnlyList<string> Queue,
    IReadOnlyList<TodoItem> Todos,
    IReadOnlyList<StatusFieldValue> StatusFields,
    TokenUsage? Usage,
    TokenUsage? CumulativeUsage,
    int ContextWindow,
    int? EstimatedContextTokens,
    SideQuestionSnapshot Side,
    IReadOnlyList<ChildSessionDocument> Children,
    TurnFinished? LastTurn,
    IReadOnlyList<ApprovalPassed> Approvals);
