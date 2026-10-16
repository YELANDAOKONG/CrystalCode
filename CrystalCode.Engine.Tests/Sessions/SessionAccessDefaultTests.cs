using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class SessionAccessDefaultTests
{
    [Fact]
    public void NewSession_SeedsWorkspaceDefaults()
    {
        using var extra = new TemporaryWorkspace();
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            prepare: (home, workspace) => Remember(home, workspace, extra.Path, "read"));

        var rule = Assert.Single(headless.ReadSaved().Access);
        Assert.Equal(new Workspace(extra.Path).Root, rule.Path);
        Assert.Equal("read", rule.Mode);
    }

    [Fact]
    public void LaunchFlags_ReplaceDefaultsForThisProcess()
    {
        using var defaults = new TemporaryWorkspace();
        using var flagged = new TemporaryWorkspace();
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            prepare: (home, workspace) => Remember(home, workspace, defaults.Path, "read"),
            launchAccess: [new DirectoryAccess(flagged.Path, "write")]);

        var rule = Assert.Single(headless.ReadSaved().Access);
        Assert.Equal(new Workspace(flagged.Path).Root, rule.Path);
        Assert.Equal("write", rule.Mode);
        Assert.Contains(new Workspace(defaults.Path).Root, File.ReadAllText(headless.Home.AccessPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resume_DoesNotReapplyDefaults()
    {
        using var extra = new TemporaryWorkspace();
        var saved = new SessionDocument
        {
            Access = [],
            Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "hi" }]
        };
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            resume: saved,
            prepare: (home, workspace) => Remember(home, workspace, extra.Path, "read"));

        await headless.Session.SubmitAsync("/access", CancellationToken.None);

        Assert.DoesNotContain(
            headless.Observer.Events.OfType<NoteWritten>(),
            note => note.Text.Contains(new Workspace(extra.Path).Root, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ClearAndFork_ReseedDefaultsWithoutCopyingSessionRules()
    {
        using var defaults = new TemporaryWorkspace();
        using var added = new TemporaryWorkspace();
        var client = new ScriptedStreamingClient(TextRound("done"));
        using var headless = new HeadlessSession(
            client,
            prepare: (home, workspace) => Remember(home, workspace, defaults.Path, "read"));
        await headless.Session.SubmitAsync("/access add " + added.Path + " write", CancellationToken.None);
        await headless.RunTurnAsync("hi");
        var source = headless.Session.SessionId;

        await headless.Session.SubmitAsync("/fork", CancellationToken.None);

        Assert.NotEqual(source, headless.Session.SessionId);
        Assert.Equal(new Workspace(defaults.Path).Root, Assert.Single(headless.ReadSaved().Access).Path);
        Assert.True(new SessionStore(headless.Home).TryLoad(source, out var original));
        Assert.Equal(2, original.Access.Count);

        await headless.Session.SubmitAsync("/access add " + added.Path + " write", CancellationToken.None);
        await headless.Session.SubmitAsync("/clear", CancellationToken.None);

        Assert.Equal(new Workspace(defaults.Path).Root, Assert.Single(headless.ReadSaved().Access).Path);
    }

    [Fact]
    public async Task WorkspaceChange_SeedsTheDestinationAndARefusalKeepsRules()
    {
        using var other = new TemporaryWorkspace();
        using var fromDefault = new TemporaryWorkspace();
        using var toDefault = new TemporaryWorkspace();
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: HeadlessSession.ScriptedSettings().WithWorkspaceTrust(false),
            prepare: (home, workspace) =>
            {
                Remember(home, workspace, fromDefault.Path, "read");
                Remember(home, other.Path, toDefault.Path, "write");
            });

        await headless.Session.SubmitAsync("/cd " + other.Path, CancellationToken.None);

        var moved = Assert.Single(headless.ReadSaved().Access);
        Assert.Equal(new Workspace(toDefault.Path).Root, moved.Path);
        Assert.Equal("write", moved.Mode);
    }

    [Fact]
    public async Task RefusedWorkspaceChange_KeepsTheCurrentRules()
    {
        using var other = new TemporaryWorkspace();
        using var current = new TemporaryWorkspace();
        using var destination = new TemporaryWorkspace();
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            prepare: (home, workspace) =>
            {
                Remember(home, workspace, current.Path, "read");
                Remember(home, other.Path, destination.Path, "write");
            });

        await headless.Session.SubmitAsync("/cd " + other.Path, CancellationToken.None);

        Assert.Contains(
            headless.Observer.Events.OfType<NoteWritten>(),
            note => note.Text == "Staying in " + new Workspace(headless.WorkspacePath).Root);
        Assert.Equal(new Workspace(current.Path).Root, Assert.Single(headless.ReadSaved().Access).Path);
    }

    [Fact]
    public async Task DefaultCommands_UpdateTheFileAndTheCurrentSession()
    {
        using var extra = new TemporaryWorkspace();
        using var sessionOnly = new TemporaryWorkspace();
        using var headless = new HeadlessSession(new ScriptedStreamingClient());

        await headless.Session.SubmitAsync("/access default", CancellationToken.None);
        Assert.Contains(
            headless.Observer.Events.OfType<NoteWritten>(),
            note => note.Text == "No default directories for this workspace.");

        await headless.Session.SubmitAsync(
            "/access default add " + extra.Path + " read",
            CancellationToken.None);
        Assert.Equal("read", Assert.Single(headless.ReadSaved().Access).Mode);
        Assert.Contains(new Workspace(extra.Path).Root, File.ReadAllText(headless.Home.AccessPath), StringComparison.Ordinal);

        await headless.Session.SubmitAsync("/access add " + sessionOnly.Path + " write", CancellationToken.None);
        await headless.Session.SubmitAsync(
            "/access default remove " + sessionOnly.Path,
            CancellationToken.None);
        Assert.Contains(
            headless.Observer.Events.OfType<ErrorWritten>(),
            error => error.Text == "No default access rule for that directory.");
        Assert.Equal(2, headless.ReadSaved().Access.Count);

        await headless.Session.SubmitAsync(
            "/access default remove " + extra.Path,
            CancellationToken.None);
        Assert.Equal(new Workspace(sessionOnly.Path).Root, Assert.Single(headless.ReadSaved().Access).Path);
        Assert.False(File.Exists(headless.Home.AccessPath));
    }

    [Fact]
    public async Task DefaultAdd_IsRejectedDuringATurn()
    {
        var client = new BlockingStreamingClient();
        using var extra = new TemporaryWorkspace();
        using var headless = new HeadlessSession(client);
        await headless.Session.SubmitAsync("wait", CancellationToken.None);
        await client.Started.WaitAsync(TimeSpan.FromSeconds(10));

        await headless.Session.SubmitAsync(
            "/access default add " + extra.Path + " read",
            CancellationToken.None);

        Assert.Contains(
            headless.Observer.Events.OfType<ErrorWritten>(),
            error => error.Text.Contains("Finish the current turn", StringComparison.Ordinal));
        Assert.False(File.Exists(headless.Home.AccessPath));
        headless.Session.TryInterrupt();
        await headless.Session.CompleteTurnAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NewSession_SkipsAMissingDefaultAndStillOpens()
    {
        using var extra = new TemporaryWorkspace();
        var missing = Path.Combine(Path.GetTempPath(), "crystal-missing-" + Guid.NewGuid().ToString("N"));
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            prepare: (home, workspace) =>
            {
                var root = new Workspace(workspace).Root;
                var present = new Workspace(extra.Path).Root;
                home.EnsureCreated();
                File.WriteAllText(
                    home.AccessPath,
                    $$"""
                    {
                      "workspaces": [
                        {
                          "path": {{System.Text.Json.JsonSerializer.Serialize(root)}},
                          "directories": [
                            { "path": {{System.Text.Json.JsonSerializer.Serialize(missing)}}, "mode": "read" },
                            { "path": {{System.Text.Json.JsonSerializer.Serialize(present)}}, "mode": "write" }
                          ]
                        }
                      ]
                    }
                    """);
            });

        Assert.Equal(new Workspace(extra.Path).Root, Assert.Single(headless.ReadSaved().Access).Path);
        Assert.Contains(
            headless.Observer.Events.OfType<ErrorWritten>(),
            error => error.Text.Contains(missing, StringComparison.Ordinal));
        Assert.Contains(missing, File.ReadAllText(headless.Home.AccessPath), StringComparison.Ordinal);
    }

    private static void Remember(CrystalHome home, string workspace, string directory, string mode)
    {
        var store = new WorkspaceAccessStore(home);
        Assert.True(
            store.TrySet(workspace, new DirectoryAccess(new Workspace(directory).Root, mode), out var error),
            error);
    }

    private static IReadOnlyList<ChatStreamEvent> TextRound(string text) =>
        [new ChatTextDelta(0, 0, ChatRole.Assistant, text), new ChatCandidateCompleted(0, FinishReason.Stop)];
}
