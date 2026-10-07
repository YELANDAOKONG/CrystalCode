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
        IReadOnlyList<SlashCompletion>? exportArguments = null)
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
                exportArguments);
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
        IReadOnlyList<SlashCompletion>? exportArguments)
    {
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
