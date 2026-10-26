namespace CrystalCode.Server.Security;

/// <summary>
/// What a token may do. Each scope includes the ones before it.
/// </summary>
public enum ServerScope
{
    /// <summary>Read the snapshot, events, images, exports, children, and quick prompts.</summary>
    Observe = 0,

    /// <summary>Send input, answer interactions, and change modes, drafts, and verbose settings.</summary>
    Operate = 1,

    /// <summary>Shut down, manage tokens, and run commands that change trust, access, approval, workspace, or saved configuration.</summary>
    Admin = 2
}
