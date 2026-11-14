using System.Text.Json;

using Crystal.Tools;

namespace CrystalCode.Engine.Tools.Todos;

/// <summary>
/// Replaces or merges the session todo list.
/// </summary>
public sealed class TodoWriteTool : ITool
{
    public const string ToolName = "todowrite";
    private const int MaximumContentLength = 200;

    private const string ToolDescription =
        "Updates the session todo list. Use it before multi-step work "
        + "(three or more steps, non-trivial, or several user items). "
        + "Skip a single simple edit or a purely conversational question. "
        + "Keep exactly one item in_progress. Mark completed only after the work is done and verified. "
        + "merge true upserts by id, keeping existing positions and appending new items; "
        + "omit or false replaces the list. With merge true, reorder true moves supplied items "
        + "to the front in input order; reorder defaults to false and is ignored when replacing.";

    private readonly TodoList _todos;

    public TodoWriteTool(TodoList todos)
    {
        ArgumentNullException.ThrowIfNull(todos);
        _todos = todos;
        Definition = ToolDefinition.Json(
            ToolName,
            ToolSchema.Parse(
                """
                {
                  "type": "object",
                  "properties": {
                    "todos": {
                      "type": "array",
                      "description": "Todo items to write.",
                      "items": {
                        "type": "object",
                        "properties": {
                          "id": {
                            "type": "string",
                            "description": "Stable id for this item."
                          },
                          "content": {
                            "type": "string",
                            "description": "Short task description."
                          },
                          "status": {
                            "type": "string",
                            "description": "pending, in_progress, completed, or cancelled."
                          }
                        },
                        "required": ["id", "content", "status"]
                      }
                    },
                    "merge": {
                      "type": "boolean",
                      "description": "When true, upsert by id and keep other items, preserving existing positions and appending new items. When false, replace the list."
                    },
                    "reorder": {
                      "type": "boolean",
                      "description": "With merge true, move supplied items to the front in input order. Defaults to false. Ignored when replacing."
                    }
                  },
                  "required": ["todos"]
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

        if (!TryRead(call.Arguments, out var items, out var merge, out var reorder, out var error))
        {
            return ValueTask.FromResult(new ToolOutput(error, ToolResultStatus.Failure));
        }

        var text = merge ? _todos.Merge(items, reorder) : _todos.Replace(items);
        if (text.StartsWith("At most", StringComparison.Ordinal))
        {
            return ValueTask.FromResult(new ToolOutput(text, ToolResultStatus.Failure));
        }

        return ValueTask.FromResult(new ToolOutput(text));
    }

    private static bool TryRead(
        string arguments,
        out IReadOnlyList<TodoItem> items,
        out bool merge,
        out bool reorder,
        out string error)
    {
        items = [];
        merge = false;
        reorder = false;
        error = "Arguments must include a todos array.";
        if (!ToolArguments.TryReadOptionalBoolean(arguments, "merge", out var mergeValue))
        {
            error = "merge must be a boolean when supplied.";
            return false;
        }

        merge = mergeValue ?? false;
        if (!ToolArguments.TryReadOptionalBoolean(arguments, "reorder", out var reorderValue))
        {
            error = "reorder must be a boolean when supplied.";
            return false;
        }

        reorder = reorderValue ?? false;
        if (!TryOpen(arguments, out var document))
        {
            return false;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("todos", out var todos)
                || todos.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var parsed = new List<TodoItem>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in todos.EnumerateArray())
            {
                if (!TryReadItem(element, ids, out var item, out error))
                {
                    return false;
                }

                parsed.Add(item);
            }

            items = parsed;
            error = string.Empty;
            return true;
        }
    }

    private static bool TryReadItem(
        JsonElement element,
        HashSet<string> ids,
        out TodoItem item,
        out string error)
    {
        item = null!;
        error = "Each todo needs id, content, and status.";
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!TryReadString(element, "id", out var id)
            || !TryReadString(element, "content", out var content)
            || !TryReadString(element, "status", out var statusText))
        {
            return false;
        }

        if (!ids.Add(id))
        {
            error = $"Duplicate todo id '{id}'.";
            return false;
        }

        if (content.Length > MaximumContentLength)
        {
            error = $"Todo content cannot exceed {MaximumContentLength} characters.";
            return false;
        }

        if (!TryParseStatus(statusText, out var status))
        {
            error = "status must be pending, in_progress, completed, or cancelled.";
            return false;
        }

        item = new TodoItem(id, content, status);
        error = string.Empty;
        return true;
    }

    private static bool TryParseStatus(string text, out TodoStatus status)
    {
        if (text.Equals("pending", StringComparison.OrdinalIgnoreCase))
        {
            status = TodoStatus.Pending;
            return true;
        }

        if (text.Equals("in_progress", StringComparison.OrdinalIgnoreCase)
            || text.Equals("in-progress", StringComparison.OrdinalIgnoreCase))
        {
            status = TodoStatus.InProgress;
            return true;
        }

        if (text.Equals("completed", StringComparison.OrdinalIgnoreCase)
            || text.Equals("complete", StringComparison.OrdinalIgnoreCase))
        {
            status = TodoStatus.Completed;
            return true;
        }

        if (text.Equals("cancelled", StringComparison.OrdinalIgnoreCase)
            || text.Equals("canceled", StringComparison.OrdinalIgnoreCase))
        {
            status = TodoStatus.Cancelled;
            return true;
        }

        status = TodoStatus.Pending;
        return false;
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

    private static bool TryOpen(string arguments, out JsonDocument document)
    {
        try
        {
            document = JsonDocument.Parse(arguments);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return true;
            }

            document.Dispose();
            document = null!;
            return false;
        }
        catch (JsonException)
        {
            document = null!;
            return false;
        }
    }
}
