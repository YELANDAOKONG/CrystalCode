namespace CrystalCode.Engine.Approvals;

/// <summary>
/// Risk and authority assigned to one tool call.
/// </summary>
public sealed record ToolClassification(
    Risk Risk,
    Authority Authority,
    string Summary,
    bool RequirePrompt = false)
{
    /// <summary>Only ordinary workspace calls can reuse or create grants.</summary>
    public bool AllowsReusableGrant =>
        !RequirePrompt
        && Authority == Authority.Workspace
        && (Risk == Risk.Read || Risk == Risk.Write);
}
