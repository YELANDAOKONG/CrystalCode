using System.Globalization;

using CrystalCode.Commands;
using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Run;

/// <summary>
/// Applies <c>crystal run</c> flags to an in-memory settings value.
/// </summary>
internal static class TaskRunOverrides
{
    public static bool TryApply(
        HarnessSettings current,
        TaskRunSettings request,
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
                next = next.WithPromptSet(Required(request.PromptSet, "Prompt set"));
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
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static ApprovalModelSettings ParseApprovalModel(
        HarnessSettings current,
        TaskRunSettings request)
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

        return new ApprovalModelSettings(true, provider.Value, model);
    }

    private static TurnLimits ParseBudget(HarnessSettings current, TaskRunSettings request)
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
