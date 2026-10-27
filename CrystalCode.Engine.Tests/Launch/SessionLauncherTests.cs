using CrystalCode.Engine.Home;
using CrystalCode.Engine.Launch;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Sessions;
using CrystalCode.Engine.Tests.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Launch;

public sealed class SessionLauncherTests
{
    [Fact]
    public async Task Prompt_AsksForTrustAndRemembersYes()
    {
        using var fixture = new LaunchFixture();
        var trust = new ScriptedTrustPrompt(accept: true);

        var result = await SessionLauncher.PrepareAsync(fixture.Request(), trust, null, CancellationToken.None);

        Assert.NotNull(result.Launch);
        Assert.Equal(1, trust.Asked);
        Assert.True(new WorkspaceTrustStore(fixture.Home.Home).Contains(fixture.Workspace.Path));
        fixture.AssertSettingsUnchanged();
    }

    [Fact]
    public async Task Prompt_DeclinedTrustIsNotRecorded()
    {
        using var fixture = new LaunchFixture();
        var trust = new ScriptedTrustPrompt(accept: false);

        var result = await SessionLauncher.PrepareAsync(fixture.Request(), trust, null, CancellationToken.None);

        Assert.Null(result.Launch);
        Assert.Equal(SessionLaunchFailureKind.Declined, result.Failure);
        Assert.False(new WorkspaceTrustStore(fixture.Home.Home).Contains(fixture.Workspace.Path));
    }

    [Fact]
    public async Task Prompt_WithoutAnOperatorReportsTheQuestion()
    {
        using var fixture = new LaunchFixture();

        var result = await SessionLauncher.PrepareAsync(fixture.Request(), null, null, CancellationToken.None);

        Assert.Equal(SessionLaunchFailureKind.TrustNeedsOperator, result.Failure);
    }

    [Fact]
    public async Task Require_RefusesAnUntrustedWorkspaceWithoutAsking()
    {
        using var fixture = new LaunchFixture();
        var trust = new ScriptedTrustPrompt(accept: true);

        var result = await SessionLauncher.PrepareAsync(
            fixture.Request() with { Trust = SessionTrustCheck.Require },
            trust,
            null,
            CancellationToken.None);

        Assert.Equal(SessionLaunchFailureKind.Untrusted, result.Failure);
        Assert.Equal(SessionLauncher.UntrustedWorkspace, result.Error);
        Assert.Equal(0, trust.Asked);
    }

    [Fact]
    public async Task Skip_OpensWithoutRecordingTrust()
    {
        using var fixture = new LaunchFixture();

        var result = await SessionLauncher.PrepareAsync(
            fixture.Request() with { Trust = SessionTrustCheck.Skip },
            null,
            null,
            CancellationToken.None);

        Assert.NotNull(result.Launch);
        Assert.False(new WorkspaceTrustStore(fixture.Home.Home).Contains(fixture.Workspace.Path));
    }

    [Fact]
    public async Task Resume_UsesTheChooserAndLoadsTheChosenSession()
    {
        using var fixture = new LaunchFixture();
        fixture.Trust();
        var id = fixture.SaveSession();
        var chooser = new ScriptedSessionChooser(id);

        var result = await SessionLauncher.PrepareAsync(
            fixture.Request() with { Resume = true },
            null,
            chooser,
            CancellationToken.None);

        Assert.Equal(1, chooser.Calls);
        Assert.False(chooser.ListWorkspace);
        Assert.Equal(id, result.Launch?.Resume?.Id);
    }

    [Fact]
    public async Task Resume_WithoutAChooserReportsTheQuestion()
    {
        using var fixture = new LaunchFixture();
        fixture.Trust();
        fixture.SaveSession();

        var result = await SessionLauncher.PrepareAsync(
            fixture.Request() with { Resume = true },
            null,
            null,
            CancellationToken.None);

        Assert.Equal(SessionLaunchFailureKind.ResumeNeedsOperator, result.Failure);
    }

    [Fact]
    public async Task Fork_OpensABranchUnderANewIdAndLeavesTheSourceAlone()
    {
        using var fixture = new LaunchFixture();
        fixture.Trust();
        var id = fixture.SaveSession();
        var store = new SessionStore(fixture.Home.Home);
        var before = File.ReadAllBytes(Path.Combine(fixture.Home.Home.SessionsDirectory, id + ".json"));

        var result = await SessionLauncher.PrepareAsync(
            fixture.Request() with { Resume = true, ResumeTarget = id, Fork = true },
            null,
            null,
            CancellationToken.None);

        var launch = Assert.IsType<SessionLaunch>(result.Launch);
        Assert.True(launch.Forked);
        Assert.NotEqual(id, launch.Resume?.Id);
        Assert.Equal("hello", Assert.Single(launch.Resume!.Items).Text);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(fixture.Home.Home.SessionsDirectory, id + ".json")));
        Assert.True(store.TryLoad(id, out _));
    }

    [Fact]
    public async Task Resume_DeclinedChoiceStopsTheLaunch()
    {
        using var fixture = new LaunchFixture();
        fixture.Trust();
        fixture.SaveSession();

        var result = await SessionLauncher.PrepareAsync(
            fixture.Request() with { Resume = true },
            null,
            new ScriptedSessionChooser(null),
            CancellationToken.None);

        Assert.Equal(SessionLaunchFailureKind.Declined, result.Failure);
    }

    [Fact]
    public async Task Space_CannotBeCombinedWithAWorkspace()
    {
        using var fixture = new LaunchFixture();

        var result = await SessionLauncher.PrepareAsync(
            fixture.Request() with { Space = true },
            null,
            null,
            CancellationToken.None);

        Assert.Equal(SessionLaunchFailureKind.Invalid, result.Failure);
        Assert.Equal(SessionLauncher.SpaceWithWorkspace, result.Error);
    }

    [Fact]
    public async Task PersistSelection_ControlsWhetherTheLaunchModelMaySave()
    {
        using var fixture = new LaunchFixture();
        fixture.Trust();

        var kept = await SessionLauncher.PrepareAsync(fixture.Request(), null, null, CancellationToken.None);
        var dropped = await SessionLauncher.PrepareAsync(
            fixture.Request() with { PersistSelection = false },
            null,
            null,
            CancellationToken.None);

        Assert.Equal("qwen3:8b", kept.Launch?.Persisted.Model);
        Assert.NotEqual("qwen3:8b", dropped.Launch?.Persisted.Model);
        Assert.Equal("qwen3:8b", dropped.Launch?.Settings.Model);
        fixture.AssertSettingsUnchanged();
    }

    [Fact]
    public async Task Mode_MustNameAnEnabledMode()
    {
        using var fixture = new LaunchFixture();
        fixture.Trust();

        var result = await SessionLauncher.PrepareAsync(
            fixture.Request(fixture.Options() with { Mode = "missing" }),
            null,
            null,
            CancellationToken.None);

        Assert.Equal(SessionLaunchFailureKind.Invalid, result.Failure);
        Assert.Contains("Mode not found", result.Error, StringComparison.Ordinal);
    }

    private sealed class LaunchFixture : IDisposable
    {
        private readonly byte[] _configBefore;

        public LaunchFixture()
        {
            new DiskSettingsStore(Home.Home).LoadOrCreate();
            _configBefore = File.ReadAllBytes(Home.Home.ConfigPath);
        }

        public TemporaryHome Home { get; } = new();

        public TemporaryWorkspace Workspace { get; } = new();

        public SessionLaunchOptions Options() =>
            new() { Home = Home.Root, Provider = "ollama", Model = "qwen3:8b" };

        public SessionLaunchRequest Request(SessionLaunchOptions? options = null) =>
            new(options ?? Options(), Workspace.Path, Workspace.Path);

        public void Trust() => new WorkspaceTrustStore(Home.Home).Remember(Workspace.Path);

        public string SaveSession()
        {
            var id = SessionStore.NewId();
            new SessionStore(Home.Home).Save(new SessionDocument
            {
                Id = id,
                Workspace = new CrystalCode.Engine.Tools.Workspace(Workspace.Path).Root,
                Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "hello" }]
            });
            return id;
        }

        public void AssertSettingsUnchanged() =>
            Assert.Equal(_configBefore, File.ReadAllBytes(Home.Home.ConfigPath));

        public void Dispose()
        {
            Workspace.Dispose();
            Home.Dispose();
        }
    }
}
