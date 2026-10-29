using System.Text;

namespace CrystalCode.Plugins.Commands;

/// <summary>
/// Content a slash command hands to the host to draw. The host picks the
/// presentation: a bordered card in the terminal, structured fields for
/// other front ends. <see cref="ToText"/> is the plain form for output that
/// cannot draw.
/// </summary>
public sealed record PluginDocument
{
    public PluginDocument(string title, IEnumerable<PluginBlock> blocks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(blocks);
        Title = PluginDocumentLimits.Clip(title).Trim();
        Blocks = [.. blocks.Take(PluginDocumentLimits.MaximumBlocks)];
    }

    public string Title { get; }

    public IReadOnlyList<PluginBlock> Blocks { get; }

    /// <summary>Returns the title and blocks as aligned plain text.</summary>
    public string ToText()
    {
        var text = new StringBuilder();
        text.AppendLine(Title);
        foreach (var block in Blocks)
        {
            text.AppendLine();
            block.Append(text);
        }

        return text.ToString().TrimEnd();
    }

    public override string ToString() => nameof(PluginDocument);
}
