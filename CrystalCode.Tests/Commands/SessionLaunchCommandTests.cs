using Spectre.Console;
using Spectre.Console.Cli;

using CrystalCode.Commands;
using CrystalCode.Engine.Home;
using SettingsStore = CrystalCode.Engine.Home.DiskSettingsStore;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Tests.Commands;

public sealed class SessionLaunchCommandTests
{
    [Fact]
    public void Help_ListsSharedFlagsOnInteractiveCommandsOnly()
    {
        var interactive = Help(["--help"]);
        Assert.Contains("--prompt-set", interactive, StringComparison.Ordinal);
        Assert.Contains("--approval", interactive, StringComparison.Ordinal);
        Assert.Contains("--thinking", interactive, StringComparison.Ordinal);
        Assert.Contains("--context", interactive, StringComparison.Ordinal);
        Assert.Contains("--bash-timeout", interactive, StringComparison.Ordinal);
        Assert.Contains("--disable-tool", interactive, StringComparison.Ordinal);
        Assert.Contains("--allow-read", interactive, StringComparison.Ordinal);
        Assert.Contains("--allow-write", interactive, StringComparison.Ordinal);
        Assert.DoesNotContain("--format", interactive, StringComparison.Ordinal);
        Assert.DoesNotContain("--show-thinking", interactive, StringComparison.Ordinal);
        Assert.DoesNotContain("--workspace-trust", interactive, StringComparison.Ordinal);
        Assert.DoesNotContain("--space", interactive, StringComparison.Ordinal);

        var space = Help(["space", "--help"]);
        Assert.Contains("--prompt-set", space, StringComparison.Ordinal);
        Assert.Contains("--disable-tool", space, StringComparison.Ordinal);
        Assert.DoesNotContain("--workspace", space, StringComparison.Ordinal);
        Assert.DoesNotContain("--format", space, StringComparison.Ordinal);

        var run = Help(["run", "--help"]);
        Assert.Contains("--prompt-set", run, StringComparison.Ordinal);
        Assert.Contains("--context", run, StringComparison.Ordinal);
        Assert.Contains("--format", run, StringComparison.Ordinal);
        Assert.Contains("--show-thinking", run, StringComparison.Ordinal);
        Assert.Contains("--workspace-trust", run, StringComparison.Ordinal);
        Assert.Contains("--disable-tool", run, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingPromptSet_ExitsWithoutChangingSettings()
    {
        var home = Directory.CreateTempSubdirectory("crystal-launch-home-").FullName;
        var workspace = Directory.CreateTempSubdirectory("crystal-launch-workspace-").FullName;
        try
        {
            var crystalHome = new CrystalHome(home);
            new DiskSettingsStore(crystalHome).LoadOrCreate();
            var before = File.ReadAllBytes(crystalHome.ConfigPath);

            var code = await RunCommand.RunAsync(
                new RunSettings
                {
                    Home = home,
                    Workspace = workspace,
                    PromptSet = "missing-set",
                    Resume = new FlagValue<string?>(),
                },
                CancellationToken.None);

            Assert.Equal(1, code);
            Assert.Equal(before, File.ReadAllBytes(crystalHome.ConfigPath));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task PlanAndWorkTogether_ExitsWithoutChangingSettings()
    {
        var home = Directory.CreateTempSubdirectory("crystal-launch-home-").FullName;
        try
        {
            var crystalHome = new CrystalHome(home);
            new SettingsStore(crystalHome).LoadOrCreate();
            var before = File.ReadAllBytes(crystalHome.ConfigPath);

            var code = await RunCommand.RunAsync(
                new RunSettings
                {
                    Home = home,
                    Plan = true,
                    Work = true,
                    Resume = new FlagValue<string?>(),
                },
                CancellationToken.None);

            Assert.Equal(1, code);
            Assert.Equal(before, File.ReadAllBytes(crystalHome.ConfigPath));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task ResumeInvalidAccess_ExitsWithoutChangingSessionOrSettings()
    {
        var home = Directory.CreateTempSubdirectory("crystal-launch-home-").FullName;
        var workspace = Directory.CreateTempSubdirectory("crystal-launch-workspace-").FullName;
        try
        {
            var crystalHome = new CrystalHome(home);
            new SettingsStore(crystalHome).LoadOrCreate();
            var before = File.ReadAllBytes(crystalHome.ConfigPath);
            var store = new SessionStore(crystalHome);
            store.Save(new SessionDocument
            {
                Id = "invalidaccess", Workspace = workspace,
                Access = [new DirectoryAccess(workspace, "invalid")],
                Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "hi" }]
            });
            var app = new CommandApp<RunCommand>();

            var code = await app.RunAsync([
                "--home", home, "--workspace", workspace, "--provider", "ollama",
                "--model", "qwen3:8b", "--resume", "invalidaccess"]);

            Assert.Equal(1, code);
            Assert.Equal(before, File.ReadAllBytes(crystalHome.ConfigPath));
            Assert.True(store.TryLoad("invalidaccess", out var document));
            Assert.Equal("invalid", Assert.Single(document.Access).Mode);
        }
        finally
        {
            Directory.Delete(home, recursive: true);
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static string Help(string[] args)
    {
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            Out = new AnsiConsoleOutput(writer),
        });
        var app = new CommandApp<RunCommand>();
        app.Configure(config =>
        {
            config.SetApplicationName("crystal");
            config.ConfigureConsole(console);
            config.AddCommand<TaskRunCommand>("run");
            config.AddCommand<SpaceCommand>("space");
        });
        var code = app.Run(args);
        Assert.Equal(0, code);
        return writer.ToString();
    }
}
