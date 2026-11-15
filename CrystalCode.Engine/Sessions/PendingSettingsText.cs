using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Operator labels for settings that apply on the next turn, in the order
/// they are applied.
/// </summary>
public static class PendingSettingsText
{
    public static IReadOnlyList<(string Label, string Value)> Rows(SessionPendingSettings? pending)
    {
        if (pending is null || pending.IsEmpty)
        {
            return [];
        }

        var rows = new List<(string Label, string Value)>();
        if (pending.Provider is { } provider && pending.Model is { } model)
        {
            rows.Add(("Model", ModelSelection.Format(new ProviderName(provider), model)));
        }

        if (pending.ContextWindow is int window)
        {
            rows.Add(("Context", ContextWindowSelection.Format(window)));
        }

        if (pending.ServiceTier is { } tier)
        {
            rows.Add(("Tier", Slot(tier)));
        }

        if (pending.Speed is { } speed)
        {
            rows.Add(("Speed", Slot(speed)));
        }

        if (pending.Thinking is { } thinking)
        {
            rows.Add(("Thinking", DisplayCase.Token(thinking)));
        }

        if (pending.Mode is { } mode)
        {
            rows.Add(("Mode", ModeLabel.For(mode == "plan", mode is "plan" or "work" ? null : mode)));
        }

        if (pending.Approval is { } approval)
        {
            rows.Add(("Approval", ApprovalLabel.For(new ApprovalMode(approval))));
        }

        if (pending.ApprovalThinking is { } approvalThinking)
        {
            rows.Add(("Approval thinking", DisplayCase.Token(approvalThinking)));
        }

        if (pending.ExtendedApprovalThinking is { } extendedThinking)
        {
            rows.Add(("Extended thinking", DisplayCase.Token(extendedThinking)));
        }

        if (pending.ExtendedApprovalTrigger is { } trigger)
        {
            rows.Add(("Extended trigger", DisplayCase.Token(trigger)));
        }

        if (pending.ExtendedApprovalPrimary is bool primary)
        {
            rows.Add(("Extended primary review", primary ? "On" : "Off"));
        }

        return rows;
    }

    /// <summary>One line for a status bar, for example <c>Next: Plan, Approval Full</c>.</summary>
    public static string Summary(SessionPendingSettings? pending)
    {
        var rows = Rows(pending);
        return rows.Count == 0
            ? string.Empty
            : "Next: " + string.Join(", ", rows.Select(static row => row.Label == "Mode" ? row.Value : row.Label + " " + row.Value));
    }

    private static string Slot(string token) =>
        token == SessionPendingSettings.Unset ? "not set" : DisplayCase.Token(token);
}
