using System.Globalization;
using System.Text.Json;

using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Bridge.Wire;

/// <summary>
/// <c>elicitation/create</c> forms for the operator questions the engine
/// asks a front end, and the validated answers. A question form has one
/// property per question, keyed <c>q1..qN</c>; a question that allows a
/// custom answer adds a <c>qN_custom</c> string. MCP elicitation reaches the
/// front end as these same questions, so it uses the same form. Anything
/// other than a valid <c>accept</c> is a dismissal.
/// </summary>
internal static class FormWire
{
    public const string TrustField = "trust";

    public const string ChoiceField = "choice";

    public static void Questions(
        Utf8JsonWriter writer,
        string sessionId,
        string? toolCallId,
        IReadOnlyList<UserQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(questions);
        var message = questions.Count == 1 ? questions[0].Header : "The agent asks " + Count(questions.Count) + " questions.";
        Start(writer, sessionId, toolCallId, string.IsNullOrWhiteSpace(message) ? "The agent asks a question." : message);
        writer.WriteStartObject("properties");
        var required = new List<string>();
        for (var index = 0; index < questions.Count; index++)
        {
            var question = questions[index];
            var key = Key(index);
            var description = Description(question);
            if (question.Options.Count == 0)
            {
                writer.WriteStartObject(key);
                writer.WriteString("type", "string");
                writer.WriteString("title", Title(question, index));
                writer.WriteString("description", description);
                writer.WriteEndObject();
                required.Add(key);
                continue;
            }

            if (question.Multiple)
            {
                writer.WriteStartObject(key);
                writer.WriteString("type", "array");
                writer.WriteString("title", Title(question, index));
                writer.WriteString("description", description);
                writer.WriteStartObject("items");
                writer.WriteStartArray("anyOf");
                Choices(writer, question.Options);
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteStartObject(key);
                writer.WriteString("type", "string");
                writer.WriteString("title", Title(question, index));
                writer.WriteString("description", description);
                writer.WriteStartArray("oneOf");
                Choices(writer, question.Options);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            if (question.Custom)
            {
                writer.WriteStartObject(key + "_custom");
                writer.WriteString("type", "string");
                writer.WriteString("title", "Other answer");
                writer.WriteString("description", "Used when it is not empty.");
                writer.WriteEndObject();
            }
            else if (!question.Multiple)
            {
                required.Add(key);
            }
        }

        writer.WriteEndObject();
        End(writer, required);
    }

    /// <summary>
    /// The answers in question order, or null for a dismissal or an answer
    /// that does not fit the questions.
    /// </summary>
    public static QuestionResponse Answers(JsonElement result, IReadOnlyList<UserQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        var rejected = new QuestionResponse([], true);
        if (!TryAccepted(result, out var content))
        {
            return rejected;
        }

        var answers = new List<IReadOnlyList<string>>(questions.Count);
        for (var index = 0; index < questions.Count; index++)
        {
            var question = questions[index];
            var key = Key(index);
            var custom = question.Custom
                && content.TryGetProperty(key + "_custom", out var customValue)
                && customValue.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(customValue.GetString())
                    ? customValue.GetString()!.Trim()
                    : null;
            var picked = new List<string>();
            if (content.TryGetProperty(key, out var value))
            {
                switch (value.ValueKind)
                {
                    case JsonValueKind.String when value.GetString() is { Length: > 0 } text:
                        picked.Add(text);
                        break;
                    case JsonValueKind.Array:
                        picked.AddRange(value.EnumerateArray()
                            .Where(static entry => entry.ValueKind == JsonValueKind.String)
                            .Select(static entry => entry.GetString()!));
                        break;
                    case JsonValueKind.Null or JsonValueKind.Undefined:
                        break;
                    default:
                        return rejected;
                }
            }

            if (question.Options.Count > 0
                && picked.Any(label => question.Options.All(option => option.Label != label)))
            {
                return rejected;
            }

            if (custom is not null)
            {
                if (question.Multiple)
                {
                    picked.Add(custom);
                }
                else
                {
                    picked = [custom];
                }
            }

            if (picked.Count == 0 || (!question.Multiple && picked.Count > 1))
            {
                return rejected;
            }

            answers.Add(picked);
        }

        return new QuestionResponse(answers, false);
    }

    /// <summary>
    /// A trust question. Inside a session it is session-scoped; before the
    /// session exists it is tied to the <c>session/new</c>, <c>load</c>, or
    /// <c>resume</c> request that needs it.
    /// </summary>
    public static void Trust(Utf8JsonWriter writer, string? sessionId, JsonElement? requestId, WorkspaceTrustRequest request)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(request);
        var message = request.CoversRepository
            ? $"Trust the repository at {request.TrustRoot}? It contains {request.Workspace}. Crystal Code reads its instructions, prompts, tools, plugins, and MCP servers."
            : $"Trust {request.TrustRoot}? Crystal Code reads its instructions, prompts, tools, plugins, and MCP servers.";
        Start(writer, sessionId, null, message, requestId);
        writer.WriteStartObject("properties");
        writer.WriteStartObject(TrustField);
        writer.WriteString("type", "boolean");
        writer.WriteString("title", "Trust this directory");
        writer.WriteBoolean("default", false);
        writer.WriteEndObject();
        writer.WriteEndObject();
        End(writer, [TrustField]);
    }

    public static bool Trusted(JsonElement result) =>
        TryAccepted(result, out var content)
        && content.TryGetProperty(TrustField, out var value)
        && value.ValueKind == JsonValueKind.True;

    /// <summary>A pick from saved sessions; titles show the preview and workspace.</summary>
    public static void Sessions(
        Utf8JsonWriter writer,
        string sessionId,
        IReadOnlyList<SessionSummary> sessions,
        bool listWorkspace)
    {
        Choice(writer, sessionId, "Choose a session to resume.", sessions.Select(session => (
            session.Id,
            (listWorkspace ? session.Workspace + "  " : string.Empty)
                + (session.UpdatedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "--")
                + "  " + Preview(session.Title))));
    }

    public static void Children(Utf8JsonWriter writer, string sessionId, IReadOnlyList<ChildSessionDocument> children) =>
        Choice(writer, sessionId, "Choose a child session.", children.Select(child => (
            child.Id,
            child.Label + "  " + child.Agent + "  " + child.Status)));

    /// <summary>The chosen id, if it is one of <paramref name="allowed"/>.</summary>
    public static string? Chosen(JsonElement result, IEnumerable<string> allowed) =>
        TryAccepted(result, out var content)
        && content.TryGetProperty(ChoiceField, out var value)
        && value.ValueKind == JsonValueKind.String
        && allowed.Contains(value.GetString(), StringComparer.Ordinal)
            ? value.GetString()
            : null;

    private static void Choice(
        Utf8JsonWriter writer,
        string sessionId,
        string message,
        IEnumerable<(string Id, string Title)> choices)
    {
        Start(writer, sessionId, null, message);
        writer.WriteStartObject("properties");
        writer.WriteStartObject(ChoiceField);
        writer.WriteString("type", "string");
        writer.WriteString("title", "Choice");
        writer.WriteStartArray("oneOf");
        foreach (var (id, title) in choices)
        {
            writer.WriteStartObject();
            writer.WriteString("const", id);
            writer.WriteString("title", title);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
        End(writer, [ChoiceField]);
    }

    private static bool TryAccepted(JsonElement result, out JsonElement content)
    {
        content = default;
        if (result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("action", out var action)
            || action.ValueKind != JsonValueKind.String
            || action.GetString() != "accept"
            || !result.TryGetProperty("content", out content)
            || content.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return true;
    }

    private static void Start(
        Utf8JsonWriter writer,
        string? sessionId,
        string? toolCallId,
        string message,
        JsonElement? requestId = null)
    {
        writer.WriteStartObject();
        writer.WriteString("mode", "form");
        if (sessionId is not null)
        {
            writer.WriteString("sessionId", sessionId);
        }
        else if (requestId is { } id)
        {
            writer.WritePropertyName("requestId");
            id.WriteTo(writer);
        }

        if (toolCallId is not null)
        {
            writer.WriteString("toolCallId", toolCallId);
        }

        writer.WriteString("message", message);
        writer.WriteStartObject("requestedSchema");
        writer.WriteString("type", "object");
    }

    private static void End(Utf8JsonWriter writer, IReadOnlyList<string> required)
    {
        writer.WriteStartArray("required");
        foreach (var name in required)
        {
            writer.WriteStringValue(name);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void Choices(Utf8JsonWriter writer, IReadOnlyList<QuestionOption> options)
    {
        foreach (var option in options)
        {
            writer.WriteStartObject();
            writer.WriteString("const", option.Label);
            writer.WriteString("title", option.Label);
            if (!string.IsNullOrWhiteSpace(option.Description))
            {
                writer.WriteString("description", option.Description);
            }

            writer.WriteEndObject();
        }
    }

    private static string Key(int index) => "q" + (index + 1).ToString(CultureInfo.InvariantCulture);

    private static string Title(UserQuestion question, int index) =>
        string.IsNullOrWhiteSpace(question.Header) ? "Question " + (index + 1).ToString(CultureInfo.InvariantCulture) : question.Header;

    private static string Description(UserQuestion question) => question.Text;

    private static string Count(int count) => count.ToString(CultureInfo.InvariantCulture);

    private static string Preview(string preview) => preview.Length <= 80 ? preview : preview[..77] + "...";
}
