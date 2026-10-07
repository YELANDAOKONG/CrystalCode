using Spectre.Console;
using Spectre.Console.Cli;

using CrystalCode.Commands;
using CrystalCode.Engine.Home;

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
        Assert.Contains("--bash-timeout", interactive, StringComparison.Ordinal);
        Assert.DoesNotContain("--format", interactive, StringComparison.Ordinal);
        Assert.DoesNotContain("--show-thinking", interactive, StringComparison.Ordinal);
        Assert.DoesNotContain("--workspace-trust", interactive, StringComparison.Ordinal);
        Assert.DoesNotContain("--space", interactive, StringComparison.Ordinal);

        var space = Help(["space", "--help"]);
        Assert.Contains("--prompt-set", space, StringComparison.Ordinal);
        Assert.DoesNotContain("--workspace", space, StringComparison.Ordinal);
        Assert.DoesNotContain("--format", space, StringComparison.Ordinal);

        var run = Help(["run", "--help"]);
        Assert.Contains("--prompt-set", run, StringComparison.Ordinal);
        Assert.Contains("--format", run, StringComparison.Ordinal);
        Assert.Contains("--show-thinking", run, StringComparison.Ordinal);
        Assert.Contains("--workspace-trust", run, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingPromptSet_ExitsWithoutChangingSettings()
    {
        var home = Directory.CreateTempSubdirectory("crystal-launch-home-").FullName;
        var workspace = Directory.CreateTempSubdirectory("crystal-launch-workspace-").FullName;
        try
        {
            var crystalHome = new CrystalHome(home);
            new SettingsStore(crystalHome).LoadOrCreate();
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
