using System.Globalization;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Modes;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Builds the <see cref="SessionOptionsChanged"/> catalog from plain session
/// state. Every value names the slash command that selects it, so a front
/// end never parses help text and never bypasses the command path.
/// </summary>
internal static class SessionOptionCatalog
{
    public const string On = "on";

    public const string Off = "off";

    public const string Unset = "unset";

    public static IReadOnlyList<SessionOption> Build(
        HarnessSettings settings,
        string currentMode,
        IReadOnlyList<(string Name, string Description)> customModes,
        ThinkingSelection thinking,
        ApprovalMode approval,
        string promptSet,
        IReadOnlyList<string> promptSets)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var model = settings.ActiveModel;
        var options = new List<SessionOption>
        {
            Mode(currentMode, customModes),
            Model(settings),
        };

        if (Thinking(model, thinking) is { } thinkingOption)
        {
            options.Add(thinkingOption);
        }

        if (model.ContextWindows.Count > 1)
        {
            options.Add(new SessionOption(
                "context",
                "Context",
                "model_config",
                settings.ContextWindow.ToString(CultureInfo.InvariantCulture),
                [.. model.ContextWindows.Select(window => new SessionOptionValue(
                    window.ToString(CultureInfo.InvariantCulture),
                    ContextWindowSelection.Format(window),
                    "/context " + window.ToString(CultureInfo.InvariantCulture)))]));
        }

        if (model.ServiceTiers.Count > 0)
        {
            options.Add(Slot("tier", "Service tier", "/tier", settings.ServiceTier, model.ServiceTiers));
        }

        if (model.Speeds.Count > 0)
        {
            options.Add(Slot("speed", "Speed", "/speed", settings.Speed, model.Speeds));
        }

        options.Add(new SessionOption(
            "approval",
            "Approval",
            "_crystal_approval",
            approval.Value,
            [.. new[] { ApprovalMode.Default, ApprovalMode.Edit, ApprovalMode.Review, ApprovalMode.Audit, ApprovalMode.Full }
                .Select(mode => new SessionOptionValue(mode.Value, ApprovalLabel.For(mode), "/approval " + mode.Value))]));
        options.Add(Reviewer("approval_model", "Approval model", "/approval model", settings, settings.ApprovalModel));
        options.Add(Reviewer(
            "extended_approval",
            "Extended approval",
            "/approval extended",
            settings,
            settings.ExtendedApproval.Reviewer));
        options.Add(new SessionOption(
            "prompt_set",
            "Prompt set",
            "_crystal_prompt",
            promptSet,
            [.. promptSets.Select(name => new SessionOptionValue(name, DisplayCase.Token(name), "/promptset " + name))]));
        options.Add(Switch("agents", "Agents", "_crystal_agents", settings.Agents.Enabled, "/agents"));
        options.Add(Switch(
            "micro_compaction",
            "Microcompaction",
            "_crystal_compaction",
            settings.MicroCompaction.Enabled,
            "/compact micro"));
        options.Add(Switch("estimated_tokens", "Estimated tokens", "_crystal_display", settings.EstimatedTokens, "/tokens"));
        return options;
    }

    private static SessionOption Mode(string current, IReadOnlyList<(string Name, string Description)> custom) =>
        new(
            "mode",
            "Mode",
            "mode",
            current,
            [
                new SessionOptionValue(ModeCatalog.Plan, "Plan", "/mode " + ModeCatalog.Plan, "Built-in reads; no edit, write, or bash"),
                new SessionOptionValue(ModeCatalog.Work, "Work", "/mode " + ModeCatalog.Work, "Built-in edit, write, shell, and background shell tasks"),
                .. custom.Select(mode => new SessionOptionValue(mode.Name, mode.Name, "/mode " + mode.Name, mode.Description))
            ]);

    private static SessionOption Model(HarnessSettings settings)
    {
        var values = new List<SessionOptionValue>();
        foreach (var provider in settings.Catalog.Providers.Keys)
        {
            foreach (var model in settings.Catalog.GetModelNames(new ProviderName(provider)))
            {
                values.Add(new SessionOptionValue(
                    provider + "/" + model,
                    model,
                    "/model " + provider + " " + model,
                    Group: provider));
            }
        }

        return new SessionOption("model", "Model", "model", settings.Provider.Value + "/" + settings.Model, values);
    }

    private static SessionOption? Thinking(ModelSettings model, ThinkingSelection selection)
    {
        if (!model.Thinking)
        {
            return null;
        }

        var values = new List<SessionOptionValue>();
        if (model.ThinkingCanDisable)
        {
            values.Add(new SessionOptionValue(ThinkingSelection.Off.Value, "Off", "/thinking off"));
        }

        values.Add(new SessionOptionValue(ThinkingSelection.Default.Value, "Default", "/thinking default"));
        foreach (var effort in model.ThinkingEfforts)
        {
            values.Add(new SessionOptionValue(effort, ThinkingLabel.For(new ThinkingSelection(effort)), "/thinking " + effort));
        }

        var current = selection == ThinkingSelection.Off
            ? model.ThinkingCanDisable ? ThinkingSelection.Off.Value : ThinkingSelection.Default.Value
            : model.AllowsEffort(selection.Value) ? selection.Value : ThinkingSelection.Default.Value;
        return new SessionOption("thinking", "Thinking", "thought_level", current, values);
    }

    /// <summary>
    /// Tier and speed tokens. Unset is listed so the current value is always
    /// one of the values; the command can cycle back to it but not name it,
    /// so its <see cref="SessionOptionValue.Command"/> is empty.
    /// </summary>
    private static SessionOption Slot(string id, string name, string command, string? current, IReadOnlyList<string> tokens) =>
        new(
            id,
            name,
            "model_config",
            current ?? Unset,
            [
                new SessionOptionValue(Unset, "Unset", string.Empty),
                .. tokens.Select(token => new SessionOptionValue(token, DisplayCase.Token(token), command + " " + token))
            ]);

    private static SessionOption Reviewer(
        string id,
        string name,
        string command,
        HarnessSettings settings,
        ApprovalModelSettings reviewer)
    {
        var values = new List<SessionOptionValue> { new(Off, "Off", command + " " + Off) };
        foreach (var provider in settings.Catalog.Providers.Keys)
        {
            foreach (var model in settings.Catalog.GetModelNames(new ProviderName(provider)))
            {
                values.Add(new SessionOptionValue(
                    provider + "/" + model,
                    model,
                    command + " " + provider + " " + model,
                    Group: provider));
            }
        }

        var current = reviewer is { Enabled: true, HasSelection: true }
            ? reviewer.Provider + "/" + reviewer.Model
            : Off;
        return new SessionOption(id, name, "_crystal_approval", current, values);
    }

    private static SessionOption Switch(string id, string name, string category, bool enabled, string command) =>
        new(
            id,
            name,
            category,
            enabled ? On : Off,
            [new SessionOptionValue(On, "On", command + " " + On), new SessionOptionValue(Off, "Off", command + " " + Off)],
            IsSwitch: true);
}
