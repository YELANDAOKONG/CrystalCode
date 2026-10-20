using System.Globalization;
using System.Text;
using System.Text.Json;

using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Run;

/// <summary>
/// JSON Lines transcript for one unattended run. Each stdout line is one
/// object with <c>type</c>, <c>timestamp</c>, and <c>sessionID</c>. Thinking
/// text is emitted only when requested. The observer is called from the turn
/// thread.
/// </summary>
internal sealed class RunJsonLog : IRunLog
{
    private readonly TextWriter _output;
    private readonly bool _showThinking;
    private readonly object _gate = new();
    private readonly StringBuilder _reply = new();
    private readonly StringBuilder _thinking = new();
    private readonly List<ToolCall> _calls = [];
    private string _sessionId = string.Empty;

    public RunJsonLog(TextWriter output, bool showThinking)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
        _showThinking = showThinking;
    }

    public TurnStopReason? StopReason { get; private set; }

    public void BindSession(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        lock (_gate)
        {
            _sessionId = sessionId.Trim();
        }
    }

    public void WriteEpilogue(string? status, string hint)
    {
        ArgumentNullException.ThrowIfNull(hint);
        lock (_gate)
        {
            FlushSegments();
            if (status is not null)
            {
                Emit("stopped", writer => writer.WriteString("status", status));
            }

            Emit("session", writer => writer.WriteString("text", hint));
        }
    }

    public void OnEvent(SessionEvent sessionEvent)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        lock (_gate)
        {
            switch (sessionEvent)
            {
                case ChildSessionChanged child:
                    Emit("child_status", writer =>
                    {
                        writer.WriteString("childSessionID", child.Child.Id);
                        writer.WriteString("invocationID", child.InvocationId);
                        writer.WriteString("agent", child.Child.Agent);
                        writer.WriteString("label", child.Child.Label);
                        writer.WriteString("status", child.Child.Status);
                        writer.WritePropertyName("images");
                        JsonSerializer.Serialize(writer, child.Child.Images.Select(image => new
                        {
                            number = image.Number, mimeType = image.MimeType, contentHash = image.ContentHash,
                            source = "child_tool"
                        }));
                        writer.WritePropertyName("usage");
                        JsonSerializer.Serialize(writer, child.Child.CumulativeUsage);
                    });
                    break;
                case ChildEventReceived child:
                    EmitChild(child);
                    break;
                case StreamReceived received:
                    Apply(received.StreamEvent);
                    break;
                case TurnStarted:
                    Emit("step_start");
                    break;
                case ModelRoundClosed:
                    FlushSegments();
                    break;
                case ToolCallsIssued issued:
                    FlushSegments();
                    _calls.AddRange(issued.Calls);
                    break;
                case ToolResultsReceived received:
                    FlushSegments();
                    foreach (var result in received.Results)
                    {
                        EmitTool(Take(result.CallId), result);
                    }

                    break;
                case ErrorWritten error:
                    FlushSegments();
                    Emit("error", writer => writer.WriteString("message", error.Text));
                    break;
                case NoteWritten note:
                    FlushSegments();
                    Emit("note", writer => writer.WriteString("message", note.Text));
                    break;
                case RetryScheduled retry:
                    Emit(
                        "retry",
                        writer =>
                        {
                            writer.WriteNumber("attempt", retry.Attempt.Attempt);
                            writer.WriteString("message", retry.Attempt.Message);
                            writer.WriteNumber("delaySeconds", retry.Attempt.Delay.TotalSeconds);
                        });
                    break;
                case TurnFinished finished:
                    FlushSegments();
                    StopReason = finished.Result.StopReason;
                    Emit(
                        "step_finish",
                        writer =>
                        {
                            writer.WriteString("reason", finished.Result.StopReason.Value);
                            writer.WriteNumber("modelCalls", finished.Result.ModelCallCount);
                            writer.WriteNumber("toolCalls", finished.Result.ToolCallCount);
                            if (finished.Usage is not null)
                            {
                                writer.WriteNumber("inputTokens", finished.Usage.InputTokenCount);
                                writer.WriteNumber("outputTokens", finished.Usage.OutputTokenCount);
                            }

                            writer.WriteNumber("contextWindow", finished.ContextWindow);
                        });
                    break;
            }
        }
    }

    private void EmitChild(ChildEventReceived child)
    {
        string? type = null;
        string? text = null;
        switch (child.Event)
        {
            case StreamReceived { StreamEvent: ChatTextDelta delta }:
                type = "child_text";
                text = delta.Text;
                break;
            case StreamReceived { StreamEvent: ChatReasoningTextDelta delta } when _showThinking:
                type = "child_reasoning";
                text = delta.Text;
                break;
            case ErrorWritten error:
                type = "child_error";
                text = error.Text;
                break;
            case RetryScheduled retry:
                type = "child_retry";
                text = $"Attempt {retry.Attempt.Attempt}: {retry.Attempt.Message}";
                break;
            case ToolCallsIssued calls:
                type = "child_tools";
                text = string.Join(", ", calls.Calls.Select(call => call.Name));
                break;
            case ToolResultsReceived results:
                type = "child_results";
                text = string.Join("\n", results.Results.Select(result => result.Text));
                break;
            default:
                break;
        }
        if (type is null)
        {
            return;
        }
        Emit(type, writer =>
        {
            writer.WriteString("childSessionID", child.ChildSessionId);
            writer.WriteString("invocationID", child.InvocationId);
            writer.WriteString("text", ImageMarkerText.Display(text ?? string.Empty));
        });
    }

    private void Apply(ChatStreamEvent streamEvent)
    {
        switch (streamEvent)
        {
            case ChatTextDelta text when text.Text.Length > 0:
                _reply.Append(text.Text);
                break;
            case ChatReasoningTextDelta reasoning when _showThinking && reasoning.Text.Length > 0:
                _thinking.Append(reasoning.Text);
                break;
        }
    }

    private void FlushSegments()
    {
        if (_thinking.Length > 0)
        {
            var thinking = _thinking.ToString().TrimEnd();
            _thinking.Clear();
            if (thinking.Length > 0)
            {
                Emit("reasoning", writer => writer.WriteString("text", thinking));
            }
        }

        if (_reply.Length == 0)
        {
            return;
        }

        var text = _reply.ToString().TrimEnd();
        _reply.Clear();
        if (text.Length > 0)
        {
            Emit("text", writer => writer.WriteString("text", text));
        }
    }

    private void EmitTool(ToolCall? call, ToolResult result)
    {
        Emit(
            "tool_use",
            writer =>
            {
                if (call is not null)
                {
                    writer.WriteString("callId", call.CallId);
                    writer.WriteString("name", call.Name);
                    WriteArguments(writer, call.Arguments);
                }
                else if (!string.IsNullOrEmpty(result.CallId))
                {
                    writer.WriteString("callId", result.CallId);
                }

                writer.WriteString(
                    "status",
                    result.Status == ToolResultStatus.Success ? "success" : "failure");
                writer.WriteString(
                    "output",
                    result.Text.Replace("\r\n", "\n", StringComparison.Ordinal));
            });
    }

    private static void WriteArguments(Utf8JsonWriter writer, string? arguments)
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

    private ToolCall? Take(string? callId)
    {
        if (!string.IsNullOrEmpty(callId))
        {
            var index = _calls.FindIndex(call => call.CallId == callId);
            if (index >= 0)
            {
                var found = _calls[index];
                _calls.RemoveAt(index);
                return found;
            }
        }

        if (_calls.Count == 0)
        {
            return null;
        }

        var first = _calls[0];
        _calls.RemoveAt(0);
        return first;
    }

    private void Emit(string type, Action<Utf8JsonWriter>? write = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", type);
            writer.WriteString(
                "timestamp",
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            if (_sessionId.Length > 0)
            {
                writer.WriteString("sessionID", _sessionId);
            }

            write?.Invoke(writer);
            writer.WriteEndObject();
        }

        _output.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
    }
}
