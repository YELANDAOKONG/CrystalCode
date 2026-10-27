namespace CrystalCode.Engine.Launch;

/// <summary>
/// Everything a front end decided before a session exists.
/// </summary>
/// <param name="Options">Process-only session overrides.</param>
/// <param name="WorkingDirectory">
/// The process directory. It is the workspace when <paramref name="Workspace"/>
/// is null and resolves relative resume targets.
/// </param>
/// <param name="Workspace">An explicit workspace path, or null.</param>
/// <param name="Space">Use the operator space as the workspace.</param>
/// <param name="Resume">True when the operator asked to resume.</param>
/// <param name="ResumeTarget">A session id, a directory, <c>all</c>, or null to choose in this workspace.</param>
/// <param name="Trust">How an untrusted workspace is handled.</param>
/// <param name="PersistSelection">
/// True when a later preference save may also write the launch provider and
/// model. Every other launch override stays off disk either way.
/// </param>
/// <param name="AdditionalRoots">
/// When not null, the complete set of directories outside the workspace the
/// session may write, as an ACP client's additional roots. Saved and default
/// rules outside the workspace are dropped; read rules on or inside the
/// workspace are kept, because they only narrow access. Never written to
/// <c>access.json</c>.
/// </param>
public sealed record SessionLaunchRequest(
    SessionLaunchOptions Options,
    string WorkingDirectory,
    string? Workspace = null,
    bool Space = false,
    bool Resume = false,
    string? ResumeTarget = null,
    SessionTrustCheck Trust = SessionTrustCheck.Prompt,
    bool PersistSelection = true,
    IReadOnlyList<string>? AdditionalRoots = null);
