namespace CrystalCode.Engine.Launch;

/// <summary>
/// Why a launch did not produce a session. A front end chooses the wording
/// for the kinds that depend on its surface.
/// </summary>
public enum SessionLaunchFailureKind
{
    /// <summary>
    /// An invalid option, configuration, credential, workspace, prompt set,
    /// prompt attachment, directory rule, mode, or saved session.
    /// </summary>
    Invalid,

    /// <summary>The operator declined trust or the session choice.</summary>
    Declined,

    /// <summary>The workspace is not trusted and the request did not allow asking.</summary>
    Untrusted,

    /// <summary>Trust must be asked and no trust prompt was supplied.</summary>
    TrustNeedsOperator,

    /// <summary>A session must be chosen and no chooser was supplied.</summary>
    ResumeNeedsOperator
}
