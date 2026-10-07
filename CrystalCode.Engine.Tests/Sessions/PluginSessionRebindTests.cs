using System.Diagnostics;

using Crystal;
using Crystal.Chat;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tests.Approvals;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class PluginSessionRebindTests
{
    private static readonly Lazy<string> RecorderPublish = new(PublishRecorder);

    [Fact]
    public async Task Clear_EndsTheOldSessionBeforeStartingTheNewOne()
    {
        using var opened = Open();
        await opened.Session.StartAsync(CancellationToken.None);
        var first = opened.Session.SessionId;

        await opened.Session.SubmitAsync("/clear", CancellationToken.None);

        Assert.NotEqual(first, opened.Session.SessionId);
        Assert.Equal(
            ["start\t" + first, "end\t" + first, "start\t" + opened.Session.SessionId],
            ReadLog(opened.Workspace));
    }

    [Fact]
    public async Task Resume_SameWorkspaceEndsTheOldIdBeforeStartingTheSavedOne()
    {
        using var opened = Open(new ScriptedStreamingClient(TextRound("done")));
        await opened.Session.StartAsync(CancellationToken.None);
        await RunTurnAsync(opened.Session, "hi");
        var first = opened.Session.SessionId;

        await opened.Session.SubmitAsync("/clear", CancellationToken.None);
        var second = opened.Session.SessionId;
        await opened.Session.SubmitAsync("/resume " + first, CancellationToken.None);

        Assert.Equal(first, opened.Session.SessionId);
        Assert.Equal(
            [
                "start\t" + first,
                "end\t" + first,
                "start\t" + second,
                "end\t" + second,
                "start\t" + first
            ],
            ReadLog(opened.Workspace));
    }

    [Fact]
    public async Task Fork_EndsTheSourceSessionBeforeStartingTheBranch()
    {
        using var opened = Open(new ScriptedStreamingClient(TextRound("done")));
        await opened.Session.StartAsync(CancellationToken.None);
        await RunTurnAsync(opened.Session, "hi");
        var source = opened.Session.SessionId;

        await opened.Session.SubmitAsync("/fork", CancellationToken.None);

        Assert.NotEqual(source, opened.Session.SessionId);
        Assert.Equal(
            ["start\t" + source, "end\t" + source, "start\t" + opened.Session.SessionId],
            ReadLog(opened.Workspace));
    }

    [Fact]
    public async Task ChangeDirectory_EndsInTheWorkspaceBeingLeft()
    {
        using var other = new TemporaryWorkspace();
        using var opened = Open();
        await opened.Session.StartAsync(CancellationToken.None);
        var id = opened.Session.SessionId;
        var left = opened.Workspace;

        await opened.Session.SubmitAsync("/cd " + other.Path, CancellationToken.None);

        Assert.Equal(id, opened.Session.SessionId);
        Assert.Equal(["start\t" + id, "end\t" + id], ReadLog(left));
        Assert.Equal(["start\t" + id], ReadLog(other.Path));
    }

    [Fact]
    public async Task Resume_OtherWorkspaceIsOneEndAndOneStart()
    {
        using var other = new TemporaryWorkspace();
        var saved = SessionStore.NewId();
        using var opened = Open(chooser: new ChoosingSessionChooser(saved));
        new SessionStore(opened.Home).Save(
            new SessionDocument
            {
                Id = saved,
                Workspace = new Workspace(other.Path).Root,
                Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "there" }]
            });
        await opened.Session.StartAsync(CancellationToken.None);
        var current = opened.Session.SessionId;

        await opened.Session.SubmitAsync("/resume " + other.Path, CancellationToken.None);

        Assert.Equal(saved, opened.Session.SessionId);
        Assert.Equal(["start\t" + current, "end\t" + current], ReadLog(opened.Workspace));
        Assert.Equal(["start\t" + saved], ReadLog(other.Path));
    }

    private static Opened Open(
        IStreamingChatClient? client = null,
        ISessionChooser? chooser = null)
    {
        var home = new TemporaryHome();
        var workspace = new TemporaryWorkspace();
        InstallRecorder(home.Home);
        var provider = new ProviderDefinition(
            new ProviderName("scripted"),
            ScriptedChatPlugin.Protocol,
            new Uri("http://localhost:11434/"),
            new Dictionary<string, ModelSettings> { ["model"] = new(200_000) });
        var settings = new HarnessSettings(
            provider.Name,
            "model",
            ApprovalMode.Default,
            0.8,
            ProviderCatalog.CreateStarter().Overlay([provider]),
            workspaceTrust: false);
        var plugins = new PluginRegistry();
        plugins.Add(new WorkspaceToolsPlugin());
        plugins.Add(new ScriptedChatPlugin(client ?? new ScriptedStreamingClient()));
        var session = CodingSession.Create(
            settings,
            new SettingsStore(home.Home),
            new CredentialStore(home.Home),
            home.Home,
            workspace.Path,
            new SessionFrontEnd(
                new RecordingSessionObserver(),
                new RecordingApprovalPrompt(ApprovalChoice.Deny),
                new FixedUserPrompt("unused"),
                chooser ?? new DecliningSessionChooser(),
                new ScriptedTrustPrompt(accept: false)),
            plugins);
        return new Opened(home, workspace, session);
    }

    private static async Task RunTurnAsync(CodingSession session, string text)
    {
        var quit = await session.SubmitAsync(text, CancellationToken.None);
        Assert.False(quit);
        if (session.TurnTask is { } turn)
        {
            await turn;
        }

        await session.CompleteTurnAsync(CancellationToken.None);
    }

    private static void InstallRecorder(CrystalHome home)
    {
        var source = RecorderPublish.Value;
        var destination = Path.Combine(home.PluginsDirectory, "Recorder");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        File.WriteAllText(
            Path.Combine(destination, "plugin.json"),
            """
            {
              "assembly": "RecorderPlugin.dll",
              "type": "RecorderPlugin.RecorderPlugin"
            }
            """);
    }

    private static string[] ReadLog(string workspace)
    {
        var path = Path.Combine(new Workspace(workspace).Root, "session-hook.log");
        return File.Exists(path) ? File.ReadAllLines(path) : [];
    }

    private static string PublishRecorder()
    {
        var root = Path.Combine(Path.GetTempPath(), "crystalcode-recorder-plugin");
        var output = Path.Combine(root, "out");
        Directory.CreateDirectory(output);
        var project = Path.Combine(root, "src");
        Directory.CreateDirectory(project);
        var contract = Path.Combine(AppContext.BaseDirectory, "CrystalCode.Plugins.dll");
        Assert.True(File.Exists(contract), contract);
        File.WriteAllText(
            Path.Combine(project, "RecorderPlugin.csproj"),
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <RestorePackagesPath>{Path.Combine(root, "packages")}</RestorePackagesPath>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="CrystalCode.Plugins"><HintPath>{contract}</HintPath></Reference>
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(project, "RecorderPlugin.cs"),
            """
            using CrystalCode.Plugins;
            using CrystalCode.Plugins.Hooks;

            namespace RecorderPlugin;

            public sealed class RecorderPlugin : IPlugin
            {
                public string Name => "Recorder";

                public PluginContribution Contribute() => new(hooks: [new SessionHook()]);
            }

            public sealed class SessionHook : IPluginHook
            {
                public ValueTask OnSessionStartedAsync(
                    PluginSession session,
                    CancellationToken cancellationToken = default)
                {
                    Write("start", session);
                    return ValueTask.CompletedTask;
                }

                public ValueTask OnSessionEndedAsync(
                    PluginSession session,
                    CancellationToken cancellationToken = default)
                {
                    Write("end", session);
                    return ValueTask.CompletedTask;
                }

                private static void Write(string kind, PluginSession session)
                {
                    File.AppendAllText(
                        Path.Combine(session.WorkspaceRoot, "session-hook.log"),
                        kind + "\t" + session.SessionId + "\n");
                }
            }
            """);
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = project,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        start.ArgumentList.Add("publish");
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("Release");
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add(output);
        using var process = Process.Start(start);
        Assert.NotNull(process);
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(2)));
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.ExitCode == 0, stdout + stderr);
        return output;
    }

    private static ChatStreamEvent[] TextRound(string text) =>
    [
        new ChatTextDelta(0, 0, ChatRole.Assistant, text),
        new ChatCandidateCompleted(0, FinishReason.Stop)
    ];

    private sealed class ChoosingSessionChooser(string id) : ISessionChooser
    {
        public Task<string?> ChooseAsync(
            IReadOnlyList<SessionSummary> sessions,
            string? currentId,
            bool listWorkspace,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(id);
    }

    private sealed class Opened(TemporaryHome home, TemporaryWorkspace workspace, CodingSession session) : IDisposable
    {
        public CrystalHome Home => home.Home;

        public string Workspace => workspace.Path;

        public CodingSession Session => session;

        public void Dispose()
        {
            session.Close();
            workspace.Dispose();
            home.Dispose();
        }
    }
}
