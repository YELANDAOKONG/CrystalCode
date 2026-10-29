using System.Text;

namespace CrystalCode.Plugins.Commands;

/// <summary>Items shown one per line with a dash.</summary>
public sealed record PluginList : PluginBlock
{
    public PluginList(IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = PluginDocumentLimits.Take(items.Select(PluginDocumentLimits.Clip), out var omitted);
        Omitted = omitted;
    }

    public IReadOnlyList<string> Items { get; }

    /// <summary>Gets how many items past the limit were dropped.</summary>
    public int Omitted { get; }

    internal override void Append(StringBuilder text)
    {
        foreach (var item in Items)
        {
            text.Append("- ").AppendLine(item);
        }

        if (Omitted > 0)
        {
            text.AppendLine($"({Omitted} more omitted)");
        }
    }
}
