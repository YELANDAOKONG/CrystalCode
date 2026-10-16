using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class SessionAccessTests
{
    [Fact]
    public async Task Access_AddUpdateRemovePersistsCanonicalRules()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        var child = Directory.CreateDirectory(Path.Combine(headless.WorkspacePath, "reference files")).FullName;
        await headless.Session.SubmitAsync("/access add \"" + child + "\" read", CancellationToken.None);
        var saved = headless.ReadSaved();
        Assert.Equal(new DirectoryAccess(new Workspace(child).Root, "read"), Assert.Single(saved.Access));

        await headless.Session.SubmitAsync("/access add \"" + child + "\" write", CancellationToken.None);
        Assert.Equal("write", Assert.Single(headless.ReadSaved().Access).Mode);
        await headless.Session.SubmitAsync("/access", CancellationToken.None);
        Assert.Contains(headless.Observer.Events.OfType<NoteWritten>(), note => note.Text.Contains(child, StringComparison.Ordinal));

        await headless.Session.SubmitAsync("/access remove \"" + child + "\"", CancellationToken.None);
        Assert.Empty(headless.ReadSaved().Access);
    }

    [Fact]
    public async Task Access_WorkspaceListingShowsEffectiveReadOnlyMode()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.SubmitAsync("/access add . read", CancellationToken.None);
        await headless.Session.SubmitAsync("/access", CancellationToken.None);

        Assert.Contains(headless.Observer.Events.OfType<NoteWritten>(), note =>
            note.Text == "Workspace  read  " + new Workspace(headless.WorkspacePath).Root);
    }

    [Fact]
    public async Task Access_RemoveMissingDirectoryStillRevokesRule()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        var child = Directory.CreateDirectory(Path.Combine(headless.WorkspacePath, "reference")).FullName;
        await headless.Session.SubmitAsync("/access add " + child + " read", CancellationToken.None);
        Directory.Delete(child);
        await headless.Session.SubmitAsync("/access remove " + child, CancellationToken.None);

        Assert.Empty(headless.ReadSaved().Access);
    }

    [Theory]
    [InlineData("/clear")]
    [InlineData("/fork")]
    [InlineData("/space")]
    public async Task Access_NewSessionForkAndWorkspaceSwitchClearRules(string command)
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient(TextRound("done")));
        Assert.True(headless.Session.TrySetDirectoryAccess(headless.WorkspacePath, "read", out _));
        await headless.RunTurnAsync("hi");
        var oldId = headless.Session.SessionId;
        await headless.Session.SubmitAsync(command, CancellationToken.None);
        headless.Session.Close();

        Assert.Empty(headless.ReadSaved().Access);
        if (command == "/fork")
        {
            Assert.NotEqual(oldId, headless.Session.SessionId);
            Assert.True(new SessionStore(headless.Home).TryLoad(oldId, out var original));
            Assert.Single(original.Access);
        }
    }

    [Fact]
    public async Task Access_ResumeRestoresRulesAndReadsOutsideWithoutApproval()
    {
        using var outside = new TemporaryWorkspace();
        await File.WriteAllTextAsync(Path.Combine(outside.Path, "a.txt"), "reference text");
        var client = new ScriptedStreamingClient(
            [new ChatToolCallDelta(0, 0, "c1", "read", JsonSerializer.Serialize(new { path = Path.Combine(outside.Path, "a.txt") })),
                new ChatCandidateCompleted(0, FinishReason.ToolCalls)],
            TextRound("done"));
        var saved = new SessionDocument
        {
            Items = TranscriptCodec.Write([new ChatMessage(ChatRole.System, "Saved system")]),
            Access = [new DirectoryAccess(new Workspace(outside.Path).Root, "read")]
        };
        using var headless = new HeadlessSession(client, resume: saved);
        await headless.RunTurnAsync("read the reference");

        Assert.Equal(0, headless.Approvals.Count);
        Assert.Single(headless.ReadSaved().Access);
        Assert.Contains(headless.Observer.Events.OfType<ToolResultsReceived>(), result =>
            result.Results.Any(item => item.Text.Contains("reference text", StringComparison.Ordinal)));
        Assert.Contains(client.LastRequest!.Items.OfType<ChatMessage>(), message =>
            message.Role == ChatRole.System && message.Text.Contains(outside.Path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Access_InvalidModeOrCredentialDirectoryDoesNotChangeRules()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        var credentials = Directory.CreateDirectory(Path.Combine(headless.WorkspacePath, ".ssh")).FullName;
        Assert.False(headless.Session.TrySetDirectoryAccess(credentials, "read", out _));
        Assert.False(headless.Session.TrySetDirectoryAccess(headless.WorkspacePath, "readWrite", out _));
        await headless.Session.SubmitAsync("/access add", CancellationToken.None);
        Assert.NotEmpty(headless.Observer.Events.OfType<ErrorWritten>());
    }

    [Fact]
    public async Task Access_ChangingRulesDuringTurnIsRejected()
    {
        var client = new BlockingStreamingClient();
        using var headless = new HeadlessSession(client);
        await headless.Session.SubmitAsync("wait", CancellationToken.None);
        await client.Started.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(headless.Session.TrySetDirectoryAccess(headless.WorkspacePath, "read", out _));
        await headless.Session.SubmitAsync("/access add . read", CancellationToken.None);
        Assert.Contains(headless.Observer.Events.OfType<ErrorWritten>(), error => error.Text.Contains("Finish the current turn", StringComparison.Ordinal));
        headless.Session.TryInterrupt();
        await headless.Session.CompleteTurnAsync(CancellationToken.None);
        Assert.Empty(headless.ReadSaved().Access);
    }

    private static IReadOnlyList<ChatStreamEvent> TextRound(string text) =>
        [new ChatTextDelta(0, 0, ChatRole.Assistant, text), new ChatCandidateCompleted(0, FinishReason.Stop)];
}
