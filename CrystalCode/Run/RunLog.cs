using System.Text;

using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;
using CrystalCode.Terminal;

namespace CrystalCode.Run;

/// <summary>
/// Readable plain-text trace for one unattended run. Tool bodies are shortened.
/// Thinking text is kept only when requested. The observer is called from the
/// turn thread.
/// </summary>
internal sealed class RunLog : IRunLog
{
    private readonly TextWriter _output;
    private readonly bool _showThinking;
    private readonly object _gate = new();
    private readonly StringBuilder _reply = new();
    private readonly StringBuilder _thinking = new();
    private readonly List<ToolCall> _calls = [];
    private bool _wrote;

    public RunLog(TextWriter output, bool showThinking)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
        _showThinking = showThinking;
    }

    public TurnStopReason? StopReason { get; private set; }

    public void BindSession(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
    }

    public void WriteEpilogue(string? status, string hint)
    {
        ArgumentNullException.ThrowIfNull(hint);
        lock (_gate)
        {
            Flush();
            if (status is not null)
            {
                WriteBreak();
                _output.WriteLine("Stopped  " + status);
                _wrote = true;
            }

            _output.WriteLine();
            _output.WriteLine(hint);
        }
    }

    public void OnEvent(SessionEvent sessionEvent)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        lock (_gate)
        {
            switch (sessionEvent)
            {
                case StreamReceived received:
                    Apply(received.StreamEvent);
                    break;
                case ToolCallsIssued issued:
                    Flush();
                    _calls.AddRange(issued.Calls);
                    break;
                case ToolResultsReceived received:
                    Flush();
                    foreach (var result in received.Results)
                    {
                        WriteTool(Take(result.CallId), result);
                    }

                    break;
                case ErrorWritten error:
                    Flush();
                    WriteSection("Error", error.Text);
                    break;
                case NoteWritten note:
                    Flush();
                    WriteSection("Note", note.Text);
                    break;
                case TurnFinished finished:
                    Flush();
                    StopReason = finished.Result.StopReason;
                    break;
                default:
                    break;
            }
        }
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
            default:
                break;
        }
    }

    private void Flush()
    {
        if (_thinking.Length > 0)
        {
            var thinking = _thinking.ToString();
            _thinking.Clear();
            WriteSection("Thinking", thinking);
        }

        if (_reply.Length == 0)
        {
            return;
        }

        var text = _reply.ToString();
        _reply.Clear();
        WriteParagraph(text);
    }

    private void WriteTool(ToolCall? call, ToolResult result)
    {
        var summary = call is null ? "Tool" : Summary(call);
        var body = Indent(RunToolBody.Format(call?.Name, result.Status, result.Text));
        WriteSection(summary, body);
    }

    private void WriteParagraph(string text)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            return;
        }

        WriteBreak();
        WriteNormalized(normalized);
        _wrote = true;
    }

    private void WriteSection(string label, string body)
    {
        var normalized = Normalize(body);
        WriteBreak();
        _output.WriteLine(label);
        if (normalized.Length > 0)
        {
            WriteNormalized(normalized);
        }

        _wrote = true;
    }

    private void WriteBreak()
    {
        if (_wrote)
        {
            _output.WriteLine();
        }
    }

    private void WriteNormalized(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            _output.WriteLine(line);
        }
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');

    private static string Indent(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var lines = text.Split('\n');
        var builder = new StringBuilder();
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                builder.Append('\n');
            }

            if (lines[index].Length > 0)
            {
                builder.Append("  ");
                builder.Append(lines[index]);
            }
        }

        return builder.ToString();
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

    private static string Summary(ToolCall call)
    {
        var name = string.IsNullOrWhiteSpace(call.Name) ? "tool" : call.Name;
        try
        {
            return ToolCallText.Summary(name, call.Arguments ?? string.Empty);
        }
        catch (ArgumentException)
        {
            return name;
        }
    }
}
