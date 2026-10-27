using System.Text;
using System.Text.Json;

using Spectre.Console;
using Spectre.Console.Cli;

using CrystalCode.Commands;
using CrystalCode.Engine.Plugins;

using Xunit;

namespace CrystalCode.Tests.Commands;

public sealed class BridgeCommandTests
{
    [Fact]
    public void Help_ListsSessionAndWorkspaceOptionsWithoutASecretFlag()
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
            config.AddCommand<BridgeCommand>("bridge");
        });

        Assert.Equal(0, app.Run(["bridge", "--help"]));
        var help = writer.ToString();
        Assert.Contains("--workspace", help, StringComparison.Ordinal);
        Assert.Contains("--space", help, StringComparison.Ordinal);
        Assert.Contains("--prompt-set", help, StringComparison.Ordinal);
        Assert.DoesNotContain("--token", help, StringComparison.Ordinal);
        Assert.DoesNotContain("--api-key", help, StringComparison.Ordinal);
        Assert.DoesNotContain("--resume", help, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpaceWithWorkspace_IsRefusedBeforeTheProtocolStarts()
    {
        var error = new StringWriter();
        using var output = new MemoryStream();

        var code = await BridgeCommand.RunAsync(
            new BridgeSettings { Space = true, Workspace = "/tmp" },
            new MemoryStream(),
            output,
            error,
            new PluginRegistry(),
            CancellationToken.None);

        Assert.Equal(1, code);
        Assert.Equal(0, output.Length);
        Assert.Contains("--space", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EndOfInput_ExitsWithZeroAndWritesOnlyProtocolLines()
    {
        var input = new MemoryStream(Encoding.UTF8.GetBytes(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1}}""" + "\n"));
        using var output = new MemoryStream();

        var code = await BridgeCommand.RunAsync(
            new BridgeSettings(),
            input,
            output,
            new StringWriter(),
            new PluginRegistry(),
            CancellationToken.None);

        Assert.Equal(0, code);
        var line = Assert.Single(Encoding.UTF8.GetString(output.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(1, document.RootElement.GetProperty("result").GetProperty("protocolVersion").GetInt32());
    }

    [Fact]
    public void Version_IsShortOrDev()
    {
        var version = BridgeCommand.Version();

        Assert.True(version == "dev" || version.Length == 12, version);
    }
}
