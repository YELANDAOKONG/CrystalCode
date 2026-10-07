using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Prompts;

internal static class PromptAttachmentCompletions
{
    public static IReadOnlyList<SlashCompletion> For(PromptResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var discovered = new List<string>();
        var enabled = new List<string>();
        foreach (var entry in resolution.Attachments)
        {
            if (entry.Source is not null)
            {
                discovered.Add(entry.Name);
            }

            if (entry.Enabled)
            {
                enabled.Add(entry.Name);
            }
        }

        return
        [
            new("enable", "Enable a prompt attachment", ["enable"], Names(discovered)),
            new("disable", "Disable a prompt attachment", ["disable"], Names(enabled)),
            new("up", "Move a prompt attachment earlier", ["up"], Names(enabled)),
            new("down", "Move a prompt attachment later", ["down"], Names(enabled))
        ];
    }

    private static IReadOnlyList<SlashCompletion> Names(IReadOnlyList<string> names)
    {
        var options = new List<SlashCompletion>(names.Count);
        foreach (var name in names)
        {
            options.Add(new SlashCompletion(name, "Prompt attachment", [name]));
        }

        return options;
    }
}
