using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Mcp;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

/// <summary>
/// Engine support an editor front end needs: session MCP servers, additional
/// roots, attaching an image file, the option catalog, and report text.
/// </summary>
public sealed class FrontEndSessionInputTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public void SessionServers_ReplaceConfiguredOnesByName()
    {
        var configured = new[]
        {
            new McpServerDefinition("alpha", McpServerSource.Home, McpTransportKind.Stdio, command: "/bin/a"),
            new McpServerDefinition("beta", McpServerSource.Project, McpTransportKind.Stdio, command: "/bin/b")
        };
        var session = new[]
        {
            new McpServerDefinition("beta", McpServerSource.Session, McpTransportKind.StreamableHttp, url: "https://example.invalid/"),
            new McpServerDefinition("gamma", McpServerSource.Session, McpTransportKind.Stdio, command: "/bin/g")
        };

        var merged = McpServerDiscovery.WithSession(configured, session);

        Assert.Equal(["alpha", "beta", "gamma"], merged.Select(static server => server.Name));
        Assert.Equal(McpServerSource.Session, merged[1].Source);
        Assert.Throws<ArgumentException>(() => McpServerDiscovery.WithSession(configured, configured));
    }

    [Fact]
    public async Task SessionServers_AreConnectedAndNeverSaved()
    {
        using var missing = new TemporaryWorkspace();
        var command = Path.Combine(missing.Path, "not-a-server");
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(TextRound("done")),
            settings: HeadlessSession.ScriptedSettings().WithMcp(true),
            mcpServers: [new McpServerDefinition(
                "editor",
                McpServerSource.Session,
                McpTransportKind.Stdio,
                command: command,
                arguments: ["--token", "sk-session-secret"],
                environment: new Dictionary<string, string> { ["TOKEN"] = "sk-env-secret" })]);
        await headless.Session.StartAsync(CancellationToken.None);
        await headless.RunTurnAsync("hi");
        await headless.Session.SubmitAsync("/mcp", CancellationToken.None);

        var text = string.Join("\n", headless.Observer.Events.OfType<NoteWritten>().Select(static note => note.Text));
        Assert.Contains("editor  Session", text, StringComparison.Ordinal);
        var all = string.Join("\n", headless.Observer.Events.Select(static e => e.ToString()));
        Assert.DoesNotContain("sk-session-secret", text + all, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-env-secret", text + all, StringComparison.Ordinal);
        var saved = File.ReadAllText(Path.Combine(headless.Home.SessionsDirectory, headless.Session.SessionId + ".json"));
        Assert.DoesNotContain("editor", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-session-secret", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void AdditionalRoots_MustBeAbsoluteExistingAndOutside()
    {
        using var workspace = new TemporaryWorkspace();
        using var outside = new TemporaryWorkspace();
        var inside = Directory.CreateDirectory(Path.Combine(workspace.Path, "inner")).FullName;

        Assert.True(SessionLaunchAccess.TryReadRoots([outside.Path, outside.Path], workspace.Path, out var roots, out var error), error);
        Assert.Equal(new Workspace(outside.Path).Root, Assert.Single(roots).Path);
        Assert.Equal("write", roots[0].Mode);
        Assert.False(SessionLaunchAccess.TryReadRoots(["relative"], workspace.Path, out _, out error));
        Assert.Contains("absolute", error, StringComparison.Ordinal);
        Assert.False(SessionLaunchAccess.TryReadRoots([inside], workspace.Path, out _, out error));
        Assert.Contains("outside the workspace", error, StringComparison.Ordinal);
        Assert.False(SessionLaunchAccess.TryReadRoots([Path.Combine(outside.Path, "missing")], workspace.Path, out _, out error));
        Assert.Contains("not found", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AdditionalRoots_ReplaceOutsideDefaultsAndKeepNarrowingRules()
    {
        using var root = new TemporaryWorkspace();
        using var other = new TemporaryWorkspace();
        string? narrowing = null;
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            prepare: (home, workspace) =>
            {
                narrowing = Directory.CreateDirectory(Path.Combine(workspace, "docs")).FullName;
                var store = new WorkspaceAccessStore(home);
                Assert.True(store.TrySet(workspace, new DirectoryAccess(new Workspace(narrowing).Root, "read"), out var error), error);
                Assert.True(store.TrySet(workspace, new DirectoryAccess(new Workspace(other.Path).Root, "write"), out error), error);
            },
            additionalRoots: [new DirectoryAccess(new Workspace(root.Path).Root, "write")]);

        var access = headless.ReadSaved().Access;

        Assert.Equal(2, access.Count);
        Assert.Contains(access, rule => rule.Path == new Workspace(root.Path).Root && rule.Mode == "write");
        Assert.Contains(access, rule => rule.Path == new Workspace(narrowing!).Root && rule.Mode == "read");
        Assert.DoesNotContain(access, rule => rule.Path == new Workspace(other.Path).Root);
        Assert.Contains(new Workspace(other.Path).Root.Replace("\\", "\\\\"), File.ReadAllText(headless.Home.AccessPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryAttachImageFile_UsesTheAttachChecksAndKeepsTheImageInTheDraft()
    {
        using var headless = new HeadlessSession(
            new ScriptedStreamingClient(),
            settings: VisionSettings(),
            images: new ChildVisionClient());
        await headless.Session.StartAsync(CancellationToken.None);
        var path = Path.Combine(headless.WorkspacePath, "shot.png");
        File.WriteAllBytes(path, Png);
        File.WriteAllText(Path.Combine(headless.WorkspacePath, "note.txt"), "not an image");
        using var outside = new TemporaryWorkspace();
        File.WriteAllBytes(Path.Combine(outside.Path, "away.png"), Png);

        Assert.True(headless.Session.TryAttachImageFile(path, out var image, out var error), error);
        Assert.False(headless.Session.TryAttachImageFile(Path.Combine(headless.WorkspacePath, "note.txt"), out _, out var invalid));
        Assert.False(headless.Session.TryAttachImageFile(Path.Combine(outside.Path, "away.png"), out _, out var away));

        Assert.StartsWith(ImageMarkerText.Prefix.ToString(), image!.TrustedMarker, StringComparison.Ordinal);
        Assert.True(headless.Session.TryGetImage(image.Number, out _));
        Assert.Contains("Image attachment failed", invalid, StringComparison.Ordinal);
        Assert.NotEmpty(away);
        Assert.DoesNotContain(headless.Observer.Events.OfType<NoteWritten>(), static note => note.Text.StartsWith("Attached", StringComparison.Ordinal));
        headless.Session.NotifyDraftChanged(string.Empty);
        Assert.False(headless.Session.TryGetImage(image.Number, out _));
    }

    [Fact]
    public async Task TryAttachImageFile_OnATextModel_IsRefused()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);
        var path = Path.Combine(headless.WorkspacePath, "shot.png");
        File.WriteAllBytes(path, Png);

        Assert.False(headless.Session.TryAttachImageFile(path, out _, out var error));
        Assert.Contains("do not support image input", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Options_ArePublishedAtStartAndAfterAChange()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);

        var initial = headless.Observer.Events.OfType<SessionOptionsChanged>().Last().Options;
        Assert.Equal("work", initial.Single(static option => option.Id == "mode").Current);
        var approval = initial.Single(static option => option.Id == "approval");
        Assert.All(approval.Values, static value => Assert.StartsWith("/approval ", value.Command, StringComparison.Ordinal));
        Assert.True(initial.Single(static option => option.Id == "agents").IsSwitch);
        var count = headless.Observer.Events.OfType<SessionOptionsChanged>().Count();

        await headless.Session.SubmitAsync(approval.Values.Single(static value => value.Value == "edit").Command, CancellationToken.None);
        await headless.Session.SubmitAsync("/plan", CancellationToken.None);
        await headless.Session.SubmitAsync("/compact micro on", CancellationToken.None);

        var latest = headless.Observer.Events.OfType<SessionOptionsChanged>().Last().Options;
        Assert.Equal("edit", latest.Single(static option => option.Id == "approval").Current);
        Assert.Equal("plan", latest.Single(static option => option.Id == "mode").Current);
        Assert.Equal("on", latest.Single(static option => option.Id == "micro_compaction").Current);
        Assert.True(headless.Observer.Events.OfType<SessionOptionsChanged>().Count() >= count + 3);
    }

    [Fact]
    public async Task Options_AreNotRepublishedWhenNothingChanged()
    {
        using var headless = new HeadlessSession(new ScriptedStreamingClient());
        await headless.Session.StartAsync(CancellationToken.None);
        var count = headless.Observer.Events.OfType<SessionOptionsChanged>().Count();

        await headless.Session.SubmitAsync("/status", CancellationToken.None);
        await headless.Session.SubmitAsync("/tokens " + (HeadlessSession.ScriptedSettings().EstimatedTokens ? "on" : "off"), CancellationToken.None);

        Assert.Equal(count, headless.Observer.Events.OfType<SessionOptionsChanged>().Count());
    }

    [Fact]
    public void TierAndSpeed_ListUnsetWithoutACommand()
    {
        var provider = new ProviderDefinition(
            new ProviderName("scripted"),
            ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings> { ["model"] = new(200_000, serviceTiers: ["flex"], speeds: ["fast"]) });
        var settings = new HarnessSettings(provider.Name, "model", ApprovalMode.Default, 0.8, new ProviderCatalog([provider]));

        var options = SessionOptionCatalog.Build(settings, "work", [], ThinkingSelection.Default, ApprovalMode.Default, "default", ["default"]);

        var tier = options.Single(static option => option.Id == "tier");
        Assert.Equal("unset", tier.Current);
        Assert.Equal(string.Empty, tier.Values[0].Command);
        Assert.Equal("/tier flex", tier.Values[1].Command);
        Assert.Equal("/speed fast", options.Single(static option => option.Id == "speed").Values[1].Command);
        Assert.Equal("/model scripted model", options.Single(static option => option.Id == "model").Values.Single().Command);
    }

    [Fact]
    public void HelpAndStatsText_ArePlain()
    {
        var help = HelpText.Format([new SlashCompletion("deploy", "Ship it", ["deploy"])]);
        var stats = StatsText.Format(
            new SessionStatsReport(2, 3, 4, 5, 10, 20, 0, 30, 15, 15, 7, null, null, [new SessionToolCount("read", 4)]),
            allWorkspaces: false);

        Assert.Contains("/help  /h", help, StringComparison.Ordinal);
        Assert.Contains("/deploy", help, StringComparison.Ordinal);
        Assert.Contains("Ship it", help, StringComparison.Ordinal);
        Assert.Contains("This workspace", stats, StringComparison.Ordinal);
        Assert.Contains("read", stats, StringComparison.Ordinal);
        Assert.DoesNotContain("[", stats, StringComparison.Ordinal);
    }

    private static HarnessSettings VisionSettings()
    {
        var provider = new ProviderDefinition(
            new ProviderName("scripted"),
            ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings> { ["model"] = new(200_000, imageInput: true) });
        return new HarnessSettings(provider.Name, "model", ApprovalMode.Default, 0.8, new ProviderCatalog([provider]));
    }

    private static ChatStreamEvent[] TextRound(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];
}
