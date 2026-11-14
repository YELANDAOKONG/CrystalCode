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

    /// <summary>The <c>topic_model</c> value that names topics with the session model.</summary>
    public const string SessionModel = "session";

    public const string Unset = "unset";

    internal const string SideModelSelectionFlag = "--select";

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
        var approvalModel = settings.ApprovalModel;
        options.Add(Switch("approval_model_enabled", "Use a separate approval model", "_crystal_approval",
            approvalModel.Enabled, "/approval model"));
        options.Add(ConfiguredModel("approval_model", "Approval model", "_crystal_approval",
            "/approval model", settings, approvalModel.Provider, approvalModel.Model));
        AddTuning(
            options,
            settings,
            "approval",
            "Approval",
            "_crystal_approval",
            "/approval",
            approvalModel.HasSelection
                ? (approvalModel.Provider!, approvalModel.Model!, approvalModel.ThinkingEffort, approvalModel.ServiceTier, approvalModel.Speed, approvalModel.ContextWindow)
                : null);
        var extended = settings.ExtendedApproval.Reviewer;
        options.Add(Switch("extended_approval", "Extended approval", "_crystal_approval",
            extended.Enabled, "/approval extended"));
        options.Add(ConfiguredModel("extended_model", "Extended approval model", "_crystal_approval",
            "/approval extended", settings, extended.Provider, extended.Model));
        AddTuning(
            options,
            settings,
            "extended",
            "Extended approval",
            "_crystal_approval",
            "/approval extended",
            extended.HasSelection
                ? (extended.Provider!, extended.Model!, extended.ThinkingEffort, extended.ServiceTier, extended.Speed, extended.ContextWindow)
                : null);
        options.Add(new SessionOption(
            "prompt_set",
            "Prompt set",
            "_crystal_prompt",
            promptSet,
            [.. promptSets.Select(name => new SessionOptionValue(name, DisplayCase.Token(name), "/promptset " + name))]));
        options.Add(Switch("agents", "Agents", "_crystal_agents", settings.Agents.Enabled, "/agents"));
        var compactionModel = settings.CompactionModel;
        options.Add(Switch("compaction_model_enabled", "Use a separate compaction model", "_crystal_compaction",
            compactionModel.Enabled, "/compact model"));
        options.Add(ConfiguredModel("compaction_model", "Compaction model", "_crystal_compaction",
            "/compact model", settings, compactionModel.Provider, compactionModel.Model));
        AddTuning(
            options,
            settings,
            "compaction",
            "Compaction",
            "_crystal_compaction",
            "/compact",
            compactionModel.HasSelection
                ? (compactionModel.Provider!, compactionModel.Model!, compactionModel.ThinkingEffort, compactionModel.ServiceTier, compactionModel.Speed, compactionModel.ContextWindow)
                : null);
        options.Add(Switch("topic", "Topic naming", "_crystal_topic", settings.TopicModel.Enabled, "/topic"));
        var topic = settings.TopicModel;
        options.Add(Picker(
            "topic_model",
            "Topic model",
            "_crystal_topic",
            "/topic model",
            settings,
            topic.UsesSessionModel ? SessionModel : topic.Provider + "/" + topic.Model,
            new SessionOptionValue(SessionModel, "Session model", "/topic model")));
        if (topic.UsesSessionModel)
        {
            // Tier, speed, and context follow the session, so only the gear is the topic's own.
            if (Thinking(settings.ActiveModel, topic.ThinkingEffort, "topic_thinking", "Topic thinking", "_crystal_topic", "/topic thinking") is { } gear)
            {
                options.Add(gear);
            }
        }
        else
        {
            AddTuning(
                options,
                settings,
                "topic",
                "Topic",
                "_crystal_topic",
                "/topic",
                (topic.Provider!, topic.Model!, topic.ThinkingEffort, topic.ServiceTier, topic.Speed, topic.ContextWindow));
        }

        var pursue = settings.PursueModel;
        options.Add(Picker("pursue_model", "Pursue model", "_crystal_pursue", "/pursue model",
            settings, pursue.UsesSessionModel ? SessionModel : pursue.Provider + "/" + pursue.Model,
            new SessionOptionValue(SessionModel, "Session model", "/pursue model")));
        if (pursue.UsesSessionModel)
        {
            if (Thinking(settings.ActiveModel, pursue.ThinkingEffort, "pursue_thinking", "Pursue thinking",
                    "_crystal_pursue", "/pursue thinking") is { } gear)
            {
                options.Add(gear);
            }
        }
        else
        {
            AddTuning(options, settings, "pursue", "Pursue", "_crystal_pursue", "/pursue",
                (pursue.Provider!, pursue.Model!, pursue.ThinkingEffort, pursue.ServiceTier, pursue.Speed, pursue.ContextWindow));
        }

        var pursueCompaction = pursue.Compaction;
        options.Add(Picker("pursue_compaction_model", "Pursue compaction model", "_crystal_pursue",
            "/pursue compaction model", settings,
            pursueCompaction.HasSelection ? pursueCompaction.Provider + "/" + pursueCompaction.Model : SessionModel,
            new SessionOptionValue(SessionModel, "Session setting", "/pursue compaction model")));
        AddTuning(options, settings, "pursue_compaction", "Pursue compaction", "_crystal_pursue", "/pursue compaction",
            pursueCompaction.HasSelection
                ? (pursueCompaction.Provider!, pursueCompaction.Model!, pursueCompaction.ThinkingEffort,
                    pursueCompaction.ServiceTier, pursueCompaction.Speed, pursueCompaction.ContextWindow)
                : null);

        foreach (var kind in ResourceKinds.All)
        {
            var word = ResourceKinds.Word(kind);
            var title = ResourceKinds.Title(kind);
            var resource = settings.ResourceModels.For(kind);
            var command = "/describe " + word;
            options.Add(Switch(word + "_model_enabled", title + " description", "_crystal_describe",
                resource.Enabled, command));
            options.Add(ConfiguredModel(word + "_model", title + " model", "_crystal_describe",
                command + " model", settings, resource.Provider, resource.Model, kind));
            AddTuning(
                options,
                settings,
                word,
                title,
                "_crystal_describe",
                command,
                resource.HasSelection
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
    /// Thinking, tier, speed, and context of a selected side model, each
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

    private static SessionOption ConfiguredModel(
        string id,
        string name,
        string category,
        string command,
        HarnessSettings settings,
        string? provider,
        string? model,
        ResourceKind? kind = null)
    {
        var current = provider is not null && model is not null ? provider + "/" + model : Unset;
        return Picker(id, name, category, command + " " + SideModelSelectionFlag, settings, current,
            new SessionOptionValue(Unset, "Unset", string.Empty), kind);
    }

    private static SessionOption Picker(
        string id,
        string name,
        string category,
        string command,
        HarnessSettings settings,
        string current,
        SessionOptionValue initialValue,
        ResourceKind? kind = null)
    {
        var values = new List<SessionOptionValue> { initialValue };
        foreach (var provider in settings.Catalog.Providers.Keys)
        {
            var providerName = new ProviderName(provider);
            foreach (var model in settings.Catalog.GetModelNames(providerName))
            {
                if (kind is { } resourceKind && !ResourceKinds.Accepts(settings.Catalog.GetModel(providerName, model), resourceKind))
                {
                    continue;
                }

                values.Add(new SessionOptionValue(
                    provider + "/" + model,
                    model,
                    command + " " + provider + " " + model,
                    Group: provider));
            }
        }

        // A disabled model can still be stored after its catalog entry is removed.
        if (!values.Any(value => value.Value == current))
        {
            values.Add(new SessionOptionValue(current, current, string.Empty));
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
