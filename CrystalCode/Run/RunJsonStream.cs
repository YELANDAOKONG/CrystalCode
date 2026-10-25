using System.Globalization;
using System.Text;
using System.Text.Json;

using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Run;

/// <summary>
/// Projects the turn events of one conversation, the parent or one child,
/// onto JSON events. Text and thinking are emitted once per segment. A tool
/// result is paired with its call only by call id.
/// </summary>
internal sealed class RunJsonStream
{
    private readonly Action<string, Action<Utf8JsonWriter>> _emit;
    private readonly bool _showThinking;
    private readonly StringBuilder _reply = new();
    private readonly StringBuilder _thinking = new();
    private readonly Dictionary<string, ToolCall> _calls = new(StringComparer.Ordinal);

    public RunJsonStream(Action<string, Action<Utf8JsonWriter>> emit, bool showThinking)
    {
        ArgumentNullException.ThrowIfNull(emit);
        _emit = emit;
        _showThinking = showThinking;
    }

    public void Apply(SessionEvent sessionEvent)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        switch (sessionEvent)
        {
            case StreamReceived received:
                Append(received.StreamEvent);
                break;
            case ModelRoundClosed:
                Flush();
                break;
            case ToolCallsIssued issued:
                Flush();
                foreach (var call in issued.Calls)
                {
                    _calls[call.CallId] = call;
                }

                break;
            case ToolResultsReceived received:
                Flush();
                foreach (var result in received.Results)
                {
                    WriteTool(result);
                }

                break;
            case ErrorWritten error:
                Flush();
                _emit("error", writer => writer.WriteString("message", error.Text));
                break;
            case NoteWritten note:
                Flush();
                _emit("note", writer => writer.WriteString("message", note.Text));
                break;
            case RetryScheduled retry:
                // The failed attempt's partial output is replaced by the retry.
                _reply.Clear();
                _thinking.Clear();
                _emit(
                    "retry",
                    writer =>
                    {
                        writer.WriteNumber("attempt", retry.Attempt.Attempt);
                        writer.WriteString("message", retry.Attempt.Message);
                        writer.WriteNumber("delaySeconds", retry.Attempt.Delay.TotalSeconds);
                    });
                break;
            default:
                break;
        }
    }

    public void Flush()
    {
        var thinking = Take(_thinking);
        if (thinking.Length > 0)
        {
            _emit("reasoning", writer => writer.WriteString("text", thinking));
        }

        var text = Take(_reply);
        if (text.Length > 0)
        {
            _emit("text", writer => writer.WriteString("text", text));
        }
    }

    private void Append(ChatStreamEvent streamEvent)
    {
        switch (streamEvent)
        {
            case ChatTextDelta text:
                _reply.Append(text.Text);
                break;
            case ChatReasoningTextDelta reasoning when _showThinking:
                _thinking.Append(reasoning.Text);
                break;
            default:
                break;
        }
    }

    private void WriteTool(ToolResult result)
    {
        _calls.Remove(result.CallId, out var call);
        var images = ImageMarkerText.Matches(result.Text)
            .Select(static match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .ToArray();
        _emit(
            "tool_use",
            writer =>
            {
                writer.WriteString("callId", result.CallId);
                if (call is not null)
                {
                    writer.WriteString("name", call.Name);
                    WriteArguments(writer, call.Arguments);
                }

                writer.WriteString(
                    "status",
                    result.Status == ToolResultStatus.Success ? "success" : "failure");
                writer.WriteString(
                    "output",
                    ImageMarkerText.Display(result.Text).Replace("\r\n", "\n", StringComparison.Ordinal));
                if (images.Length > 0)
                {
                    writer.WritePropertyName("images");
                    writer.WriteStartArray();
                    foreach (var number in images)
                    {
                        writer.WriteNumberValue(number);
                    }

                    writer.WriteEndArray();
                }
            });
    }

    private static void WriteArguments(Utf8JsonWriter writer, string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            writer.WriteString("arguments", string.Empty);
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(arguments);
            writer.WritePropertyName("arguments");
            document.RootElement.WriteTo(writer);
        }
        catch (JsonException)
        {
            writer.WriteString("arguments", arguments);
        }
    }

    private static string Take(StringBuilder buffer)
    {
        var text = buffer.ToString().TrimEnd();
        buffer.Clear();
        return text;
    }
}
