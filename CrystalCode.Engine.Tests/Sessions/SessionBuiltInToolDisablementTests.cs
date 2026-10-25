using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class SessionBuiltInToolDisablementTests
{
    [Fact]
    public async Task Disable_RemovesTheToolSavesItWithTheSessionAndLeavesConfigAlone()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/tools builtin session plan disable read", CancellationToken.None);
        await headless.Session.SubmitAsync("/tools", CancellationToken.None);

        var listed = headless.Observer.Events.OfType<ToolsListed>().Last();
        Assert.DoesNotContain(listed.Report.Plan, tool => tool.Name == ReadTool.ToolName);
        Assert.Contains(listed.Report.Work, tool => tool.Name == ReadTool.ToolName);
        var saved = headless.ReadSaved().DisabledBuiltInTools;
        Assert.Equal([ReadTool.ToolName], saved?.Plan);
        Assert.Empty(saved?.Work ?? []);
        Assert.False(File.Exists(headless.Home.ConfigPath)
            && File.ReadAllText(headless.Home.ConfigPath).Contains("disabledBuiltInTools", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Enable_RestoresTheToolAndDropsTheSavedList()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.Session.SubmitAsync("/tools builtin session work disable bash", CancellationToken.None);

        await headless.Session.SubmitAsync("/tools builtin session work enable bash", CancellationToken.None);
        await headless.Session.SubmitAsync("/tools", CancellationToken.None);

        var listed = headless.Observer.Events.OfType<ToolsListed>().Last();
        Assert.Contains(listed.Report.Work, tool => tool.Name == BashTool.ToolName);
        Assert.Null(headless.ReadSaved().DisabledBuiltInTools);
    }

    [Fact]
    public async Task Disable_RejectsAnUnknownName()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/tools builtin session plan disable nope", CancellationToken.None);

        Assert.Contains(
            headless.Observer.Events.OfType<ErrorWritten>(),
            error => error.Text.Contains("nope", StringComparison.Ordinal));
        await headless.Session.SubmitAsync("/tools builtin session show", CancellationToken.None);
        Assert.Contains("Plan None  Work None", headless.Observer.Events.OfType<NoteWritten>().Last().Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Show_WithoutSessionStillListsThePersistentNames()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.Session.SubmitAsync("/tools builtin plan disable grep", CancellationToken.None);
        await headless.Session.SubmitAsync("/tools builtin session work disable bash", CancellationToken.None);

        await headless.Session.SubmitAsync("/tools builtin show", CancellationToken.None);

        var note = headless.Observer.Events.OfType<NoteWritten>().Last().Text;
        Assert.Contains("Persistently disabled", note, StringComparison.Ordinal);
        Assert.Contains("Plan grep", note, StringComparison.Ordinal);
        Assert.DoesNotContain("bash", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Show_ListsOnlySessionNames()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.Session.SubmitAsync("/tools builtin plan disable grep", CancellationToken.None);
        await headless.Session.SubmitAsync("/tools builtin session work disable bash", CancellationToken.None);

        await headless.Session.SubmitAsync("/tools builtin session show", CancellationToken.None);

        var note = headless.Observer.Events.OfType<NoteWritten>().Last().Text;
        Assert.Contains("Work bash", note, StringComparison.Ordinal);
        Assert.DoesNotContain("grep", note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resume_RestoresTheSessionDisablement()
    {
        var saved = new SessionDocument
        {
            Items = TranscriptCodec.Write([new ChatMessage(ChatRole.System, "Saved system")]),
            DisabledBuiltInTools = new BuiltInToolDisablementDocument { Work = [BashTool.ToolName] }
        };
        using var headless = new HeadlessSession(new ScriptedStreamingClient(), resume: saved);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/tools", CancellationToken.None);

        var listed = headless.Observer.Events.OfType<ToolsListed>().Last();
        Assert.DoesNotContain(listed.Report.Work, tool => tool.Name == BashTool.ToolName);
        Assert.Contains(listed.Report.Plan, tool => tool.Name == ReadTool.ToolName);
    }

    [Fact]
    public async Task Resume_IgnoresANameThisBuildDoesNotKnow()
    {
        var saved = new SessionDocument
        {
            Items = TranscriptCodec.Write([new ChatMessage(ChatRole.System, "Saved system")]),
            DisabledBuiltInTools = new BuiltInToolDisablementDocument { Work = ["retired_tool", BashTool.ToolName] }
        };
        using var headless = new HeadlessSession(new ScriptedStreamingClient(), resume: saved);
        await headless.Session.StartAsync(CancellationToken.None);

        await headless.Session.SubmitAsync("/tools", CancellationToken.None);

        var listed = headless.Observer.Events.OfType<ToolsListed>().Last();
        Assert.DoesNotContain(listed.Report.Work, tool => tool.Name == BashTool.ToolName);
    }

    [Theory]
    [InlineData("/clear")]
    [InlineData("/fork")]
    public async Task NewSessionAndFork_StartWithTheToolsEnabled(string command)
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient(TextRound("done")));
        await headless.Session.SubmitAsync("/tools builtin session work disable bash", CancellationToken.None);
        await headless.RunTurnAsync("hi");
        var source = headless.Session.SessionId;

        await headless.Session.SubmitAsync(command, CancellationToken.None);
        await headless.Session.SubmitAsync("/tools", CancellationToken.None);

        var listed = headless.Observer.Events.OfType<ToolsListed>().Last();
        Assert.Contains(listed.Report.Work, tool => tool.Name == BashTool.ToolName);
        Assert.Null(headless.ReadSaved().DisabledBuiltInTools);
        Assert.NotEqual(source, headless.Session.SessionId);
        Assert.True(new SessionStore(headless.Home).TryLoad(source, out var original));
        Assert.Equal([BashTool.ToolName], original.DisabledBuiltInTools?.Work);
    }

    private static IReadOnlyList<ChatStreamEvent> TextRound(string text) =>
        [new ChatTextDelta(0, 0, ChatRole.Assistant, text), new ChatCandidateCompleted(0, FinishReason.Stop)];
}
