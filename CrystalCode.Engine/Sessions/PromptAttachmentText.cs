using CrystalCode.Engine.Prompts;

namespace CrystalCode.Engine.Sessions;

internal static class PromptAttachmentText
{
    public static string Format(PromptResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var lines = new List<string> { "Prompt Attachments:" };
        if (resolution.Attachments.Count == 0)
        {
            lines.Add("  (none)");
            return string.Join(Environment.NewLine, lines);
        }

        var enabledIndex = 0;
        foreach (var entry in resolution.Attachments)
        {
            if (entry.Enabled)
            {
                enabledIndex++;
                lines.Add("* " + enabledIndex + "  " + entry.Name + "  " + Source(entry.Source));
            }
            else
            {
                lines.Add("     " + entry.Name + "  " + Source(entry.Source));
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string Status(PromptResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        var names = new List<string>();
        foreach (var entry in resolution.Attachments)
        {
            if (entry.Enabled && entry.Source is not null)
            {
                names.Add(entry.Name);
            }
        }

        return string.Join(", ", names);
    }

    private static string Source(PromptAttachmentSource? source) =>
        source switch
        {
            PromptAttachmentSource.Home => "Home",
            PromptAttachmentSource.Workspace => "Workspace",
            null => "Not found",
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
}
