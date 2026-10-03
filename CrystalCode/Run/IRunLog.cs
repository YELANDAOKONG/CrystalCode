using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Run;

/// <summary>
/// Projects one unattended turn onto stdout and records why it stopped.
/// </summary>
internal interface IRunLog : ISessionObserver
{
    TurnStopReason? StopReason { get; }

    void BindSession(string sessionId);

    void WriteEpilogue(string? status, string hint);
}
