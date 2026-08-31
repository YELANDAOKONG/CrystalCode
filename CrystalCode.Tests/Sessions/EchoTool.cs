using Crystal.Tools;

using CrystalCode.Tools;

namespace CrystalCode.Tests.Sessions;

internal sealed class EchoTool : ITool
{
    public EchoTool()
    {
        Definition = new ToolDefinition(
            "echo",
            ToolSchema.Parse("""{"type":"object","properties":{}}"""),
            "Returns the call arguments.");
    }

    public ToolDefinition Definition { get; }

    public ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ToolOutput("echoed:" + call.Arguments));
    }
}
