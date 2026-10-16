using System.Globalization;

using CrystalCode.Commands;
using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Run;

/// <summary>
/// Applies <c>crystal run</c> flags to an in-memory settings value.
/// </summary>
internal static class TaskRunOverrides
{
    public static bool TryApply(
        HarnessSettings current,
        SessionLaunchSettings request,
        out HarnessSettings applied,
        out bool planMode,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);
        applied = current;
        planMode = false;
        error = string.Empty;
        if (request.Plan && request.Work)
        {
            error = "Pass either --plan or --work.";
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
                next = next.WithApprovalModel(ParseApprovalModel(next, request));
            }

            if (request.ApprovalThinking is not null)
            {
                var effort = ThinkingSelection.Parse(
                    Required(request.ApprovalThinking, "Approval thinking"));
                next = next.WithApprovalModel(
                    next.ApprovalModel.WithThinkingEffort(effort));
            }

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

            if (request.Skills is not null)
            {
                next = next.WithSkills(ParseSwitch(request.Skills, "--skills"));
            }

            if (request.ExternalTools is not null)
            {
                next = next.WithExternalTools(ParseSwitch(request.ExternalTools, "--external-tools"));
            }

            if (request.Plugins is not null)
            {
                next = next.WithPlugins(ParseSwitch(request.Plugins, "--plugins"));
            }

            if (request.PromptSet is not null)
            {
                next = next.WithPromptSetOverride(Required(request.PromptSet, "Prompt set"));
            }

            var requestedAttachments = request.PromptAttachment ?? [];
            if (request.PromptAttachments is not null)
            {
                var attachmentsOn = ParseSwitch(request.PromptAttachments, "--prompt-attachments");
                if (!attachmentsOn && requestedAttachments.Length > 0)
                {
                    throw new ArgumentException(
                        "Do not pass --prompt-attachment when --prompt-attachments is off.");
                }

                next = next.WithUsePromptAttachments(attachmentsOn);
            }

            if (requestedAttachments.Length > 0)
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

            applied = next;
            planMode = request.Plan;
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

    private static ApprovalModelSettings ParseApprovalModel(
        HarnessSettings current,
        SessionLaunchSettings request)
    {
        var specifiedProvider = request.ApprovalProvider is not null;
        var specifiedModel = request.ApprovalModelId is not null;
        var specified = specifiedProvider || specifiedModel;
        bool? enabled = request.ApprovalModel is null
            ? null
            : ParseSwitch(request.ApprovalModel, "--approval-model");
        if (enabled == false)
        {
            if (specified)
            {
                throw new ArgumentException(
                    "Do not pass --approval-provider or --approval-model-id when --approval-model is off.");
            }

            return current.ApprovalModel.DisabledCopy();
        }

        if (enabled is null && !specified)
        {
            return current.ApprovalModel;
        }

        var provider = specifiedProvider
            ? ProviderName.Parse(Required(request.ApprovalProvider!, "Approval provider"))
            : current.ApprovalModel.Provider is string stored
                ? ProviderName.Parse(stored)
                : current.Provider;
        var model = specifiedModel
            ? Required(request.ApprovalModelId!, "Approval model")
            : current.ApprovalModel.Model
                ?? throw new ArgumentException("Pass --approval-model-id.");

        try
        {
            _ = current.Catalog.GetModel(provider, model);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or ArgumentException)
        {
            throw new ArgumentException(exception.Message, exception);
        }

        return new ApprovalModelSettings(
            true,
            provider.Value,
            model,
            current.ApprovalModel.ThinkingEffort);
    }

    private static CompactionModelSettings ParseCompactionModel(
        HarnessSettings current,
        SessionLaunchSettings request)
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

    private static TurnLimits ParseBudget(HarnessSettings current, SessionLaunchSettings request)
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

    internal static bool TryAcceptPromptSet(CrystalHome home, string? name, out string error)
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

    internal static bool TryAcceptPromptAttachments(
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

    internal static bool TryReadWorkspaceTrust(string? value, out bool enabled, out string error)
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
