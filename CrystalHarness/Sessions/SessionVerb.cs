namespace CrystalHarness.Sessions;

/// <summary>
/// Slash verbs recognized by the session prompt.
/// </summary>
public enum SessionVerb
{
    None,
    Help,
    Plan,
    Approval,
    Status,
    Clear,
    Cd,
    Quit,
    Unknown
}
