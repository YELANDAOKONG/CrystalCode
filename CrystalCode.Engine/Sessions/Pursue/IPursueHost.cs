using Crystal.Chat;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Sessions.Pursue;

/// <summary>What a pursue run needs from the session that hosts it.</summary>
internal interface IPursueHost
{
    /// <summary>True when the operator queued input while the run was working.</summary>
    bool HasQueuedInput { get; }

    /// <summary>Runs the working model until it stops, under <paramref name="limits"/>.</summary>
    Task<TurnResult> RunSegmentAsync(
        IReadOnlyList<ChatItem> input,
        TurnLimits limits,
        CancellationToken cancellationToken);

    /// <summary>Asks the checker whether the objective is met.</summary>
    Task<PursueCheck> CheckAsync(
        IReadOnlyList<ChatItem> transcript,
        PursueState state,
        CancellationToken cancellationToken);

    /// <summary>Takes one model call from the run's shared budget for a check.</summary>
    bool TryReserveCheck();

    /// <summary>Builds the message that sends the working model on.</summary>
    ChatMessage BuildContinuation(PursueState state, int remainingContinuations, bool resumed);

    /// <summary>Records a continuation message in the saved conversation.</summary>
    void CommitContinuation(ChatMessage message);

    BackgroundTaskSnapshot? FindTask(string taskId);

    /// <summary>Returns true when queued input ends the wait.</summary>
    Task<bool> WaitForTaskOrInputAsync(IReadOnlyList<string> taskIds, CancellationToken cancellationToken);

    void Publish(PursueState state);

    void Publish(PursueChecked checkedEvent);

    void Note(string text);

    void Error(string text);
}
