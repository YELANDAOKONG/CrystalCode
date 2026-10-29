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

        if (Thinking(model, thinking, "thinking", "Thinking", "thought_level", "/thinking") is { } thinkingOption)
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
        AddTuning(
            options,
            settings,
            "approval",
            "Approval",
            "_crystal_approval",
            "/approval",
            settings.ApprovalModel is { Enabled: true } approvalModel
                ? (approvalModel.Provider!, approvalModel.Model!, approvalModel.ThinkingEffort, approvalModel.ServiceTier, approvalModel.Speed, approvalModel.ContextWindow)
                : null);
        options.Add(Reviewer(
            "extended_approval",
            "Extended approval",
            "/approval extended",
            settings,
            settings.ExtendedApproval.Reviewer));
        AddTuning(
            options,
            settings,
            "extended",
            "Extended approval",
            "_crystal_approval",
            "/approval extended",
            settings.ExtendedApproval.Reviewer is { Enabled: true } extended
                ? (extended.Provider!, extended.Model!, extended.ThinkingEffort, extended.ServiceTier, extended.Speed, extended.ContextWindow)
                : null);
        options.Add(new SessionOption(
            "prompt_set",
            "Prompt set",
            "_crystal_prompt",
            promptSet,
            [.. promptSets.Select(name => new SessionOptionValue(name, DisplayCase.Token(name), "/promptset " + name))]));
        options.Add(Switch("agents", "Agents", "_crystal_agents", settings.Agents.Enabled, "/agents"));
        options.Add(Picker(
            "compaction_model",
            "Compaction model",
            "_crystal_compaction",
            "/compact model",
            settings,
            settings.CompactionModel is { Enabled: true, HasSelection: true } compaction
                ? compaction.Provider + "/" + compaction.Model
                : Off));
        AddTuning(
            options,
            settings,
            "compaction",
            "Compaction",
            "_crystal_compaction",
            "/compact",
            settings.CompactionModel is { Enabled: true } compactionModel
                ? (compactionModel.Provider!, compactionModel.Model!, compactionModel.ThinkingEffort, compactionModel.ServiceTier, compactionModel.Speed, compactionModel.ContextWindow)
                : null);
        foreach (var kind in ResourceKinds.All)
        {
            var word = ResourceKinds.Word(kind);
            var title = ResourceKinds.Title(kind);
            var resource = settings.ResourceModels.For(kind);
            // Only models that accept this input are offered.
            options.Add(new SessionOption(
                word + "_model",
                title + " model",
                "_crystal_describe",
                resource is { Enabled: true, HasSelection: true }
                    ? resource.Provider + "/" + resource.Model
                    : Off,
                [.. ResourceValues(word, settings, kind)]));
            AddTuning(
                options,
                settings,
                word,
                title,
                "_crystal_describe",
                "/describe " + word,
                resource.Enabled
                    ? (resource.Provider!, resource.Model!, resource.ThinkingEffort, resource.ServiceTier, resource.Speed, resource.ContextWindow)
                    : null);
        }
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

    /// <summary>
    /// Thinking, tier, speed, and context of an enabled side model, each
    /// offered only when that model has a choice. Ids are
    /// <paramref name="prefix"/> plus <c>_thinking</c>, <c>_tier</c>,
    /// <c>_speed</c>, and <c>_context</c>. A side model's unset tier or speed
    /// is not sent, and its unset context is the model's first budget.
    /// </summary>
    private static void AddTuning(
        List<SessionOption> options,
        HarnessSettings settings,
        string prefix,
        string label,
        string category,
        string command,
        (string Provider, string Model, ThinkingSelection Thinking, string? ServiceTier, string? Speed, int? Context)? side)
    {
        if (side is not { } selected)
        {
            return;
        }

        ModelSettings model;
        try
        {
            model = settings.Catalog.GetModel(new ProviderName(selected.Provider), selected.Model);
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            return;
        }

        if (Thinking(model, selected.Thinking, prefix + "_thinking", label + " thinking", category, command + " thinking") is { } thinking)
        {
            options.Add(thinking);
        }

        if (model.ServiceTiers.Count > 0)
        {
            options.Add(Slot(prefix + "_tier", label + " tier", command + " tier", selected.ServiceTier, model.ServiceTiers, category));
        }

        if (model.Speeds.Count > 0)
        {
            options.Add(Slot(prefix + "_speed", label + " speed", command + " speed", selected.Speed, model.Speeds, category));
        }

        if (model.ContextWindows.Count > 1)
        {
            var windows = model.ContextWindows;
            options.Add(new SessionOption(
                prefix + "_context",
                label + " context",
                category,
                (selected.Context ?? windows[0]).ToString(CultureInfo.InvariantCulture),
                [.. windows.Select(window => new SessionOptionValue(
                    window.ToString(CultureInfo.InvariantCulture),
                    ContextWindowSelection.Format(window),
                    command + " context " + window.ToString(CultureInfo.InvariantCulture)))]));
        }
    }

    private static SessionOption? Thinking(
        ModelSettings model,
        ThinkingSelection selection,
        string id,
        string name,
        string category,
        string command)
    {
        if (!model.Thinking)
        {
            return null;
        }

        var values = new List<SessionOptionValue>();
        if (model.ThinkingCanDisable)
        {
            values.Add(new SessionOptionValue(ThinkingSelection.Off.Value, "Off", command + " off"));
        }

        values.Add(new SessionOptionValue(ThinkingSelection.Default.Value, "Default", command + " default"));
        foreach (var effort in model.ThinkingEfforts)
        {
            values.Add(new SessionOptionValue(effort, ThinkingLabel.For(new ThinkingSelection(effort)), command + " " + effort));
        }

        var current = selection == ThinkingSelection.Off
            ? model.ThinkingCanDisable ? ThinkingSelection.Off.Value : ThinkingSelection.Default.Value
            : model.AllowsEffort(selection.Value) ? selection.Value : ThinkingSelection.Default.Value;
        return new SessionOption(id, name, category, current, values);
    }

    /// <summary>
    /// Tier and speed tokens. Unset is listed so the current value is always
    /// one of the values; the command can cycle back to it but not name it,
    /// so its <see cref="SessionOptionValue.Command"/> is empty.
    /// </summary>
    private static SessionOption Slot(
        string id,
        string name,
        string command,
        string? current,
        IReadOnlyList<string> tokens,
        string category = "model_config") =>
        new(
            id,
            name,
            category,
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
        ApprovalModelSettings reviewer) =>
        Picker(
            id,
            name,
            "_crystal_approval",
            command,
            settings,
            reviewer is { Enabled: true, HasSelection: true }
                ? reviewer.Provider + "/" + reviewer.Model
                : Off);

    private static IEnumerable<SessionOptionValue> ResourceValues(string word, HarnessSettings settings, ResourceKind kind)
    {
        yield return new SessionOptionValue(Off, "Off", "/describe " + word + " off");
        foreach (var provider in settings.Catalog.Providers.Keys)
        {
            foreach (var model in settings.Catalog.GetModelNames(new ProviderName(provider)))
            {
                if (ResourceKinds.Accepts(settings.Catalog.GetModel(new ProviderName(provider), model), kind))
                {
                    yield return new SessionOptionValue(
                        provider + "/" + model,
                        model,
                        "/describe " + word + " model " + provider + " " + model,
                        Group: provider);
                }
            }
        }
    }

    /// <summary>Off plus every catalog model, grouped by provider.</summary>
    private static SessionOption Picker(
        string id,
        string name,
        string category,
        string command,
        HarnessSettings settings,
        string current)
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

        return new SessionOption(id, name, category, current, values);
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
