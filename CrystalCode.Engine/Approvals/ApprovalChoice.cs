namespace CrystalCode.Engine.Approvals;

/// <summary>
/// The operator's answer to one approval card.
/// </summary>
public sealed record ApprovalChoice
{
    public static ApprovalChoice Deny { get; } = new("deny", GrantScope.Once);

    /// <summary>
    /// No operator exists to answer, as in an unattended run. This is not a refusal.
    /// </summary>
    public static ApprovalChoice Unavailable { get; } = new("unavailable", GrantScope.Once);

    public static ApprovalChoice AllowOnce { get; } = new("allow", GrantScope.Once);

    public static ApprovalChoice AllowSession { get; } = new("allow", GrantScope.Session);

    public static ApprovalChoice AllowPersistent { get; } = new("allow", GrantScope.Persistent);

    private ApprovalChoice(string action, GrantScope scope)
    {
        Action = action;
        Scope = scope;
    }

    public string Action { get; }

    public GrantScope Scope { get; }

    public bool IsAllow => Action == "allow";

    public bool IsUnavailable => Action == "unavailable";

    public override string ToString() => IsAllow ? $"allow:{Scope.Value}" : Action;
}
