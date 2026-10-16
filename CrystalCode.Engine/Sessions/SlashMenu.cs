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
        IReadOnlyList<SlashCompletion>? compactionThinkingArguments = null)
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
                approvalModelArguments,
                approvalThinkingArguments,
                contextArguments,
                compactionModelArguments,
                compactionThinkingArguments);
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
        IReadOnlyList<SlashCompletion>? approvalModelArguments,
        IReadOnlyList<SlashCompletion>? approvalThinkingArguments,
        IReadOnlyList<SlashCompletion>? contextArguments,
        IReadOnlyList<SlashCompletion>? compactionModelArguments,
        IReadOnlyList<SlashCompletion>? compactionThinkingArguments)
    {
        if (spec.Verb == SessionVerb.Context && contextArguments is not null)
        {
            return contextArguments;
        }

        if (spec.Verb == SessionVerb.Approval)
        {
            return ApprovalArguments(
                spec.Arguments,
                approvalModelArguments,
                approvalThinkingArguments);
        }

        if (spec.Verb == SessionVerb.Compact)
        {
            return CompactionArguments(
                spec.Arguments,
                compactionModelArguments,
                compactionThinkingArguments);
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
    /// lists under <c>model</c> and <c>thinking</c>. Unlike the single-purpose
    /// verbs, <c>/approval</c> mixes mode words with two subcommands.
    /// </summary>
    private static IReadOnlyList<SlashCompletion> ApprovalArguments(
        IReadOnlyList<string>? arguments,
        IReadOnlyList<SlashCompletion>? modelArguments,
        IReadOnlyList<SlashCompletion>? thinkingArguments)
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
                _ => null
            };
            var help = argument switch
            {
                "model" => "Show or set the approval model; on and off switch it",
                "thinking" => "Cycle or set the reviewer thinking gear",
                _ => argument
            };
            options.Add(nested is { Count: > 0 }
                ? new SlashCompletion(argument, help, [argument], nested)
                : new SlashCompletion(argument, help, [argument]));
        }

        return options;
    }

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
        IReadOnlyList<SlashCompletion>? thinkingArguments)
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
                "summary" or "auto" => SummarySwitch,
                _ => null
            };
            var help = argument switch
            {
                "model" => "Show or set the compaction model; on and off switch it",
                "thinking" => "Cycle or set the compaction thinking gear",
                "summary" => "Toggle the summary printed after /compact",
                "auto" => "Toggle the summary printed after automatic compaction",
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
