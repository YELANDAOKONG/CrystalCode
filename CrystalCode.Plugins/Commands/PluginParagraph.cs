using System.Text;

namespace CrystalCode.Plugins.Commands;

/// <summary>A run of plain text.</summary>
public sealed record PluginParagraph : PluginBlock
{
    public PluginParagraph(string text)
    {
        Text = PluginDocumentLimits.Clip(text);
    }

    public string Text { get; }

    internal override void Append(StringBuilder text) => text.AppendLine(Text);
}
