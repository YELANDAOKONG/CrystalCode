using CrystalCode.Engine.Approvals.Interfaces;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Events.Interfaces;
using CrystalCode.Engine.Mcp;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Sessions.Interfaces;
using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.Interfaces;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Everything a front end supplies to a session: where events go, and who
/// answers approval requests, operator questions, and session choices, plus
/// MCP servers the front end passes in for this session only.
/// </summary>
public sealed record SessionFrontEnd(
    ISessionObserver Observer,
    IApprovalPrompt Approvals,
    IUserPrompt Questions,
    ISessionChooser Sessions,
    IWorkspaceTrustPrompt Trust)
{
    /// <summary>Receives the current syntax service after plugin startup or replacement.</summary>
    public Action<SyntaxService>? SyntaxReady { get; init; }

    public Func<IReadOnlyList<CrystalCode.Engine.Home.ChildSessionDocument>, CancellationToken, Task<string?>>?
        ChooseChildAsync { get; init; }

    /// <summary>
    /// Lets the operator pick a theme from the given names when <c>/theme</c> has no argument. The
    /// current name is passed so the picker can start on it. A null result means the operator
    /// cancelled. When this is null the command lists the names as a note.
    /// </summary>
    public Func<IReadOnlyList<string>, string, CancellationToken, Task<string?>>?
        ChooseThemeAsync { get; init; }

    /// <summary>
    /// Servers with <see cref="McpServerSource.Session"/>, connected after
    /// Home and Project and replacing a server of the same name. They are not
    /// saved with the session, and their values appear in no event or text.
    /// </summary>
    public IReadOnlyList<McpServerDefinition> McpServers { get; init; } = [];
}
