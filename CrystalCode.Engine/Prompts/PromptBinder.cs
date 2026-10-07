using System.Text.RegularExpressions;

using CrystalCode.Engine.Plugins;
using CrystalCode.Plugins.Environment;
using CrystalCode.Plugins.Placeholders;

namespace CrystalCode.Engine.Prompts;

/// <summary>
/// Substitutes host placeholders in prompt templates.
/// </summary>
public static partial class PromptBinder
{
    public static string Apply(string template, PromptContext session) =>
        Apply(template, session, placeholders: null);

    public static string Apply(
        string template,
        PromptContext session,
        PluginPlaceholderTable? placeholders) =>
        Apply(template, new PromptBinding(Session: session, Placeholders: placeholders));

    public static string Apply(string template, PromptBinding binding)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(binding);

        return PlaceholderPattern().Replace(
            template,
            match => Resolve(match.Groups[1].Value, binding));
    }

    private static string Resolve(string rawName, PromptBinding binding)
    {
        var name = rawName.ToLowerInvariant();
        if (binding.Session is not null && TryGetSessionValue(name, binding.Session, out var sessionValue))
        {
            return sessionValue;
        }

        if (binding.Review is not null && TryGetReviewValue(name, binding.Review, out var reviewValue))
        {
            return reviewValue;
        }

        if (binding.Compaction is not null
            && TryGetCompactionValue(name, binding.Compaction, out var compactionValue))
        {
            return compactionValue;
        }

        if (binding.Placeholders is not null
            && binding.Placeholders.TryResolve(name, CreatePlaceholderContext(binding), out var pluginValue))
        {
            return pluginValue;
        }

        return "{{" + rawName + "}}";
    }

    private static PluginPlaceholderContext CreatePlaceholderContext(PromptBinding binding)
    {
        var session = binding.Session;
        var mode = session?.Mode ?? string.Empty;
        if (mode.Length == 0 && binding.Review is not null)
        {
            mode = "review";
        }
        else if (mode.Length == 0 && binding.Compaction is not null)
        {
            mode = "compaction";
        }

        return new PluginPlaceholderContext(
            mode,
            session?.Workspace ?? string.Empty,
            session?.SessionId ?? string.Empty,
            session?.Approval ?? string.Empty,
            session?.Provider ?? string.Empty,
            session?.Model ?? string.Empty,
            binding.Placeholders?.Environment ?? PluginEnvironment.Empty);
    }

    private static bool TryGetSessionValue(string name, PromptContext context, out string value)
    {
        switch (name)
        {
            case PromptPlaceholder.Env:
                value = context.EnvironmentBlock;
                return true;
            case PromptPlaceholder.Skills:
                value = context.Skills;
                return true;
            case PromptPlaceholder.Instructions:
                value = context.Instructions;
                return true;
            case PromptPlaceholder.InstructionsSection:
                value = context.InstructionsSection;
                return true;
            case PromptPlaceholder.Workspace:
                value = context.Workspace;
                return true;
            case PromptPlaceholder.IsGitRepo:
                value = context.IsGitRepo;
                return true;
            case PromptPlaceholder.GitRoot:
                value = context.GitRoot;
                return true;
            case PromptPlaceholder.Platform:
                value = context.Platform;
                return true;
            case PromptPlaceholder.Date:
                value = context.Date;
                return true;
            case PromptPlaceholder.Time:
                value = context.Time;
                return true;
            case PromptPlaceholder.Os:
                value = context.Os;
                return true;
            case PromptPlaceholder.Architecture:
                value = context.Architecture;
                return true;
            case PromptPlaceholder.Provider:
                value = context.Provider;
                return true;
            case PromptPlaceholder.Model:
                value = context.Model;
                return true;
            case PromptPlaceholder.ModelLine:
                value = context.ModelLine;
                return true;
            case PromptPlaceholder.Mode:
                value = context.Mode;
                return true;
            case PromptPlaceholder.ProductName:
                value = context.ProductName;
                return true;
            case PromptPlaceholder.SessionId:
                value = context.SessionId;
                return true;
            case PromptPlaceholder.Approval:
                value = context.Approval;
                return true;
            default:
                value = string.Empty;
                return false;
        }
    }

    private static bool TryGetReviewValue(string name, ReviewPromptContext context, out string value)
    {
        switch (name)
        {
            case PromptPlaceholder.Conversation:
                value = context.Conversation;
                return true;
            case PromptPlaceholder.ToolName:
                value = context.ToolName;
                return true;
            case PromptPlaceholder.ToolArguments:
                value = context.ToolArguments;
                return true;
            case PromptPlaceholder.HostRisk:
                value = context.HostRisk;
                return true;
            case PromptPlaceholder.HostAuthority:
                value = context.HostAuthority;
                return true;
            case PromptPlaceholder.ClassificationSummary:
                value = context.ClassificationSummary;
                return true;
            default:
                value = string.Empty;
                return false;
        }
    }

    private static bool TryGetCompactionValue(string name, CompactionPromptContext context, out string value)
    {
        switch (name)
        {
            case PromptPlaceholder.Conversation:
                value = context.Conversation;
                return true;
            case PromptPlaceholder.PriorSummarySection:
                value = context.PriorSummarySection;
                return true;
            case PromptPlaceholder.SummaryTask:
                value = context.SummaryTask;
                return true;
            case PromptPlaceholder.OutputTemplate:
                value = context.OutputTemplate;
                return true;
            case PromptPlaceholder.TodosSection:
                value = context.TodosSection;
                return true;
            default:
                value = string.Empty;
                return false;
        }
    }

    [GeneratedRegex(
        @"\{\{\s*([a-z][a-z0-9_]*)\s*\}\}",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderPattern();
}
