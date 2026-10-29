using CrystalCode.Engine.Plugins.Interfaces;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Builds the full slash command menu: built-in verbs with their argument
/// completions, followed by plugin commands.
/// </summary>
public static class SlashMenu
{
    public static IReadOnlyList<SlashCompletion> Create(
        IReadOnlyList<ISlashCommand>? extras,
        IReadOnlyList<SlashCompletion>? thinkingArguments = null,
        IReadOnlyList<SlashCompletion>? modelArguments = null,
        IReadOnlyList<SlashCompletion>? promptSetArguments = null,
        IReadOnlyList<SlashCompletion>? promptAttachmentArguments = null,
        IReadOnlyList<SlashCompletion>? toolArguments = null,
        IReadOnlyList<SlashCompletion>? exportArguments = null,
        IReadOnlyList<SlashCompletion>? approvalModelArguments = null,
        IReadOnlyList<SlashCompletion>? approvalThinkingArguments = null,
        IReadOnlyList<SlashCompletion>? contextArguments = null,
        IReadOnlyList<SlashCompletion>? compactionModelArguments = null,
        IReadOnlyList<SlashCompletion>? compactionThinkingArguments = null,
        IReadOnlyList<SlashCompletion>? tierArguments = null,
        IReadOnlyList<SlashCompletion>? speedArguments = null,
        IReadOnlyList<SlashCompletion>? approvalTierArguments = null,
        IReadOnlyList<SlashCompletion>? approvalSpeedArguments = null,
        IReadOnlyList<SlashCompletion>? extendedApprovalArguments = null,
        IReadOnlyList<SlashCompletion>? compactionTierArguments = null,
        IReadOnlyList<SlashCompletion>? compactionSpeedArguments = null,
        IReadOnlyList<SlashCompletion>? approvalContextArguments = null,
        IReadOnlyList<SlashCompletion>? compactionContextArguments = null,
        IReadOnlyList<SlashCompletion>? describeArguments = null,
        IReadOnlyList<SlashCompletion>? topicArguments = null)
    {
        var menu = new List<SlashCompletion>();
        foreach (var spec in SlashCatalog.BuiltIn)
        {
            var keys = new List<string> { spec.Name };
            keys.AddRange(spec.Aliases);
            var arguments = ArgumentsFor(
                spec,
                thinkingArguments,
                modelArguments,
                promptSetArguments,
                promptAttachmentArguments,
                toolArguments,
                exportArguments,
                new ApprovalNested(
                    approvalModelArguments,
                    approvalThinkingArguments,
                    approvalTierArguments,
                    approvalSpeedArguments,
                    approvalContextArguments,
                    extendedApprovalArguments),
                contextArguments,
                compactionModelArguments,
                compactionThinkingArguments,
                compactionTierArguments,
                compactionSpeedArguments,
                compactionContextArguments,
                tierArguments,
                speedArguments);
            if (spec.Verb == SessionVerb.Describe && describeArguments is { Count: > 0 })
            {
                arguments = describeArguments;
            }

            if (spec.Verb == SessionVerb.Topic && topicArguments is { Count: > 0 })
            {
                arguments = topicArguments;
            }

            menu.Add(new SlashCompletion(spec.Name, spec.Help, keys, arguments));
        }

        if (extras is null)
        {
            return menu;
        }

        foreach (var command in extras)
        {
            menu.Add(new SlashCompletion(command.Name, command.Help, [command.Name]));
        }

        return menu;
    }

    private static IReadOnlyList<SlashCompletion> ArgumentsFor(
        SlashSpec spec,
        IReadOnlyList<SlashCompletion>? thinkingArguments,
        IReadOnlyList<SlashCompletion>? modelArguments,
        IReadOnlyList<SlashCompletion>? promptSetArguments,
        IReadOnlyList<SlashCompletion>? promptAttachmentArguments,
        IReadOnlyList<SlashCompletion>? toolArguments,
        IReadOnlyList<SlashCompletion>? exportArguments,
        ApprovalNested approvalArguments,
        IReadOnlyList<SlashCompletion>? contextArguments,
        IReadOnlyList<SlashCompletion>? compactionModelArguments,
        IReadOnlyList<SlashCompletion>? compactionThinkingArguments,
        IReadOnlyList<SlashCompletion>? compactionTierArguments,
        IReadOnlyList<SlashCompletion>? compactionSpeedArguments,
        IReadOnlyList<SlashCompletion>? compactionContextArguments,
        IReadOnlyList<SlashCompletion>? tierArguments,
        IReadOnlyList<SlashCompletion>? speedArguments)
    {
        if (spec.Verb == SessionVerb.Context && contextArguments is not null)
        {
            return contextArguments;
        }

        if (spec.Verb == SessionVerb.Tier && tierArguments is not null)
        {
            return tierArguments;
        }

        if (spec.Verb == SessionVerb.Speed && speedArguments is not null)
        {
            return speedArguments;
        }

        if (spec.Verb == SessionVerb.Approval)
        {
            return ApprovalArguments(spec.Arguments, approvalArguments);
        }

        if (spec.Verb == SessionVerb.Compact)
        {
            return CompactionArguments(
                spec.Arguments,
                compactionModelArguments,
                compactionThinkingArguments,
                compactionTierArguments,
                compactionSpeedArguments,
                compactionContextArguments);
        }

        if (spec.Verb == SessionVerb.Thinking && thinkingArguments is not null)
        {
            return thinkingArguments;
        }

        if (spec.Verb == SessionVerb.Model && modelArguments is not null)
        {
            return modelArguments;
        }

        if (spec.Verb == SessionVerb.PromptSet && promptSetArguments is not null)
        {
            return promptSetArguments;
        }

        if (spec.Verb == SessionVerb.PromptAttachment && promptAttachmentArguments is not null)
        {
            return promptAttachmentArguments;
        }

        if (spec.Verb == SessionVerb.Tools && toolArguments is not null)
        {
            return toolArguments;
        }

        if (spec.Verb == SessionVerb.Export && exportArguments is not null)
        {
            return exportArguments;
        }

        return ToArgumentOptions(spec.Arguments);
    }

    /// <summary>
    /// Keeps the static approval arguments and nests the dynamic completion
    /// lists under <c>model</c>, <c>thinking</c>, <c>tier</c>, <c>speed</c>,
    /// and <c>extended</c>. Unlike the single-purpose verbs, <c>/approval</c>
    /// mixes mode words with subcommands.
    /// </summary>
    private static IReadOnlyList<SlashCompletion> ApprovalArguments(
        IReadOnlyList<string>? arguments,
        ApprovalNested nestedArguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return [];
        }

        var options = new List<SlashCompletion>(arguments.Count);
        foreach (var argument in arguments)
        {
            var nested = argument switch
            {
                "model" => nestedArguments.Model,
                "thinking" => nestedArguments.Thinking,
                "tier" => nestedArguments.Tier,
                "speed" => nestedArguments.Speed,
                "context" => nestedArguments.Context,
                "extended" => nestedArguments.Extended,
                _ => null
            };
            var help = argument switch
            {
                "model" => "Show or set the approval model; on and off switch it",
                "thinking" => "Cycle or set the reviewer thinking gear",
                "tier" => "Cycle or set the approval model service tier",
                "speed" => "Cycle or set the approval model speed",
                "context" => "Cycle or set the approval model context budget",
                "extended" => "Extended approval after a review allow",
                _ => argument
            };
            options.Add(nested is { Count: > 0 }
                ? new SlashCompletion(argument, help, [argument], nested)
                : new SlashCompletion(argument, help, [argument]));
        }

        return options;
    }

    private sealed record ApprovalNested(
        IReadOnlyList<SlashCompletion>? Model,
        IReadOnlyList<SlashCompletion>? Thinking,
        IReadOnlyList<SlashCompletion>? Tier,
        IReadOnlyList<SlashCompletion>? Speed,
        IReadOnlyList<SlashCompletion>? Context,
        IReadOnlyList<SlashCompletion>? Extended);

    private static readonly SlashCompletion[] SummarySwitch =
    [
        new("on", "Turn it on", ["on"]),
        new("off", "Turn it off", ["off"])
    ];

    /// <summary>
    /// Nests model and thinking completions under <c>/compact</c>. A bare
    /// command still summarizes and has no required argument.
    /// </summary>
    private static IReadOnlyList<SlashCompletion> CompactionArguments(
        IReadOnlyList<string>? arguments,
        IReadOnlyList<SlashCompletion>? modelArguments,
        IReadOnlyList<SlashCompletion>? thinkingArguments,
        IReadOnlyList<SlashCompletion>? tierArguments,
        IReadOnlyList<SlashCompletion>? speedArguments,
        IReadOnlyList<SlashCompletion>? contextArguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return [];
        }

        var options = new List<SlashCompletion>(arguments.Count);
        foreach (var argument in arguments)
        {
            var nested = argument switch
            {
                "model" => modelArguments,
                "thinking" => thinkingArguments,
                "tier" => tierArguments,
                "speed" => speedArguments,
                "context" => contextArguments,
                "summary" or "auto" or "micro" => SummarySwitch,
                _ => null
            };
            var help = argument switch
            {
                "full" => "Summarize the whole conversation, including the latest turn",
                "model" => "Show or set the compaction model; on and off switch it",
                "thinking" => "Cycle or set the compaction thinking gear",
                "tier" => "Cycle or set the compaction model service tier",
                "speed" => "Cycle or set the compaction model speed",
                "context" => "Cycle or set the compaction model context budget",
                "summary" => "Toggle the summary printed after /compact",
                "auto" => "Toggle the summary printed after automatic compaction",
                "micro" => "Toggle removal of old tool results before each model call",
                _ => argument
            };
            options.Add(nested is { Count: > 0 }
                ? new SlashCompletion(argument, help, [argument], nested)
                : new SlashCompletion(argument, help, [argument]));
        }

        return options;
    }

    private static IReadOnlyList<SlashCompletion> ToArgumentOptions(
        IReadOnlyList<string>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return [];
        }

        var options = new List<SlashCompletion>(arguments.Count);
        foreach (var argument in arguments)
        {
            options.Add(new SlashCompletion(argument, argument, [argument]));
        }

        return options;
    }
}
