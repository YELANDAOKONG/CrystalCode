using System.Globalization;
using System.Text;
using System.Text.Json;

using Crystal;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Run;

/// <summary>
/// JSON Lines transcript for one unattended run. Each stdout line is one
/// object with <c>type</c>, <c>timestamp</c>, and <c>sessionID</c>. Child
/// events use the parent shapes under a <c>child_</c> type prefix and add
/// <c>childSessionID</c> and <c>invocationID</c>. Thinking text is emitted
/// only when requested. The observer is called from the turn thread and from
/// concurrent child invocations.
/// </summary>
internal sealed class RunJsonLog : IRunLog
{
    private const string ChildPrefix = "child_";

    private readonly TextWriter _output;
    private readonly bool _showThinking;
    private readonly object _gate = new();
    private readonly RunJsonStream _parent;
    private readonly Dictionary<string, RunJsonStream> _children = new(StringComparer.Ordinal);
    private string _sessionId = string.Empty;
    private bool _stepOpen;

    public RunJsonLog(TextWriter output, bool showThinking)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
        _showThinking = showThinking;
        _parent = new RunJsonStream(Emit, showThinking);
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
            foreach (var child in _children.Values)
            {
                child.Flush();
            }

            _parent.Flush();
            if (_stepOpen)
            {
                // The turn faulted before it reported a result, so there are no counts.
                _stepOpen = false;
                Emit("step_finish", writer => writer.WriteString("reason", TurnStopReason.Failed.Value));
            }

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
                    WriteChildStatus(child);
                    break;
                case ChildEventReceived child:
                    ChildStream(child.ChildSessionId, child.InvocationId).Apply(child.Event);
                    break;
                case TurnStarted:
                    _stepOpen = true;
                    Emit("step_start");
                    break;
                case TurnFinished finished:
                    _parent.Flush();
                    _stepOpen = false;
                    StopReason = finished.Result.StopReason;
                    WriteStepFinish(finished);
                    break;
                default:
                    _parent.Apply(sessionEvent);
                    break;
            }
        }
    }

    private RunJsonStream ChildStream(string childSessionId, Guid invocationId)
    {
        if (!_children.TryGetValue(childSessionId, out var stream))
        {
            stream = new RunJsonStream(
                (type, write) => Emit(
                    ChildPrefix + type,
                    writer =>
                    {
                        WriteChildIdentity(writer, childSessionId, invocationId);
                        write(writer);
                    }),
                _showThinking);
            _children.Add(childSessionId, stream);
        }

        return stream;
    }

    private void WriteChildStatus(ChildSessionChanged changed)
    {
        var child = changed.Child;
        // A settled child flushes its last segment before its final status.
        // A later continuation of the same child is a new invocation.
        if (child.Status is not ("queued" or "running")
            && _children.Remove(child.Id, out var stream))
        {
            stream.Flush();
        }

        Emit(
            ChildPrefix + "status",
            writer =>
            {
                WriteChildIdentity(writer, child.Id, changed.InvocationId);
                writer.WriteString("agent", child.Agent);
                writer.WriteString("label", child.Label);
                writer.WriteString("status", child.Status);
                writer.WriteNumber("modelCalls", child.ModelCalls);
                writer.WriteNumber("toolCalls", child.ToolCalls);
                writer.WritePropertyName("images");
                writer.WriteStartArray();
                foreach (var image in child.Images)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("number", image.Number);
                    writer.WriteString("mimeType", image.MimeType);
                    writer.WriteString("contentHash", image.ContentHash);
                    writer.WriteString("source", "child_tool");
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                if (child.CumulativeUsage is { } usage)
                {
                    WriteUsage(
                        writer,
                        "usage",
                        usage.InputTokenCount,
                        usage.OutputTokenCount,
                        usage.ReasoningTokenCount);
                }
            });
    }

    private void WriteStepFinish(TurnFinished finished)
    {
        Emit(
            "step_finish",
            writer =>
            {
                writer.WriteString("reason", finished.Result.StopReason.Value);
                writer.WriteNumber("modelCalls", finished.Result.ModelCallCount);
                writer.WriteNumber("toolCalls", finished.Result.ToolCallCount);
                // Usage is omitted when a provider did not report every round.
                WriteUsage(writer, "usage", finished.Result.AccumulatedUsage);
                WriteUsage(writer, "lastRequestUsage", finished.Usage);
                WriteUsage(writer, "cumulativeUsage", finished.CumulativeUsage);
                writer.WriteNumber("contextWindow", finished.ContextWindow);
            });
    }

    private static void WriteChildIdentity(Utf8JsonWriter writer, string childSessionId, Guid invocationId)
    {
        writer.WriteString("childSessionID", childSessionId);
        writer.WriteString("invocationID", invocationId);
    }

    private static void WriteUsage(Utf8JsonWriter writer, string name, TokenUsage? usage)
    {
        if (usage is null)
        {
            return;
        }

        WriteUsage(
            writer,
            name,
            usage.InputTokenCount,
            usage.OutputTokenCount,
            usage.ReasoningTokenCount);
    }

    private static void WriteUsage(
        Utf8JsonWriter writer,
        string name,
        long inputTokens,
        long outputTokens,
        long? reasoningTokens)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("inputTokens", inputTokens);
        writer.WriteNumber("outputTokens", outputTokens);
        if (reasoningTokens is long reasoning)
        {
            writer.WriteNumber("reasoningTokens", reasoning);
        }

        writer.WriteEndObject();
    }

    private void Emit(string type) => Emit(type, static _ => { });

    private void Emit(string type, Action<Utf8JsonWriter> write)
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

            write(writer);
            writer.WriteEndObject();
        }

        _output.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
    }
}
