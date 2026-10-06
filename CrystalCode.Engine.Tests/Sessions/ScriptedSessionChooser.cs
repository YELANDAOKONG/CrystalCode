using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Tests.Sessions;

internal sealed class ScriptedSessionChooser : ISessionChooser
{
    private readonly string? _choice;

    public ScriptedSessionChooser(string? choice)
    {
        _choice = choice;
    }

    public int Calls { get; private set; }

    public bool ListWorkspace { get; private set; }

    public IReadOnlyList<SessionSummary> LastSessions { get; private set; } = [];

    public Task<string?> ChooseAsync(
        IReadOnlyList<SessionSummary> sessions,
        string? currentId,
        bool listWorkspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        ListWorkspace = listWorkspace;
        LastSessions = sessions;
        return Task.FromResult(_choice);
    }
}
