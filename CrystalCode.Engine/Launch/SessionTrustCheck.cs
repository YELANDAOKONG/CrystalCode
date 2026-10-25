namespace CrystalCode.Engine.Launch;

/// <summary>
/// How a launch treats an untrusted workspace.
/// </summary>
public enum SessionTrustCheck
{
    /// <summary>
    /// Follow the <c>workspaceTrust</c> preference and ask the operator through
    /// the supplied trust prompt. A yes is remembered.
    /// </summary>
    Prompt,

    /// <summary>Refuse an untrusted workspace without asking.</summary>
    Require,

    /// <summary>Do not check trust and do not record the directory.</summary>
    Skip
}
