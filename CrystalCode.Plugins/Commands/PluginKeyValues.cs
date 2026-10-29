using System.Text;

namespace CrystalCode.Plugins.Commands;

/// <summary>Labelled values, one per line, with the labels aligned.</summary>
public sealed record PluginKeyValues : PluginBlock
{
    public PluginKeyValues(IEnumerable<PluginKeyValue> entries)
    {
        Entries = PluginDocumentLimits.Take(entries, out var omitted);
        Omitted = omitted;
    }

    public IReadOnlyList<PluginKeyValue> Entries { get; }

    /// <summary>Gets how many entries past the limit were dropped.</summary>
    public int Omitted { get; }

    internal override void Append(StringBuilder text)
    {
        var width = Entries.Count == 0 ? 0 : Entries.Max(static entry => entry.Key.Length);
        foreach (var entry in Entries)
        {
            text.Append(entry.Key.PadRight(width)).Append("  ").AppendLine(entry.Value);
        }

        if (Omitted > 0)
        {
            text.AppendLine($"({Omitted} more omitted)");
        }
    }
}
