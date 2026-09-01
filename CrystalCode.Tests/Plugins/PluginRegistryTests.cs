using CrystalCode.Configuration;
using CrystalCode.Plugins;
using CrystalCode.Plugins.Interfaces;
using CrystalCode.Tests.Sessions;
using CrystalCode.Tests.Tools;
using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Tests.Plugins;

public sealed class PluginRegistryTests
{
    [Fact]
    public void CreateBuiltIn_RegistersWorkspaceToolsAndVendorClients()
    {
        var registry = PluginRegistry.CreateBuiltIn();

        Assert.Contains(registry.Tools, tool => tool.Name == ReadTool.ToolName);
        Assert.Contains(registry.Tools, tool => tool.Name == WriteTool.ToolName);
        Assert.Equal(4, registry.Clients.Count);
        Assert.Contains(registry.Clients, client => client.CanCreate(ProviderProtocol.DeepSeek));
        Assert.Contains(registry.Clients, client => client.CanCreate(ProviderProtocol.OpenAI));
        Assert.Contains(registry.Clients, client => client.CanCreate(ProviderProtocol.Responses));
        Assert.Contains(registry.Clients, client => client.CanCreate(ProviderProtocol.Anthropic));
    }

    [Fact]
    public void CreateTools_AddsPluginToolToWorkOnly()
    {
        using var workspace = new TemporaryWorkspace();
        var registry = PluginRegistry.CreateBuiltIn();
        registry.Add(new EchoPlugin());

        var plan = WorkspaceCatalog.CreatePlan(
            new Workspace(workspace.Path),
            new TodoList(),
            new FixedUserPrompt("ok"),
            registry);
        var work = WorkspaceCatalog.CreateWork(
            new Workspace(workspace.Path),
            new TodoList(),
            new FixedUserPrompt("ok"),
            registry);

        Assert.Null(plan.Find("echo"));
        Assert.NotNull(work.Find("echo"));
    }

    [Fact]
    public void Add_ThrowsOnDuplicateToolName()
    {
        var registry = PluginRegistry.CreateBuiltIn();

        var exception = Assert.Throws<InvalidOperationException>(
            () => registry.Add(new DuplicateReadPlugin()));

        Assert.Contains("read", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FindCommand_ExecutesRegisteredSlashVerb()
    {
        var registry = new PluginRegistry();
        registry.Add(new NotePlugin());
        var output = new RecordingOutput();

        var command = registry.FindCommand("note");
        Assert.NotNull(command);
        command.Execute("hello", output);

        Assert.Equal("hello", output.LastNote);
    }

    [Fact]
    public void TryExecute_RunsRegisteredSlashVerb()
    {
        var registry = new PluginRegistry();
        registry.Add(new NotePlugin());
        var output = new RecordingOutput();

        Assert.True(registry.TryExecute("/note hello", output));
        Assert.Equal("hello", output.LastNote);
        Assert.False(registry.TryExecute("note", output));
        Assert.False(registry.TryExecute("/missing", output));
    }

    private sealed class EchoPlugin : IPlugin
    {
        public string Name => "echo";

        public PluginContribution Contribute() =>
            new(
                tools:
                [
                    new FactoryToolContribution(
                        "echo",
                        false,
                        (_, _, _) => new EchoTool())
                ]);
    }

    private sealed class DuplicateReadPlugin : IPlugin
    {
        public string Name => "dup";

        public PluginContribution Contribute() =>
            new(
                tools:
                [
                    new FactoryToolContribution(
                        ReadTool.ToolName,
                        true,
                        (workspace, _, _) => new ReadTool(workspace))
                ]);
    }

    private sealed class NotePlugin : IPlugin
    {
        public string Name => "note";

        public PluginContribution Contribute() =>
            new(commands: [new NoteCommand()]);
    }

    private sealed class NoteCommand : ISlashCommand
    {
        public string Name => "note";

        public string Help => "write a note";

        public void Execute(string argument, ISlashOutput output) =>
            output.WriteNote(argument);
    }

    private sealed class RecordingOutput : ISlashOutput
    {
        public string? LastNote { get; private set; }

        public void WriteNote(string text) => LastNote = text;

        public void WriteError(string text)
        {
        }
    }
}
