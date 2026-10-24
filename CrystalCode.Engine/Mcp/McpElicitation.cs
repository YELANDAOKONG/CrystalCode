using System.Globalization;
using System.Text.Json;

using CrystalCode.Engine.Tools;

using ModelContextProtocol.Protocol;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// Turns a server's form elicitation into operator questions and the answers
/// back into the form values. A server that asks for something this cannot
/// express is declined, never guessed.
/// </summary>
internal static class McpElicitation
{
    public const int MaximumFields = 8;

    private const string Accept = "accept";
    private const string Decline = "decline";
    private const string Cancel = "cancel";

    public static async ValueTask<ElicitResult> AskAsync(
        string serverName,
        ElicitRequestParams request,
        IUserPrompt prompt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(prompt);
        cancellationToken.ThrowIfCancellationRequested();

        // URL mode sends the operator to a browser for a flow the server controls.
        // That is outside this build, so it is declined rather than opened.
        if (!string.Equals(request.Mode ?? "form", "form", StringComparison.Ordinal)
            || request.RequestedSchema?.Properties is not { Count: > 0 and <= MaximumFields } properties)
        {
            return new ElicitResult { Action = Decline };
        }

        var required = request.RequestedSchema.Required ?? [];
        var fields = new List<Field>();
        foreach (var (name, schema) in properties)
        {
            if (!TryCreateField(name, schema, required.Contains(name), out var field))
            {
                return new ElicitResult { Action = Decline };
            }

            fields.Add(field);
        }

        var header = $"MCP {serverName}";
        var message = string.IsNullOrWhiteSpace(request.Message)
            ? "The server asks for input."
            : request.Message.Trim();
        var questions = fields
            .Select(field => field.ToQuestion(header, message))
            .ToArray();

        QuestionResponse response;
        try
        {
            response = await prompt.AskAsync(questions, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ElicitResult { Action = Cancel };
        }

        if (response.IsRejected || response.Answers.Count != fields.Count)
        {
            return new ElicitResult { Action = Cancel };
        }

        var content = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        for (var index = 0; index < fields.Count; index++)
        {
            var answers = response.Answers[index];
            if (!fields[index].TryConvert(answers, out var value, out var skipped))
            {
                return new ElicitResult { Action = Decline };
            }

            if (!skipped)
            {
                content[fields[index].Name] = value;
            }
        }

        return new ElicitResult { Action = Accept, Content = content };
    }

    private static bool TryCreateField(
        string name,
        ElicitRequestParams.PrimitiveSchemaDefinition schema,
        bool required,
        out Field field)
    {
        field = null!;
        var title = string.IsNullOrWhiteSpace(schema.Title) ? name : schema.Title.Trim();
        var description = schema.Description?.Trim() ?? string.Empty;
        switch (schema)
        {
            case ElicitRequestParams.BooleanSchema:
                field = new Field(name, title, description, required, FieldKind.Boolean, ["Yes", "No"]);
                return true;
            case ElicitRequestParams.NumberSchema number:
                field = new Field(name, title, description, required, FieldKind.Number, [], number.Type == "integer");
                return true;
            case ElicitRequestParams.StringSchema:
                field = new Field(name, title, description, required, FieldKind.Text, []);
                return true;
            case ElicitRequestParams.UntitledSingleSelectEnumSchema single when single.Enum is { Count: > 0 }:
                field = new Field(name, title, description, required, FieldKind.Choice, [.. single.Enum]);
                return true;
            case ElicitRequestParams.LegacyTitledEnumSchema legacy when legacy.Enum is { Count: > 0 }:
                field = new Field(name, title, description, required, FieldKind.Choice, [.. legacy.Enum]);
                return true;
            default:
                return false;
        }
    }

    private enum FieldKind
    {
        Text,
        Number,
        Boolean,
        Choice
    }

    private sealed class Field(
        string name,
        string title,
        string description,
        bool required,
        FieldKind kind,
        IReadOnlyList<string> options,
        bool integer = false)
    {
        public string Name => name;

        public UserQuestion ToQuestion(string header, string message)
        {
            var text = new List<string> { message, title };
            if (description.Length > 0)
            {
                text.Add(description);
            }

            if (!required)
            {
                text.Add("Optional. Leave empty to skip.");
            }

            return new UserQuestion(
                header,
                string.Join(Environment.NewLine, text),
                [.. options.Select(option => new QuestionOption(option, string.Empty))],
                Multiple: false,
                Custom: kind is FieldKind.Text or FieldKind.Number);
        }

        public bool TryConvert(IReadOnlyList<string> answers, out JsonElement value, out bool skipped)
        {
            value = default;
            skipped = false;
            var answer = answers.Count == 0 ? string.Empty : answers[0].Trim();
            if (answer.Length == 0)
            {
                skipped = !required;
                return skipped;
            }

            switch (kind)
            {
                case FieldKind.Boolean:
                    if (answer.Equals("Yes", StringComparison.OrdinalIgnoreCase)
                        || answer.Equals("No", StringComparison.OrdinalIgnoreCase))
                    {
                        value = JsonSerializer.SerializeToElement(
                            answer.Equals("Yes", StringComparison.OrdinalIgnoreCase));
                        return true;
                    }

                    return false;
                case FieldKind.Number:
                    if (integer && long.TryParse(answer, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                    {
                        value = JsonSerializer.SerializeToElement(whole);
                        return true;
                    }

                    if (!integer && double.TryParse(answer, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)
                        && double.IsFinite(real))
                    {
                        value = JsonSerializer.SerializeToElement(real);
                        return true;
                    }

                    return false;
                case FieldKind.Choice:
                    if (options.Contains(answer, StringComparer.Ordinal))
                    {
                        value = JsonSerializer.SerializeToElement(answer);
                        return true;
                    }

                    return false;
                default:
                    value = JsonSerializer.SerializeToElement(answer);
                    return true;
            }
        }
    }
}
