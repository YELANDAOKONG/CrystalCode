using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Prompts;

namespace CrystalCode.Engine.Modes;

/// <summary>
/// Composes the system text of a custom mode. The body is the mode's own
/// <c>prompt.md</c>. The host appends the environment, skill guidance,
/// instructions, and disabled-tool slots the body does not already name, so a
/// short prompt still receives the workspace facts Plan and Work carry.
/// </summary>
internal static class ModePrompt
{
    private static readonly string[] Slots =
    [
        "{{env}}",
        "{{skills}}",
        "{{instructions_section}}",
        "{{disabled_tools}}",
        "{{clock}}",
        "{{session}}"
    ];

    public static string Compose(
        ModeDefinition mode,
        PromptContext context,
        PluginPlaceholderTable? placeholders)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(context);
        var template = mode.Prompt;
        foreach (var slot in Slots)
        {
            if (!template.Contains(slot, StringComparison.OrdinalIgnoreCase)
                && !(slot == "{{instructions_section}}"
                    && template.Contains("{{instructions}}", StringComparison.OrdinalIgnoreCase)))
            {
                template += "\n\n" + slot;
            }
        }

        return PromptBinder.Apply(template, context, placeholders).Trim();
    }
}
