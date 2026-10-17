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
        var rule = Assert.Single(saved.Access);
        Assert.Equal(new Workspace(child).Root, rule.Path);
        Assert.Equal("read", rule.Mode);

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

    [Fact]
    public async Task Access_LaunchRetainsCanonicalTargetAndEntryForRevocation()
    {
        using var entries = new TemporaryWorkspace();
        using var original = new TemporaryWorkspace();
        using var other = new TemporaryWorkspace();
        var link = Path.Combine(entries.Path, "reference-link");
        Directory.CreateSymbolicLink(link, other.Path);
        var rule = new DirectoryAccess(new Workspace(original.Path).Root, "read", Path.GetFullPath(link));
        using var headless = new HeadlessSession(new ScriptedStreamingClient(), launchAccess: [rule]);

        Assert.Equal(rule, Assert.Single(headless.ReadSaved().Access));
        Directory.Delete(link);
        await headless.Session.SubmitAsync("/access remove " + link, CancellationToken.None);

        Assert.Empty(headless.ReadSaved().Access);
    }

    [Fact]
    public async Task Access_RemoveDeletedSymlinkAfterResumeRevokesOriginalTarget()
    {
        using var target = new TemporaryWorkspace();
        using var first = new HeadlessSession(new ScriptedStreamingClient());
        var link = Path.Combine(first.WorkspacePath, "reference-link");
        Directory.CreateSymbolicLink(link, target.Path);
        await first.Session.SubmitAsync("/access add " + link + " read", CancellationToken.None);
        var saved = first.ReadSaved();
        Assert.Equal(Path.GetFullPath(link), Assert.Single(saved.Access).SourcePath);
        Directory.Delete(link);
        using var resumed = new HeadlessSession(new ScriptedStreamingClient(), resume: saved);

        await resumed.Session.SubmitAsync("/access remove " + link, CancellationToken.None);

        Assert.Empty(resumed.ReadSaved().Access);
        Assert.Empty(resumed.Observer.Events.OfType<ErrorWritten>());
    }

    [Fact]
    public async Task Access_RemoveRetargetedSymlinkRevokesOriginalRuleOnly()
    {
        using var original = new TemporaryWorkspace();
        using var other = new TemporaryWorkspace();
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        var link = Path.Combine(headless.WorkspacePath, "reference-link");
        Directory.CreateSymbolicLink(link, original.Path);
        await headless.Session.SubmitAsync("/access add " + link + " read", CancellationToken.None);
        await headless.Session.SubmitAsync("/access add " + other.Path + " read", CancellationToken.None);
        Directory.Delete(link);
        Directory.CreateSymbolicLink(link, other.Path);

        await headless.Session.SubmitAsync("/access remove " + link, CancellationToken.None);

        Assert.Equal(new Workspace(other.Path).Root, Assert.Single(headless.ReadSaved().Access).Path);
    }

    [Fact]
    public async Task Access_RemoveInvalidPathReportsPathErrorAndKeepsRules()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.SubmitAsync("/access add . read", CancellationToken.None);

        await headless.Session.SubmitAsync("/access remove invalid\0path", CancellationToken.None);

        Assert.Equal("Path is not valid.", Assert.Single(headless.Observer.Events.OfType<ErrorWritten>()).Text);
        Assert.Single(headless.ReadSaved().Access);
    }

    [Fact]
    public async Task Access_RemoveUnresolvableSymlinkReportsPathError()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        var link = Path.Combine(headless.WorkspacePath, "loop");
        Directory.CreateSymbolicLink(link, link);

        await headless.Session.SubmitAsync("/access remove " + link, CancellationToken.None);

        Assert.Equal("Path is not valid.", Assert.Single(headless.Observer.Events.OfType<ErrorWritten>()).Text);
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
            [JsonChatToolCallDelta(0, 0, "c1", "read", JsonSerializer.Serialize(new { path = Path.Combine(outside.Path, "a.txt") })),
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
    public async Task Access_DirectoriesPlaceholderListsRulesOnceInACustomPrompt()
    {
        using var outside = new TemporaryWorkspace();
        var root = new Workspace(outside.Path).Root;
        var client = new ScriptedStreamingClient(TextRound("done"));
        using var headless = new HeadlessSession(
            client,
            prepare: (home, workspace) =>
            {
                _ = workspace;
                Directory.CreateDirectory(home.PromptsDirectory);
                File.WriteAllText(
                    Path.Combine(home.PromptsDirectory, "work.md"),
                    "rules\n{{directories}}\nend");
            });
        await headless.Session.SubmitAsync("/access add " + outside.Path + " read", CancellationToken.None);

        await headless.RunTurnAsync("hi");

        var system = Assert.Single(
            client.LastRequest!.Items.OfType<ChatMessage>(),
            message => message.Role == ChatRole.System).Text;
        Assert.Contains("rules\n", system, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(system, "- read: " + root));
        Assert.DoesNotContain("{{directories}}", system, StringComparison.Ordinal);
        Assert.DoesNotContain("## Workspace instructions", system, StringComparison.Ordinal);
        Assert.EndsWith("\nend", system, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Access_DirectoriesPlaceholderIsEmptyWithoutAddedRules()
    {
        var client = new ScriptedStreamingClient(TextRound("done"));
        using var headless = new HeadlessSession(
            client,
            prepare: (home, workspace) =>
            {
                _ = workspace;
                Directory.CreateDirectory(home.PromptsDirectory);
                File.WriteAllText(Path.Combine(home.PromptsDirectory, "work.md"), "mark {{directories}} end");
            });

        await headless.RunTurnAsync("hi");

        var system = Assert.Single(
            client.LastRequest!.Items.OfType<ChatMessage>(),
            message => message.Role == ChatRole.System).Text;
        Assert.Equal("mark  end", system);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        var start = 0;
        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }

    [Fact]
    public async Task Access_ResumeInvalidRulesReportsErrorAndKeepsCurrentSession()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.SubmitAsync("/access add . read", CancellationToken.None);
        var currentId = headless.Session.SessionId;
        var store = new SessionStore(headless.Home);
        store.Save(new SessionDocument
        {
            Id = "invalidaccess", Workspace = headless.WorkspacePath,
            Access = [new DirectoryAccess("relative", "read")],
            Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "hi" }]
        });

        await headless.Session.SubmitAsync("/resume invalidaccess", CancellationToken.None);

        Assert.Equal(currentId, headless.Session.SessionId);
        Assert.Equal("read", Assert.Single(headless.ReadSaved().Access).Mode);
        Assert.Equal("Saved session directory access is invalid.",
            Assert.Single(headless.Observer.Events.OfType<ErrorWritten>()).Text);
        Assert.True(store.TryLoad("invalidaccess", out var invalid));
        Assert.Equal("relative", Assert.Single(invalid.Access).Path);
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
