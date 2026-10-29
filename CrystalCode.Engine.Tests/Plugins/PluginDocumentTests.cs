using CrystalCode.Engine.Events;
using CrystalCode.Engine.Plugins.Disk;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Sessions;
using CrystalCode.Plugins.Commands;

using Xunit;

namespace CrystalCode.Engine.Tests.Plugins;

public sealed class PluginDocumentTests
{
    [Fact]
    public void ToText_AlignsKeysAndTableColumns()
    {
        var document = new PluginDocument(
            "Report",
            [
                new PluginKeyValues([new PluginKeyValue("a", "1"), new PluginKeyValue("long", "2")]),
                new PluginTable(["name", "ms"], [["x", "10"], ["yyyy", "2"]])
            ]);

        Assert.Equal(
            "Report\n\na     1\nlong  2\n\nname  ms\n----  --\nx     10\nyyyy  2".Replace("\n", Environment.NewLine),
            PluginDocuments.Copy(document).ToText());
    }

    [Fact]
    public void Table_RejectsARowOfTheWrongWidth()
    {
        Assert.Throws<ArgumentException>(() => new PluginTable(["a", "b"], [["only one"]]));
        Assert.Throws<ArgumentException>(() => new PluginTable([], []));
    }

    [Fact]
    public void Limits_CutTextRowsAndBlocksInsteadOfFailing()
    {
        var long_ = new string('x', PluginDocumentLimits.MaximumTextLength + 50);
        var rows = Enumerable.Range(0, PluginDocumentLimits.MaximumRows + 5).Select(i => (IEnumerable<string>)[i.ToString()]);
        var blocks = Enumerable.Range(0, PluginDocumentLimits.MaximumBlocks + 5)
            .Select(_ => (PluginBlock)new PluginParagraph(long_));

        var table = new PluginTable(["n"], rows);
        var document = new PluginDocument("t", blocks);

        Assert.Equal(PluginDocumentLimits.MaximumRows, table.Rows.Count);
        Assert.Equal(5, table.Omitted);
        Assert.Equal(PluginDocumentLimits.MaximumBlocks, document.Blocks.Count);
        Assert.Equal(PluginDocumentLimits.MaximumTextLength, ((PluginParagraph)document.Blocks[0]).Text.Length);
        Assert.Contains("(5 more omitted)", PluginDocuments.Copy(new PluginDocument("t", [table])).ToText());
    }

    [Fact]
    public void DiskCommand_ForwardsADocumentToTheSessionOutput()
    {
        var output = new RecordingOutput();
        var command = new DiskCommand(new DocumentCommand());

        command.Execute(string.Empty, output);

        Assert.Equal("Hello", Assert.Single(output.Documents).Title);
    }

    [Fact]
    public void SlashOutput_WithoutDocumentSupportFallsBackToANote()
    {
        ISlashOutput output = new NoteOnlyOutput();

        output.WriteDocument(PluginDocuments.Copy(new PluginDocument("Hello", [new PluginParagraph("body")])));

        Assert.Equal("Hello" + Environment.NewLine + Environment.NewLine + "body", ((NoteOnlyOutput)output).Note);
    }

    [Fact]
    public void DocumentWritten_CarriesTheDocument()
    {
        var document = new TranscriptDocument("Hello", []);

        Assert.Same(document, new DocumentWritten(document).Document);
    }

    private sealed class DocumentCommand : IPluginCommand
    {
        public string Name => "doc";

        public string Help => "doc";

        public void Execute(string argument, IPluginOutput output) =>
            output.WriteDocument(new PluginDocument("Hello", [new PluginParagraph("body")]));
    }

    private sealed class RecordingOutput : ISlashOutput
    {
        public List<TranscriptDocument> Documents { get; } = [];

        public void WriteNote(string text)
        {
        }

        public void WriteError(string text)
        {
        }

        public void WriteDocument(TranscriptDocument document) => Documents.Add(document);
    }

    private sealed class NoteOnlyOutput : ISlashOutput
    {
        public string? Note { get; private set; }

        public void WriteNote(string text) => Note = text;

        public void WriteError(string text)
        {
        }
    }
}
