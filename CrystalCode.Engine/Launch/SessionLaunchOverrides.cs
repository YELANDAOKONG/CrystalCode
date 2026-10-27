using System.Globalization;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Launch;

/// <summary>
/// Applies launch options to an in-memory settings value. Shared by every
/// front end that starts a session; nothing here writes <c>config.json</c>.
/// </summary>
public static class SessionLaunchOverrides
{
    public static bool TryApply(
        HarnessSettings current,
        SessionLaunchOptions request,
        out HarnessSettings applied,
        out string? mode,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);
        applied = current;
        mode = null;
        error = string.Empty;
        if ((request.Plan ? 1 : 0) + (request.Work ? 1 : 0) + (request.Mode is null ? 0 : 1) > 1)
        {
            error = "Pass only one of --plan, --work, or --mode.";
            return false;
        }

        try
        {
            var next = current.WithOverrides(request.Provider, request.Model);
            if (request.Approval is not null)
            {
                next = next.WithApproval(ApprovalMode.Parse(Required(request.Approval, "Approval mode")));
            }

            if (request.ApprovalModel is not null
                || request.ApprovalProvider is not null
                || request.ApprovalModelId is not null)
            {
                next = next.WithApprovalModel(ParseReviewer(
                    next,
                    next.ApprovalModel,
                    request.ApprovalModel,
                    request.ApprovalProvider,
                    request.ApprovalModelId,
                    ApprovalFlags));
            }

            next = next.WithApprovalModel(TuneReviewer(
                next.ApprovalModel,
                request.ApprovalThinking,
                request.ApprovalTier,
                request.ApprovalSpeed,
                ApprovalFlags));

            next = next.WithExtendedApproval(ParseExtendedApproval(next, request));

            if (request.CompactionModel is not null
                || request.CompactionProvider is not null
                || request.CompactionModelId is not null)
            {
                next = next.WithCompactionModel(ParseCompactionModel(next, request));
            }

            if (request.CompactionThinking is not null)
            {
                var effort = ThinkingSelection.Parse(
                    Required(request.CompactionThinking, "Compaction thinking"));
                next = next.WithCompactionModel(
                    next.CompactionModel.WithThinkingEffort(effort));
            }

            if (request.Thinking is not null)
            {
                next = next.WithThinkingEffort(
                    ThinkingSelection.Parse(Required(request.Thinking, "Thinking effort")));
            }

            if (request.Context is not null)
            {
                next = next.WithContextBudget(Required(request.Context, "Context budget"));
            }

            if (request.Tier is not null)
            {
                next = next.WithTier(
                    next.Provider,
                    next.Model,
                    Required(request.Tier, "Service tier"));
            }

            if (request.Speed is not null)
            {
                next = next.WithSpeed(
                    next.Provider,
                    next.Model,
                    Required(request.Speed, "Speed"));
            }

            if (request.Agents is not null || request.AgentsConcurrency is not null)
            {
                next = next.WithAgents(next.Agents with
                {
                    Enabled = request.Agents is null ? next.Agents.Enabled : ParseSwitch(request.Agents, "--agents"),
                    MaxConcurrent = request.AgentsConcurrency ?? next.Agents.MaxConcurrent
                });
            }

            if (request.AgentsModelCalls is not null
                || request.AgentsToolCalls is not null
                || request.AgentsDuration is not null)
            {
                var limits = next.Agents.Limits;
                if (request.AgentsModelCalls is not null)
                {
                    limits = limits.With("model-calls", ParseOptionalCount(
                        Required(request.AgentsModelCalls, "Agent model calls"), "Agent model calls", minimum: 1));
                }

                if (request.AgentsToolCalls is not null)
                {
                    limits = limits.With("tool-calls", ParseOptionalCount(
                        Required(request.AgentsToolCalls, "Agent tool calls"), "Agent tool calls", minimum: 0));
                }

                if (request.AgentsDuration is not null)
                {
                    limits = limits.With("duration", ParseOptionalCount(
                        Required(request.AgentsDuration, "Agent duration"), "Agent duration", minimum: 1));
                }

                next = next.WithAgents(next.Agents with { Limits = limits });
            }

            if (request.Skills is not null)
            {
                next = next.WithSkills(ParseSwitch(request.Skills, "--skills"));
            }

            if (request.ExternalTools is not null)
            {
                next = next.WithExternalTools(ParseSwitch(request.ExternalTools, "--external-tools"));
            }

            if (request.Mcp is not null)
            {
                next = next.WithMcp(ParseSwitch(request.Mcp, "--mcp"));
            }

            if (request.Plugins is not null)
            {
                next = next.WithPlugins(ParseSwitch(request.Plugins, "--plugins"));
            }

            if (request.WorkspaceTrust is not null)
            {
                next = next.WithWorkspaceTrust(ParseSwitch(request.WorkspaceTrust, "--workspace-trust"));
            }

            if (request.Rules is not null)
            {
                next = next.WithRuleFiles(
                    RuleFileSettings.ParseLaunch(request.Rules, next.RuleFiles));
            }

            if (request.Theme is not null)
            {
                next = next.WithTheme(Required(request.Theme, "Theme"));
            }

            if (request.PromptSet is not null)
            {
                next = next.WithPromptSetOverride(Required(request.PromptSet, "Prompt set"));
            }

            var requestedAttachments = request.PromptAttachment;
            if (request.PromptAttachments is not null)
            {
                var attachmentsOn = ParseSwitch(request.PromptAttachments, "--prompt-attachments");
                if (!attachmentsOn && requestedAttachments.Count > 0)
                {
                    throw new ArgumentException(
                        "Do not pass --prompt-attachment when --prompt-attachments is off.");
                }

                next = next.WithUsePromptAttachments(attachmentsOn);
            }

            if (requestedAttachments.Count > 0)
            {
                next = next.WithPromptAttachments(NormalizeAttachmentNames(requestedAttachments));
            }

            if (request.ModelCalls is not null
                || request.ToolCalls is not null
                || request.Duration is not null)
            {
                next = next.WithExecutionBudget(ParseBudget(current, request));
            }

            if (request.BashTimeout is not null)
            {
                next = next.WithBashTimeout(ParseOptionalCount(
                    Required(request.BashTimeout, "Bash timeout"),
                    "Bash timeout",
                    minimum: 1));
            }

            if (request.DisableTool.Count > 0)
            {
                next = next.WithDisabledBuiltInTools(request.DisableTool);
            }

            applied = next;
            mode = request.Plan ? "plan" : request.Work ? "work" : request.Mode?.Trim();
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or KeyNotFoundException)
        {
            error = exception.Message;
            return false;
        }
    }

    public static IReadOnlyList<string> UnavailableThinkingNotes(HarnessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var notes = new List<string>();
        AddUnavailableThinkingNote(
            notes,
            settings.ThinkingEffort,
            settings.ActiveModel,
            "Thinking effort");
        var approval = settings.ApprovalModel;
        if (approval.Enabled && approval.HasSelection)
        {
            try
            {
                var model = settings.Catalog.GetModel(
                    ProviderName.Parse(approval.Provider!),
                    approval.Model!);
                AddUnavailableThinkingNote(
                    notes,
                    approval.ThinkingEffort,
                    model,
                    "Approval thinking effort");
            }
            catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
            {
                // A missing approval model fails setup on its own.
            }
        }

        var extended = settings.ExtendedApproval.Reviewer;
        if (extended.Enabled && extended.HasSelection)
        {
            try
            {
                var model = settings.Catalog.GetModel(
                    ProviderName.Parse(extended.Provider!),
                    extended.Model!);
                AddUnavailableThinkingNote(
                    notes,
                    extended.ThinkingEffort,
                    model,
                    "Extended approval thinking effort");
            }
            catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
            {
                // A missing extended approval model fails setup on its own.
            }
        }

        var compaction = settings.CompactionModel;
        if (compaction.Enabled && compaction.HasSelection)
        {
            try
            {
                var model = settings.Catalog.GetModel(
                    ProviderName.Parse(compaction.Provider!),
                    compaction.Model!);
                AddUnavailableThinkingNote(
                    notes,
                    compaction.ThinkingEffort,
                    model,
                    "Compaction thinking effort");
            }
            catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
            {
                // A missing compaction model fails setup on its own.
            }
        }

        return notes;
    }

    private static void AddUnavailableThinkingNote(
        List<string> notes,
        ThinkingSelection effort,
        ModelSettings model,
        string label)
    {
        if (effort == ThinkingSelection.Default)
        {
            return;
        }

        // A model that does not think already omits reasoning hints.
        // Off asks for that same result, so it is not a fallback.
        if (!model.Thinking)
        {
            if (effort != ThinkingSelection.Off)
            {
                notes.Add(
                    $"{label} '{effort.Value}' is not available for this model. The request uses the provider default.");
            }

            return;
        }

        if (effort != ThinkingSelection.Off && !model.AllowsEffort(effort.Value))
        {
            notes.Add(
                $"{label} '{effort.Value}' is not available for this model. The request uses the provider default.");
            return;
        }

        if (effort == ThinkingSelection.Off && !model.ThinkingCanDisable)
        {
            notes.Add("The selected model cannot disable thinking. The request uses the provider default.");
        }
    }

    private static readonly ReviewerFlags ApprovalFlags = new(
        "--approval-model",
        "--approval-provider",
        "--approval-model-id",
        "Approval");

    private static readonly ReviewerFlags ExtendedFlags = new(
        "--extended-approval",
        "--extended-approval-provider",
        "--extended-approval-model-id",
        "Extended approval");

    private static ExtendedApprovalSettings ParseExtendedApproval(
        HarnessSettings current,
        SessionLaunchOptions request)
    {
        var extended = current.ExtendedApproval;
        var reviewer = extended.Reviewer;
        if (request.ExtendedApproval is not null
            || request.ExtendedApprovalProvider is not null
            || request.ExtendedApprovalModelId is not null)
        {
            reviewer = ParseReviewer(
                current,
                reviewer,
                request.ExtendedApproval,
                request.ExtendedApprovalProvider,
                request.ExtendedApprovalModelId,
                ExtendedFlags);
        }

        reviewer = TuneReviewer(
            reviewer,
            request.ExtendedApprovalThinking,
            request.ExtendedApprovalTier,
            request.ExtendedApprovalSpeed,
            ExtendedFlags);
        extended = extended.WithReviewer(reviewer);
        if (request.ExtendedApprovalTrigger is not null)
        {
            if (!ReviewRiskLevel.TryParse(request.ExtendedApprovalTrigger, out var trigger))
            {
                throw new ArgumentException("--extended-approval-trigger must be low, medium, or high.");
            }

            extended = extended.WithTrigger(trigger);
        }

        if (request.ExtendedApprovalPrimary is not null)
        {
            extended = extended.WithIncludePrimaryReview(
                ParseSwitch(request.ExtendedApprovalPrimary, "--extended-approval-primary"));
        }

        return extended;
    }

    private static ApprovalModelSettings ParseReviewer(
        HarnessSettings current,
        ApprovalModelSettings stored,
        string? switchText,
        string? providerText,
        string? modelText,
        ReviewerFlags flags)
    {
        var specifiedProvider = providerText is not null;
        var specifiedModel = modelText is not null;
        var specified = specifiedProvider || specifiedModel;
        bool? enabled = switchText is null ? null : ParseSwitch(switchText, flags.Switch);
        if (enabled == false)
        {
            if (specified)
            {
                throw new ArgumentException(
                    $"Do not pass {flags.Provider} or {flags.Model} when {flags.Switch} is off.");
            }

            return stored.DisabledCopy();
        }

        if (enabled is null && !specified)
        {
            return stored;
        }

        var provider = specifiedProvider
            ? ProviderName.Parse(Required(providerText!, flags.Label + " provider"))
            : stored.Provider is string saved
                ? ProviderName.Parse(saved)
                : current.Provider;
        var model = specifiedModel
            ? Required(modelText!, flags.Label + " model")
            : stored.Model
                ?? throw new ArgumentException($"Pass {flags.Model}.");

        try
        {
            _ = current.Catalog.GetModel(provider, model);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
        {
            throw new ArgumentException(exception.Message, exception);
        }

        // A stored tier or speed belongs to the stored model only.
        var sameModel = string.Equals(stored.Provider, provider.Value, StringComparison.Ordinal)
            && string.Equals(stored.Model, model, StringComparison.Ordinal);
        return new ApprovalModelSettings(
            true,
            provider.Value,
            model,
            stored.ThinkingEffort,
            sameModel ? stored.ServiceTier : null,
            sameModel ? stored.Speed : null);
    }

    private static ApprovalModelSettings TuneReviewer(
        ApprovalModelSettings reviewer,
        string? thinking,
        string? tier,
        string? speed,
        ReviewerFlags flags)
    {
        var next = reviewer;
        if (thinking is not null)
        {
            next = next.WithThinkingEffort(
                ThinkingSelection.Parse(Required(thinking, flags.Label + " thinking")));
        }

        if (tier is not null)
        {
            next = next.WithServiceTier(Token(tier, flags.Label + " service tier"));
        }

        if (speed is not null)
        {
            next = next.WithSpeed(Token(speed, flags.Label + " speed"));
        }

        return next;
    }

    private static string Token(string text, string label)
    {
        if (!CatalogToken.TryNormalize(Required(text, label), out var token))
        {
            throw new ArgumentException(
                label + " must start with a letter and then use only letters, digits, underscores, and hyphens.");
        }

        return token;
    }

    private sealed record ReviewerFlags(string Switch, string Provider, string Model, string Label);

    private static CompactionModelSettings ParseCompactionModel(
        HarnessSettings current,
        SessionLaunchOptions request)
    {
        var specifiedProvider = request.CompactionProvider is not null;
        var specifiedModel = request.CompactionModelId is not null;
        var specified = specifiedProvider || specifiedModel;
        bool? enabled = request.CompactionModel is null
            ? null
            : ParseSwitch(request.CompactionModel, "--compaction-model");
        if (enabled == false)
        {
            if (specified)
            {
                throw new ArgumentException(
                    "Do not pass --compaction-provider or --compaction-model-id when --compaction-model is off.");
            }

            return current.CompactionModel.DisabledCopy();
        }

        if (enabled is null && !specified)
        {
            return current.CompactionModel;
        }

        var provider = specifiedProvider
            ? ProviderName.Parse(Required(request.CompactionProvider!, "Compaction provider"))
            : current.CompactionModel.Provider is string stored
                ? ProviderName.Parse(stored)
                : current.Provider;
        var model = specifiedModel
            ? Required(request.CompactionModelId!, "Compaction model")
            : current.CompactionModel.Model
                ?? throw new ArgumentException("Pass --compaction-model-id.");

        try
        {
            _ = current.Catalog.GetModel(provider, model);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
        {
            throw new ArgumentException(exception.Message, exception);
        }

        return new CompactionModelSettings(
            true,
            provider.Value,
            model,
            current.CompactionModel.ThinkingEffort);
    }

    private static TurnLimits ParseBudget(HarnessSettings current, SessionLaunchOptions request)
    {
        var modelCalls = current.ExecutionBudget.MaximumModelCalls;
        var toolCalls = current.ExecutionBudget.MaximumToolCalls;
        var duration = current.ExecutionBudget.MaximumDuration;
        if (request.ModelCalls is not null)
        {
            modelCalls = ParseOptionalCount(
                Required(request.ModelCalls, "Model calls"),
                "Model calls",
                minimum: 1);
        }

        if (request.ToolCalls is not null)
        {
            toolCalls = ParseOptionalCount(
                Required(request.ToolCalls, "Tool calls"),
                "Tool calls",
                minimum: 0);
        }

        if (request.Duration is not null)
        {
            var seconds = ParseOptionalCount(
                Required(request.Duration, "Duration"),
                "Duration",
                minimum: 1);
            duration = seconds is int value ? TimeSpan.FromSeconds(value) : null;
        }

        return new TurnLimits(modelCalls, toolCalls, duration);
    }

    private static int? ParseOptionalCount(string text, string label, int minimum)
    {
        if (text.Equals("unlimited", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed < minimum)
        {
            var bound = minimum == 0 ? "a non-negative integer" : "a positive integer";
            throw new ArgumentException(label + " must be " + bound + " or unlimited.");
        }

        return parsed;
    }

    public static bool TryAcceptPromptSet(CrystalHome home, string? name, out string error)
    {
        ArgumentNullException.ThrowIfNull(home);
        error = string.Empty;
        if (name is null)
        {
            return true;
        }

        if (new PromptStore(home).ContainsSet(name))
        {
            return true;
        }

        error = "Prompt set not found  " + name.Trim();
        return false;
    }

    public static bool TryAcceptPromptAttachments(
        CrystalHome home,
        string workspaceRoot,
        IReadOnlyList<string>? names,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(home);
        error = string.Empty;
        if (names is null || names.Count == 0)
        {
            return true;
        }

        var store = new PromptStore(home);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || store.ContainsAttachment(workspaceRoot, name))
            {
                continue;
            }

            error = "Prompt attachment not found  " + name.Trim();
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> NormalizeAttachmentNames(IReadOnlyList<string> names)
    {
        var normalized = new List<string>(names.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in names)
        {
            var name = Required(raw, "Prompt attachment");
            if (!PromptAttachmentNames.IsValid(name))
            {
                throw new ArgumentException("Prompt attachment name is invalid  " + name);
            }

            if (seen.Add(name))
            {
                normalized.Add(name);
            }
        }

        return normalized;
    }

    public static bool TryReadWorkspaceTrust(string? value, out bool enabled, out string error)
    {
        enabled = true;
        error = string.Empty;
        if (value is null)
        {
            return true;
        }

        try
        {
            enabled = ParseSwitch(value, "--workspace-trust");
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool ParseSwitch(string text, string flag)
    {
        var normalized = Required(text, flag).ToLowerInvariant();
        if (normalized == "on")
        {
            return true;
        }

        if (normalized == "off")
        {
            return false;
        }

        throw new ArgumentException(flag + " must be on or off.");
    }

    private static string Required(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException(label + " is required.");
        }

        return text.Trim();
    }
}
