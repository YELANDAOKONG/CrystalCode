using System.Text.Json;

using Crystal.Tools;

namespace CrystalCode.Tools;

/// <summary>
/// Asks the user one or more questions and waits for the answers.
/// </summary>
public sealed class QuestionTool : ITool
{
    internal const string ToolName = "question";

    private const string ToolDescription =
        "Asks the user one or more questions and waits. Use it when you are uncertain: a missing fact, "
        + "a choice that would change the work, or something the repository cannot tell you. "
        + "Custom answers are enabled by default. Set multiple true to allow more than one answer. "
        + "Put a recommended choice first and suffix its label with (Recommended). "
        + "Do not add an Other option when custom answers are enabled. "
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
                    "questions": {
                      "type": "array",
                      "description": "Questions to ask the user in order.",
                      "minItems": 1,
                      "items": {
                        "type": "object",
                        "properties": {
                          "header": {
                            "type": "string",
                            "description": "A short label for the question."
                          },
                          "question": {
                            "type": "string",
                            "description": "The complete question to ask."
                          },
                          "options": {
                            "type": "array",
                            "description": "Available choices.",
                            "items": {
                              "type": "object",
                              "properties": {
                                "label": {
                                  "type": "string",
                                  "description": "Concise display text for the choice."
                                },
                                "description": {
                                  "type": "string",
                                  "description": "The impact or tradeoff of the choice."
                                }
                              },
                              "required": ["label", "description"]
                            }
                          },
                          "multiple": {
                            "type": "boolean",
                            "description": "Allow selecting more than one answer. Defaults to false."
                          },
                          "custom": {
                            "type": "boolean",
                            "description": "Allow typing a custom answer. Defaults to true."
                          }
                        },
                        "required": ["header", "question", "options"]
                      }
                    }
                  },
                  "required": ["questions"]
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

        if (!TryReadQuestions(call.Arguments, out var questions, out var error))
        {
            return ValueTask.FromResult(
                new ToolOutput(
                    error,
                    ToolResultStatus.Failure));
        }

        return AskAsync(questions, cancellationToken);
    }

    private async ValueTask<ToolOutput> AskAsync(
        IReadOnlyList<UserQuestion> questions,
        CancellationToken cancellationToken)
    {
        var response = await _prompt.AskAsync(questions, cancellationToken);
        if (response.IsRejected)
        {
            return new ToolOutput(
                "The user dismissed the question.",
                ToolResultStatus.Failure);
        }

        var answers = response.Answers.Select(answer => answer.ToArray()).ToArray();
        return new ToolOutput(
            "User answers: " + JsonSerializer.Serialize(new { answers }));
    }

    private static bool TryReadQuestions(
        string arguments,
        out IReadOnlyList<UserQuestion> questions,
        out string error)
    {
        questions = [];
        error = "Arguments must include a non-empty questions array.";
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(arguments);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("questions", out var value)
                || value.ValueKind != JsonValueKind.Array
                || value.GetArrayLength() == 0)
            {
                return false;
            }

            var parsed = new List<UserQuestion>();
            foreach (var element in value.EnumerateArray())
            {
                if (!TryReadQuestion(element, out var question, out error))
                {
                    return false;
                }

                parsed.Add(question);
            }

            questions = parsed;
            error = string.Empty;
            return true;
        }
    }

    private static bool TryReadQuestion(
        JsonElement element,
        out UserQuestion question,
        out string error)
    {
        question = null!;
        error = "Each question needs header, question, and options.";
        if (element.ValueKind != JsonValueKind.Object
            || !TryReadString(element, "header", out var header)
            || !TryReadString(element, "question", out var text)
            || !TryReadBoolean(element, "multiple", false, out var multiple)
            || !TryReadBoolean(element, "custom", true, out var custom)
            || !element.TryGetProperty("options", out var optionsValue)
            || optionsValue.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var options = new List<QuestionOption>();
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var optionElement in optionsValue.EnumerateArray())
        {
            if (!TryReadOption(optionElement, labels, out var option, out error))
            {
                return false;
            }

            options.Add(option);
        }

        if (options.Count == 0 && !custom)
        {
            error = "A question needs at least one option when custom answers are disabled.";
            return false;
        }

        question = new UserQuestion(header, text, options, multiple, custom);
        error = string.Empty;
        return true;
    }

    private static bool TryReadOption(
        JsonElement element,
        HashSet<string> labels,
        out QuestionOption option,
        out string error)
    {
        option = null!;
        error = "Each option needs a non-empty label and description.";
        if (element.ValueKind != JsonValueKind.Object
            || !TryReadString(element, "label", out var label)
            || !TryReadString(element, "description", out var description))
        {
            return false;
        }

        if (!labels.Add(label))
        {
            error = $"Question option label '{label}' is duplicated.";
            return false;
        }

        option = new QuestionOption(label, description);
        error = string.Empty;
        return true;
    }

    private static bool TryReadString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = property.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text.Trim();
        return true;
    }

    private static bool TryReadBoolean(
        JsonElement element,
        string name,
        bool defaultValue,
        out bool value)
    {
        value = defaultValue;
        if (!element.TryGetProperty(name, out var property))
        {
            return true;
        }

        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }
}
