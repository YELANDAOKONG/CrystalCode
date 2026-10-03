using System.Text;

using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Sessions;
using CrystalCode.Terminal;

namespace CrystalCode.Run;

/// <summary>
/// Plain-text transcript for one unattended run. Thinking text is kept only
/// when requested. The observer is called from the turn thread.
/// </summary>
internal sealed class RunLog : IRunLog
{
    private readonly TextWriter _output;
    private readonly bool _showThinking;
    private readonly object _gate = new();
    private readonly StringBuilder _reply = new();
    private readonly StringBuilder _thinking = new();

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
                _output.WriteLine("Stopped  " + status);
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
                    foreach (var call in issued.Calls)
                    {
                        _output.WriteLine("Tool  " + Summary(call));
                    }

                    break;
                case ToolResultsReceived received:
                    Flush();
                    foreach (var result in received.Results)
                    {
                        var label = result.Status == ToolResultStatus.Failure
                            ? "Error"
                            : "Result";
                        WriteBlock(label, result.Text);
                    }

                    break;
                case ErrorWritten error:
                    Flush();
                    WriteBlock("Error", error.Text);
                    break;
                case NoteWritten note:
                    Flush();
                    WriteBlock("Note", note.Text);
                    break;
                case TurnFinished finished:
                    Flush();
                    StopReason = finished.Result.StopReason;
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
        }
    }

    private void Flush()
    {
        if (_thinking.Length > 0)
        {
            WriteBlock("Thinking", _thinking.ToString());
            _thinking.Clear();
        }

        if (_reply.Length == 0)
        {
            return;
        }

        var text = _reply.ToString().TrimEnd();
        _reply.Clear();
        if (text.Length > 0)
        {
            _output.WriteLine(text);
        }
    }

    private void WriteBlock(string label, string text)
    {
        var body = text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        _output.WriteLine(label);
        if (body.Length > 0)
        {
            _output.WriteLine(body);
        }
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
