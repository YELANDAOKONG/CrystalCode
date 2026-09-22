using System.Text.Json;

using Crystal.Multimodal;
using Crystal.Multimodal.Tools;
using Crystal.Tools;

using CrystalCode.Approvals;
using CrystalCode.Home;
using CrystalCode.Sessions;
using CrystalCode.Tests.Approvals;
using CrystalCode.Tests.Home;
using CrystalCode.Tests.Tools;
using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class HybridMultimodalToolExecutorTests
{
    [Fact]
    public void Constructor_WithNativeMultimodalTool_AddsItsDefinition()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var text = new StubTextExecutor(CreateDefinition("text"));
        var policy = CreatePolicy(home, workspace);

        var executor = new HybridMultimodalToolExecutor(
            text,
            [new StubMultimodalTool(CreateDefinition("image"))],
            policy);

        Assert.Equal(["text", "image"], executor.Definitions.Select(item => item.Name));
    }

    [Fact]
    public void Constructor_WithoutMultimodalTools_LeavesTextDefinitionsOnly()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var text = new StubTextExecutor(CreateDefinition("text"));
        var policy = CreatePolicy(home, workspace);

        var executor = new HybridMultimodalToolExecutor(text, [], policy);

        Assert.Equal(["text"], executor.Definitions.Select(item => item.Name));
    }

    private static ApprovalPolicy CreatePolicy(
        TemporaryHome home,
        TemporaryWorkspace workspace) =>
        new(
            ApprovalMode.Default,
            new Workspace(workspace.Path),
            new GrantStore(home.Home),
            new ThrowingApprovalPrompt());

    private static ToolDefinition CreateDefinition(string name)
    {
        using var document = JsonDocument.Parse("""{"type":"object","properties":{}}""");
        return new ToolDefinition(name, document.RootElement.Clone(), name);
    }

    private sealed class StubTextExecutor : IToolExecutor
    {
        public StubTextExecutor(ToolDefinition definition)
        {
            Definitions = [definition];
        }

        public IReadOnlyList<ToolDefinition> Definitions { get; }

        public Task<IReadOnlyList<ToolResult>> ExecuteAsync(
            IEnumerable<ToolCall> calls,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubMultimodalTool : IMultimodalTool
    {
        public StubMultimodalTool(ToolDefinition definition)
        {
            Definition = definition;
        }

        public ToolDefinition Definition { get; }

        public ValueTask<MultimodalToolOutput> InvokeAsync(
            MultimodalToolCall call,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                new MultimodalToolOutput([new TextContent("ok")]));
    }
}
