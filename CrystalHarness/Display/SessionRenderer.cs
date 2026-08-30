using System.Diagnostics;

using Crystal;
using Crystal.Chat;
using Crystal.Tools;

using CrystalHarness.Approvals;
using CrystalHarness.Sessions;

using Spectre.Console;

namespace CrystalHarness.Display;

/// <summary>
/// Scrollback + chrome for one session. Sequential writes, no Live shell.
/// </summary>
public sealed class SessionRenderer : ITurnObserver
{
    private readonly object _gate = new();
    private readonly LineEditor _editor = new();
    private string? _streamKind;
    private Stopwatch? _turnClock;

    public void BeginTurn()
    {
        lock (_gate)
        {
            CloseStreamUnlocked();
            _turnClock = Stopwatch.StartNew();
        }
    }

    public void WriteHeader(
        string model,
        string workspaceRoot,
        bool planMode,
        ApprovalMode approval)
    {
        lock (_gate)
        {
            CloseStreamUnlocked();
            var mode = planMode ? "plan" : "work";
            var modeColor = planMode ? Theme.Plan : Theme.Work;
            AnsiConsole.MarkupLine(
                $"[{Theme.Chrome}]{MarkupText.Escape(model)}  ·  [/]"
                + $"[{modeColor}]{mode}[/]"
                + $"[{Theme.Chrome}]  ·  {MarkupText.Escape(approval.Value)}  ·  "
                + $"{MarkupText.Escape(PathDisplay.Shorten(workspaceRoot))}[/]");
            WriteRule();
            AnsiConsole.WriteLine();
        }
    }

    public void WriteUser(string text)
    {
        lock (_gate)
        {
            CloseStreamUnlocked();
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                AnsiConsole.MarkupLine($"[{Theme.User}]  {MarkupText.Escape(line)}[/]");
            }

            AnsiConsole.WriteLine();
        }
    }

    public void WriteNote(string text)
    {
        lock (_gate)
        {
            CloseStreamUnlocked();
            AnsiConsole.MarkupLine($"[{Theme.Chrome}]  {MarkupText.Escape(text)}[/]");
        }
    }

    public void WriteError(string text)
    {
        lock (_gate)
        {
            CloseStreamUnlocked();
            AnsiConsole.MarkupLine($"[{Theme.Fail}]  {MarkupText.Escape(text)}[/]");
        }
    }

    public void WriteHelp()
    {
        lock (_gate)
        {
            CloseStreamUnlocked();
            AnsiConsole.MarkupLine($"[{Theme.Chrome}]  tab         plan / work[/]");
            AnsiConsole.MarkupLine($"[{Theme.Chrome}]  /plan       same as tab[/]");
            AnsiConsole.MarkupLine($"[{Theme.Chrome}]  /approval   default | autoedit | review | full[/]");
            AnsiConsole.MarkupLine($"[{Theme.Chrome}]  /cd         show or set workspace[/]");
            AnsiConsole.MarkupLine($"[{Theme.Chrome}]  /status     turns, tokens, mode[/]");
            AnsiConsole.MarkupLine($"[{Theme.Chrome}]  /clear      new conversation[/]");
            AnsiConsole.MarkupLine($"[{Theme.Chrome}]  /quit       exit[/]");
            AnsiConsole.MarkupLine($"[{Theme.Chrome}]  ctrl+c      stop turn; twice at idle exits[/]");
        }
    }

    public void WriteStatus(
        SessionLedger ledger,
        string workspaceRoot,
        bool planMode,
        ApprovalMode approval,
        int contextWindow)
    {
        lock (_gate)
        {
            CloseStreamUnlocked();
            var mode = planMode ? "plan" : "work";
            AnsiConsole.MarkupLine(
                $"[{Theme.Chrome}]  {mode}  ·  {approval.Value}  ·  "
                + $"{MarkupText.Escape(PathDisplay.Shorten(workspaceRoot))}[/]");
            AnsiConsole.MarkupLine(
                $"[{Theme.Chrome}]  {ledger.UserTurns} turns  ·  "
                + $"{ledger.ModelCalls} model  ·  {ledger.ToolCalls} tools  ·  "
                + $"{FormatUsage(ledger.Usage, contextWindow)}[/]");
        }
    }

    public void WriteTurnFooter(
        TurnResult result,
        SessionLedger ledger,
        int contextWindow)
    {
        lock (_gate)
        {
            CloseStreamUnlocked();
            if (result.StopReason != TurnStopReason.Completed)
            {
                AnsiConsole.MarkupLine(
                    $"[{Theme.Plan}]  {MarkupText.Escape(result.StopReason.Value)}[/]");
            }

            var elapsed = _turnClock is null
                ? string.Empty
                : "  ·  " + FormatElapsed(_turnClock.Elapsed);
            AnsiConsole.MarkupLine(
                $"[{Theme.Chrome}]  {FormatUsage(result.Usage, contextWindow)}  ·  "
                + $"{result.ToolCallCount} tools{elapsed}[/]");
            AnsiConsole.WriteLine();
        }
    }

    public void OnStreamEvent(ChatStreamEvent streamEvent)
    {
        lock (_gate)
        {
            switch (streamEvent)
            {
                case ChatReasoningTextDelta reasoning when reasoning.Text.Length > 0:
                    OpenUnlocked("thinking");
                    AnsiConsole.Markup($"[{Theme.Thinking}]{MarkupText.Escape(reasoning.Text)}[/]");
                    break;
                case ChatTextDelta text when text.Text.Length > 0:
                    OpenUnlocked("assistant");
                    AnsiConsole.Markup(MarkupText.Escape(text.Text));
                    break;
                case ChatToolCallDelta toolCall:
                    OpenUnlocked("tool");
                    if (toolCall.NameDelta.Length > 0)
                    {
                        AnsiConsole.Markup($"[{Theme.Tool}]{MarkupText.Escape(toolCall.NameDelta)}[/]");
                    }

                    if (toolCall.ArgumentsDelta.Length > 0 && toolCall.ArgumentsDelta is not "{}")
                    {
                        AnsiConsole.Markup(
                            $"[{Theme.Chrome}] {MarkupText.Escape(ApprovalCard.CompactArguments(toolCall.ArgumentsDelta))}[/]");
                    }

                    break;
                default:
                    break;
            }
        }
    }

    public void OnModelRoundClosed()
    {
        lock (_gate)
        {
            CloseStreamUnlocked();
        }
    }

    public void OnToolResults(IReadOnlyList<ToolResult> results)
    {
        lock (_gate)
        {
            CloseStreamUnlocked();
            foreach (var result in results)
            {
                var color = result.Status == ToolResultStatus.Success ? Theme.Ok : Theme.Fail;
                var first = FirstLine(result.Text);
                AnsiConsole.MarkupLine(
                    $"[{color}]        {MarkupText.Escape(first)}[/]");
            }
        }
    }

    public void CloseStream()
    {
        lock (_gate)
        {
            CloseStreamUnlocked();
        }
    }

    public Task<string> ReadInputAsync(
        bool planMode,
        Func<bool> togglePlan,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            CloseStreamUnlocked();
        }

        return _editor.ReadAsync(planMode, togglePlan, cancellationToken);
    }

    public static void WriteRule()
    {
        var width = 80;
        try
        {
            width = Math.Max(Console.WindowWidth - 1, 16);
        }
        catch (IOException)
        {
        }

        AnsiConsole.MarkupLine($"[{Theme.Rule}]{new string('-', width)}[/]");
    }

    private void OpenUnlocked(string kind)
    {
        if (_streamKind == kind)
        {
            return;
        }

        CloseStreamUnlocked();
        if (kind == "thinking")
        {
            AnsiConsole.Markup($"[{Theme.Thinking}]  [/]");
        }
        else if (kind == "tool")
        {
            AnsiConsole.Markup($"[{Theme.Tool}]  [/]");
        }
        else
        {
            Console.Write("  ");
        }

        _streamKind = kind;
    }

    private void CloseStreamUnlocked()
    {
        if (_streamKind is null)
        {
            return;
        }

        Console.WriteLine();
        if (_streamKind is "thinking" or "tool")
        {
            Console.WriteLine();
        }

        _streamKind = null;
    }

    private static string FirstLine(string text)
    {
        var normalized = text.Replace("\r\n", "\n");
        var end = normalized.IndexOf('\n');
        var line = end < 0 ? normalized : normalized[..end];
        return line.Length <= 100 ? line : line[..97] + "...";
    }

    private static string FormatUsage(TokenUsage? usage, int contextWindow)
    {
        if (usage is null)
        {
            return "ctx --";
        }

        var percent = contextWindow <= 0
            ? 0
            : Math.Clamp((int)(usage.TotalTokenCount * 100 / contextWindow), 0, 99);
        return $"ctx {percent}%  ·  {usage.InputTokenCount} in / {usage.OutputTokenCount} out";
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 10)
        {
            return $"{elapsed.TotalSeconds:0.0}s";
        }

        return $"{(int)elapsed.TotalSeconds}s";
    }
}
