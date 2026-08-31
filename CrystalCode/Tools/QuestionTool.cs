using Crystal.Tools;

namespace CrystalCode.Tools;

/// <summary>
/// Asks the user a question and waits for the answer.
/// </summary>
public sealed class QuestionTool : ITool
{
    internal const string ToolName = "question";

    private const string ToolDescription =
        "Asks the user a question and waits. Use it when you are uncertain: a missing fact, "
        + "a choice that would change the work, or something the repository cannot tell you. "
        + "Optional options; put a recommended choice first. "
        + "Do not ask whether to continue or whether to run tests.";

    private readonly IUserPrompt _prompt;

    public QuestionTool(IUserPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        _prompt = prompt;
        Definition = new ToolDefinition(
            ToolName,
            ToolSchema.Parse(
                """
                {
                  "type": "object",
                  "properties": {
                    "question": {
                      "type": "string",
                      "description": "The question to ask the user."
                    },
                    "options": {
                      "type": "array",
                      "items": { "type": "string" },
                      "description": "Optional choices. When omitted the user types a free answer."
                    }
                  },
                  "required": ["question"]
                }
                """),
            ToolDescription);
    }

    public ToolDefinition Definition { get; }

    public ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);

        if (!ToolArguments.TryReadRequiredString(call.Arguments, "question", out var question)
            || !ToolArguments.TryReadOptionalStringArray(call.Arguments, "options", out var options))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    "Arguments must include question and an optional options string array.",
                    ToolResultStatus.Failure));
        }

        return AskAsync(question, options, cancellationToken);
    }

    private async ValueTask<ToolOutput> AskAsync(
        string question,
        IReadOnlyList<string>? options,
        CancellationToken cancellationToken)
    {
        var answer = await _prompt.AskAsync(question, options, cancellationToken);
        if (string.IsNullOrWhiteSpace(answer))
        {
            return new ToolOutput(
                "The user did not provide an answer.",
                ToolResultStatus.Failure);
        }

        return new ToolOutput(answer.Trim());
    }
}
