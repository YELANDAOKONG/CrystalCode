using System.Text.Json;

using Crystal.Multimodal;
using Crystal.Multimodal.Tools;
using Crystal.Tools;

using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.External;
using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools.External;

public sealed class HostToolTests
{
    [Fact]
    public void ToolHostContext_RejectsNullParts()
    {
        Assert.Throws<ArgumentNullException>(() => new ToolHostContext(null!, "session", "audit"));
        Assert.Throws<ArgumentNullException>(() => new ToolHostContext("/work", null!, "audit"));
        Assert.Throws<ArgumentNullException>(() => new ToolHostContext("/work", "session", null!));
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
        var host = new SessionToolHost(root, () => "sess-9", () => "audit");
        var fenced = new FencedExternalTool(
            new PlainTool(),
            root,
            host,
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
        var approval = "audit";
        var host = new SessionToolHost(root, () => "sess-9", () => approval);
        var text = new FencedExternalTool(
            new EchoHostTool(),
            root,
            host,
            [],
            timeoutSeconds: null);
        var image = new FencedExternalMultimodalTool(
            new EchoHostImageTool(),
            root,
            host,
            [],
            timeoutSeconds: null);

        var first = await text.InvokeAsync(new ToolCall("1", "echo", "{}"));
        Assert.Equal(Facts(root.Root, "sess-9", "audit"), first.Text);

        approval = "full";
        var second = await text.InvokeAsync(new ToolCall("2", "echo", "{}"));
        Assert.Equal(Facts(root.Root, "sess-9", "full"), second.Text);

        var nested = Path.Combine(root.Root, "nested");
        Directory.CreateDirectory(nested);
        Assert.True(root.TrySetRoot(nested, out var error), error);
        var moved = await text.InvokeAsync(new ToolCall("3", "echo", "{}"));
        Assert.Equal(Facts(root.Root, "sess-9", "full"), moved.Text);

        var pictured = await image.InvokeAsync(new MultimodalToolCall("4", "echo_image", "{}"));
        var content = Assert.IsType<TextContent>(Assert.Single(pictured.Contents));
        Assert.Equal(Facts(root.Root, "sess-9", "full"), content.Text);
    }

    private static string Facts(string workspaceRoot, string sessionId, string approval) =>
        workspaceRoot + "\n" + sessionId + "\n" + approval;

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
            ValueTask.FromResult(new ToolOutput(
                Facts(context.WorkspaceRoot, context.SessionId, context.Approval)));
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
                new TextContent(Facts(context.WorkspaceRoot, context.SessionId, context.Approval))
            ]));
    }
}
