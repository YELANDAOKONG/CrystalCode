using Crystal;
using Crystal.Chat;
using Crystal.Tools;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Stops a model round whose finish reason is not a normal stop or a tool request.
/// Tool calls on those rounds are left out of the transcript so they are not executed.
/// </summary>
internal static class CandidateRound
{
    public static bool TryStop(
        ChatCandidate candidate,
        List<ChatItem> transcript,
        out TurnStopReason stopReason,
        out string? fault)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(transcript);
        fault = null;
        var reason = candidate.FinishReason;
        if (reason == FinishReason.Stop || reason == FinishReason.ToolCalls)
        {
            stopReason = TurnStopReason.Completed;
            return false;
        }

        AppendWithoutToolCalls(transcript, candidate.Items);

        if (reason == FinishReason.Length)
        {
            stopReason = TurnStopReason.OutputTruncated;
            return true;
        }

        if (reason == FinishReason.ContentFilter)
        {
            stopReason = TurnStopReason.ContentFiltered;
            return true;
        }

        stopReason = TurnStopReason.Failed;
        fault = $"Model stopped with finish reason '{reason.Value}'.";
        return true;
    }

    /// <summary>
    /// Keeps text and reasoning from a model round and leaves its tool calls out.
    /// </summary>
    internal static void AppendWithoutToolCalls(
        List<ChatItem> transcript,
        IReadOnlyList<ChatItem> items)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(items);
        foreach (var item in items)
        {
            if (item is not ToolCall)
            {
                transcript.Add(item);
            }
        }
    }
}
