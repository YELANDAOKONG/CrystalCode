using CrystalCode.Engine.Sessions;
using CrystalCode.Plugins.Commands;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>Copies a plugin document into the engine's session document.</summary>
internal static class PluginDocuments
{
    public static TranscriptDocument Copy(PluginDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new TranscriptDocument(document.Title, [.. document.Blocks.Select(Copy)]);
    }

    private static TranscriptDocumentBlock Copy(PluginBlock block) =>
        block switch
        {
            PluginParagraph paragraph => new("paragraph", [], [], [paragraph.Text], 0),
            PluginList list => new("list", [], [], list.Items, list.Omitted),
            PluginKeyValues values => new(
                "keyValues",
                [],
                [.. values.Entries.Select(static entry => (IReadOnlyList<string>)[entry.Key, entry.Value])],
                [],
                values.Omitted),
            PluginTable table => new("table", table.Columns, table.Rows, [], table.Omitted),
            _ => new("paragraph", [], [], [], 0)
        };
}
