using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// What the session lends to every MCP connection. Servers reach the operator
/// only through these members.
/// </summary>
/// <param name="WorkspaceRoot">Returns the current workspace root.</param>
/// <param name="Questions">Receives elicitation requests as questions.</param>
/// <param name="ToolsChanged">Called when a server reports a changed tool list.</param>
internal sealed record McpHostCallbacks(
    Func<string> WorkspaceRoot,
    IUserPrompt Questions,
    Action<string> ToolsChanged)
{
    public static McpHostCallbacks None { get; } = new(
        static () => Environment.CurrentDirectory,
        new DecliningPrompt(),
        static _ => { });

    private sealed class DecliningPrompt : IUserPrompt
    {
        public ValueTask<QuestionResponse> AskAsync(
            IReadOnlyList<UserQuestion> questions,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new QuestionResponse([], true));
    }
}
