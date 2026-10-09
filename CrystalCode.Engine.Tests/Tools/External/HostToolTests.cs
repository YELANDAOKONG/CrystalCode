using System.Text.Json;

using Crystal.Multimodal;
using Crystal.Multimodal.Tools;
using Crystal.Tools;

using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins.Disk;
using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.External;
using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools.External;

public sealed class HostToolTests
{
    private const string TextSet = "test-set";
    private const string PluginDirectory = "acme";

    [Fact]
    public void ToolHostContext_RejectsNullParts()
    {
        Assert.Throws<ArgumentNullException>(() => new ToolHostContext(null!, "session", "audit"));
        Assert.Throws<ArgumentNullException>(() => new ToolHostContext("/work", null!, "audit"));
        Assert.Throws<ArgumentNullException>(() => new ToolHostContext("/work", "session", null!));
        Assert.Throws<ArgumentNullException>(
            () => new ToolHostContext("/work", "session", "audit", null!));
        Assert.Throws<ArgumentNullException>(
            () => new ToolHostContext("/work", "session", "audit", "/data", null!));
    }

    [Fact]
    public void ToolHostContext_DefaultsDataDirectoriesToEmpty()
    {
        var context = new ToolHostContext("/work", "session", "audit");

        Assert.Equal(string.Empty, context.GlobalDataDirectory);
        Assert.Equal(string.Empty, context.ProjectDataDirectory);
    }

    [Fact]
    public async Task InheritedInvoke_WithoutContext_Fails()
    {
        ITool text = new EchoHostTool();
        var textError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            text.InvokeAsync(new ToolCall("1", "echo", "{}")).AsTask());
        Assert.Equal("This tool requires host context.", textError.Message);

        IMultimodalTool image = new EchoHostImageTool();
        var imageError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            image.InvokeAsync(new MultimodalToolCall("2", "echo_image", "{}")).AsTask());
        Assert.Equal("This tool requires host context.", imageError.Message);
    }

    [Fact]
    public async Task Fence_PlainTool_KeepsTheOriginalCall()
    {
        using var workspace = new TemporaryWorkspace();
        var root = new Workspace(workspace.Path);
        var home = new CrystalHome(Path.Combine(workspace.Path, "home"));
        var host = new SessionToolHost(root, home, () => "sess-9", () => "audit");
        var fenced = new FencedExternalTool(
            new PlainTool(),
            root,
            host,
            TextSet,
            [],
            timeoutSeconds: null);

        var output = await fenced.InvokeAsync(new ToolCall("1", "plain", "{}"));

        Assert.Equal("plain", output.Text);
        Assert.Equal(ToolResultStatus.Success, output.Status);
    }

    [Fact]
    public async Task Fence_HostTool_ReceivesFactsCurrentAtTheCall()
    {
        using var workspace = new TemporaryWorkspace();
        var root = new Workspace(workspace.Path);
        var home = new CrystalHome(Path.Combine(workspace.Path, "home"));
        var approval = "audit";
        var host = new SessionToolHost(root, home, () => "sess-9", () => approval);
        var text = new FencedExternalTool(
            new EchoHostTool(),
            root,
            host,
            TextSet,
            [],
            timeoutSeconds: null);
        var image = new FencedExternalMultimodalTool(
            new EchoHostImageTool(),
            root,
            host,
            TextSet,
            [],
            timeoutSeconds: null);
        var paths = ExtensionDataPaths.Resolve(home, root.Root, ExtensionDataKind.Tools, TextSet);

        var first = await text.InvokeAsync(new ToolCall("1", "echo", "{}"));
        Assert.Equal(
            Facts(root.Root, "sess-9", "audit", paths.GlobalDirectory, paths.ProjectDirectory),
            first.Text);
        Assert.True(Directory.Exists(paths.GlobalDirectory));
        Assert.True(Directory.Exists(paths.ProjectDirectory));

        approval = "full";
        var second = await text.InvokeAsync(new ToolCall("2", "echo", "{}"));
        Assert.Equal(
            Facts(root.Root, "sess-9", "full", paths.GlobalDirectory, paths.ProjectDirectory),
            second.Text);

        var nested = Path.Combine(root.Root, "nested");
        Directory.CreateDirectory(nested);
        Assert.True(root.TrySetRoot(nested, out var error), error);
        var movedPaths = ExtensionDataPaths.Resolve(home, root.Root, ExtensionDataKind.Tools, TextSet);
        var moved = await text.InvokeAsync(new ToolCall("3", "echo", "{}"));
        Assert.Equal(
            Facts(root.Root, "sess-9", "full", movedPaths.GlobalDirectory, movedPaths.ProjectDirectory),
            moved.Text);

        var pictured = await image.InvokeAsync(new MultimodalToolCall("4", "echo_image", "{}"));
        var content = Assert.IsType<TextContent>(Assert.Single(pictured.Contents));
        Assert.Equal(
            Facts(root.Root, "sess-9", "full", movedPaths.GlobalDirectory, movedPaths.ProjectDirectory),
            content.Text);
    }

    [Fact]
    public async Task PluginHost_ReceivesFactsCurrentAtTheCall()
    {
        using var workspace = new TemporaryWorkspace();
        var root = new Workspace(workspace.Path);
        var home = new CrystalHome(Path.Combine(workspace.Path, "home"));
        var approval = "audit";
        var host = new SessionToolHost(root, home, () => "sess-1", () => approval);
        ITool text = new PluginHostTool(new EchoHostTool(), host, PluginDirectory);
        IMultimodalTool image =
            new PluginHostMultimodalTool(new EchoHostImageTool(), host, PluginDirectory);
        var paths = ExtensionDataPaths.Resolve(home, root.Root, ExtensionDataKind.Plugins, PluginDirectory);

        var first = await text.InvokeAsync(new ToolCall("1", "echo", "{}"));
        Assert.Equal(
            Facts(root.Root, "sess-1", "audit", paths.GlobalDirectory, paths.ProjectDirectory),
            first.Text);
        Assert.True(Directory.Exists(paths.GlobalDirectory));
        Assert.True(Directory.Exists(paths.ProjectDirectory));

        approval = "full";
        var second = await text.InvokeAsync(new ToolCall("2", "echo", "{}"));
        Assert.Equal(
            Facts(root.Root, "sess-1", "full", paths.GlobalDirectory, paths.ProjectDirectory),
            second.Text);

        var pictured = await image.InvokeAsync(new MultimodalToolCall("3", "echo_image", "{}"));
        var content = Assert.IsType<TextContent>(Assert.Single(pictured.Contents));
        Assert.Equal(
            Facts(root.Root, "sess-1", "full", paths.GlobalDirectory, paths.ProjectDirectory),
            content.Text);
    }

    private static string Facts(
        string workspaceRoot,
        string sessionId,
        string approval,
        string globalDataDirectory,
        string projectDataDirectory) =>
        workspaceRoot
            + "\n"
            + sessionId
            + "\n"
            + approval
            + "\n"
            + globalDataDirectory
            + "\n"
            + projectDataDirectory;

    private static string FactsFrom(ToolHostContext context) =>
        Facts(
            context.WorkspaceRoot,
            context.SessionId,
            context.Approval,
            context.GlobalDataDirectory,
            context.ProjectDataDirectory);

    private static ToolDefinition Define(string name)
    {
        using var document = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{}}");
        return new ToolDefinition(name, document.RootElement.Clone(), name);
    }

    private sealed class PlainTool : ITool
    {
        public PlainTool()
        {
            Definition = Define("plain");
        }

        public ToolDefinition Definition { get; }

        public ValueTask<ToolOutput> InvokeAsync(
            ToolCall call,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ToolOutput("plain"));
    }

    private sealed class EchoHostTool : IHostTool
    {
        public EchoHostTool()
        {
            Definition = Define("echo");
        }

        public ToolDefinition Definition { get; }

        public ValueTask<ToolOutput> InvokeAsync(
            ToolCall call,
            ToolHostContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ToolOutput(FactsFrom(context)));
    }

    private sealed class EchoHostImageTool : IHostMultimodalTool
    {
        public EchoHostImageTool()
        {
            Definition = Define("echo_image");
        }

        public ToolDefinition Definition { get; }

        public ValueTask<MultimodalToolOutput> InvokeAsync(
            MultimodalToolCall call,
            ToolHostContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new MultimodalToolOutput(
            [
                new TextContent(FactsFrom(context))
            ]));
    }
}
