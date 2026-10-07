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
            var label = Label(entry);
            var detail = label.Length == 0 ? string.Empty : "  " + label;
            if (entry.Effective)
            {
                enabledIndex++;
                lines.Add("* " + enabledIndex + "  " + entry.Name + detail + "  " + Source(entry.Source));
            }
            else
            {
                lines.Add("     " + entry.Name + detail + "  " + Source(entry.Source));
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
            if (entry.Effective)
            {
                names.Add(entry.Name);
            }
        }

        return string.Join(", ", names);
    }

    private static string Label(PromptAttachmentEntry entry)
    {
        var title = string.Equals(entry.Title, entry.Name, StringComparison.Ordinal)
            ? string.Empty
            : entry.Title;
        if (title.Length == 0)
        {
            return entry.Description;
        }

        if (entry.Description.Length == 0)
        {
            return title;
        }

        return title + "  " + entry.Description;
    }

    private static string Source(PromptAttachmentSource source) =>
        source switch
        {
            PromptAttachmentSource.Home => "Home",
            PromptAttachmentSource.Workspace => "Workspace",
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
}
