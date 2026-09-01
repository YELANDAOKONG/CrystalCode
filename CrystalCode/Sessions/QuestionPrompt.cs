using Spectre.Console;
using Spectre.Console.Rendering;

using CrystalCode.Display.Input;
using CrystalCode.Display.Paint;
using CrystalCode.Tools;

namespace CrystalCode.Sessions;

/// <summary>
/// Asks one or more questions in an overlay with single, multiple, and custom answers.
/// </summary>
public sealed class QuestionPrompt : IUserPrompt
{
    private readonly SessionRenderer _renderer;

    public QuestionPrompt(SessionRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    public async ValueTask<QuestionResponse> AskAsync(
        IReadOnlyList<UserQuestion> questions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(questions);
        var flow = new QuestionFlow(questions);
        _renderer.CloseStream();
        _renderer.PauseComposer();
        _renderer.SetProgress(ProgressText.WaitingForAnswer);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _renderer.SetOverlay(CardWidget(flow));
                var key = await _renderer.ReadKeyAsync(cancellationToken);
                if (key.Key == ConsoleKey.Escape)
                {
                    return new QuestionResponse([], IsRejected: true);
                }

                if (flow.IsConfirm)
                {
                    if (key.Key == ConsoleKey.Enter)
                    {
                        return new QuestionResponse(flow.Answers, IsRejected: false);
                    }

                    HandleTabKey(flow, key);
                    continue;
                }

                if (TryChoiceNumber(key, flow.ChoiceCount, out var choice))
                {
                    flow.MoveToChoice(choice);
                    var response = await ApplySelectionAsync(flow, cancellationToken);
                    if (response is not null)
                    {
                        return response;
                    }

                    continue;
                }

                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                    case ConsoleKey.K:
                        flow.MoveSelection(-1);
                        break;
                    case ConsoleKey.DownArrow:
                    case ConsoleKey.J:
                        flow.MoveSelection(1);
                        break;
                    case ConsoleKey.LeftArrow:
                    case ConsoleKey.H:
                        flow.MoveTab(-1);
                        break;
                    case ConsoleKey.RightArrow:
                    case ConsoleKey.L:
                    case ConsoleKey.Tab:
                        flow.MoveTab(key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1);
                        break;
                    case ConsoleKey.Enter:
                    case ConsoleKey.Spacebar when flow.Current is { Multiple: true }:
                        var response = await ApplySelectionAsync(flow, cancellationToken);
                        if (response is not null)
                        {
                            return response;
                        }

                        break;
                    default:
                        break;
                }
            }
        }
        finally
        {
            _renderer.ClearOverlay();
            _renderer.ResumeComposer();
            _renderer.SetProgress(ProgressText.WaitingForModel);
        }
    }

    private async ValueTask<QuestionResponse?> ApplySelectionAsync(
        QuestionFlow flow,
        CancellationToken cancellationToken)
    {
        var action = flow.SelectCurrent();
        if (action.EditCustom)
        {
            var draft = _renderer.ReplaceComposer(flow.CurrentCustom);
            string answer;
            try
            {
                answer = await _renderer.ReadInputAsync(
                    planMode: false,
                    static () => false,
                    cancellationToken);
            }
            finally
            {
                _renderer.ReplaceComposer(draft);
            }

            action = flow.SaveCustom(answer);
        }

        return action.Submit
            ? new QuestionResponse(flow.Answers, IsRejected: false)
            : null;
    }

    private static void HandleTabKey(QuestionFlow flow, InputKey key)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
            case ConsoleKey.H:
                flow.MoveTab(-1);
                break;
            case ConsoleKey.RightArrow:
            case ConsoleKey.L:
                flow.MoveTab(1);
                break;
            case ConsoleKey.Tab:
                flow.MoveTab(key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1);
                break;
            default:
                break;
        }
    }

    private static IRenderable CardWidget(QuestionFlow flow)
    {
        var blocks = new List<IRenderable>();
        if (!flow.IsSingle)
        {
            blocks.Add(TabWidget(flow));
        }

        if (flow.IsConfirm)
        {
            blocks.Add(ConfirmWidget(flow));
            blocks.Add(new Markup(
                $"[{Theme.Muted}]Enter Submit  Left/Right or Tab Review  Esc Dismiss[/]"));
        }
        else
        {
            blocks.Add(QuestionWidget(flow));
        }

        var panel = new Panel(new Rows(blocks))
        {
            Header = new PanelHeader("Question"),
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(Theme.Chrome),
            Padding = new Padding(1, 0, 1, 0),
            Expand = true
        };
        return new Padder(panel, new Padding(2, 0, 0, 0));
    }

    private static IRenderable TabWidget(QuestionFlow flow)
    {
        var tabs = new List<string>();
        for (var index = 0; index < flow.Questions.Count; index++)
        {
            var header = MarkupText.Escape(flow.Questions[index].Header);
            tabs.Add(index == flow.Tab
                ? $"[{Theme.Accent} bold]{header}[/]"
                : $"[{Theme.Muted}]{header}[/]");
        }

        tabs.Add(flow.IsConfirm
            ? $"[{Theme.Accent} bold]Confirm[/]"
            : $"[{Theme.Muted}]Confirm[/]");
        return new Markup(string.Join($" [{Theme.Chrome}]|[/] ", tabs));
    }

    private static IRenderable QuestionWidget(QuestionFlow flow)
    {
        var current = flow.Current!;
        var blocks = new List<IRenderable>
        {
            new Markup($"[{Theme.Review} bold]{MarkupText.Escape(current.Text)}[/]")
        };
        var grid = new Grid();
        grid.AddColumn(new GridColumn().PadRight(1));
        grid.AddColumn(new GridColumn().PadRight(2));
        grid.AddColumn();
        for (var index = 0; index < current.Options.Count; index++)
        {
            var option = current.Options[index];
            var cursor = index == flow.Selected ? ">" : " ";
            var marker = current.Multiple
                ? $"[{(flow.IsPicked(option.Label) ? "x" : " ")}]"
                : $"{index + 1}.";
            grid.AddRow(
                new Markup($"[{Theme.Accent}]{cursor}[/]"),
                new Markup($"[{Theme.User}]{MarkupText.Escape(marker + " " + option.Label)}[/]"),
                new Markup($"[{Theme.Muted}]{MarkupText.Escape(option.Description)}[/]"));
        }

        if (current.Custom)
        {
            var index = current.Options.Count;
            var cursor = index == flow.Selected ? ">" : " ";
            var picked = flow.CurrentCustom.Length > 0 && flow.IsPicked(flow.CurrentCustom);
            var marker = current.Multiple ? $"[{(picked ? "x" : " ")}]" : $"{index + 1}.";
            var description = flow.CurrentCustom.Length == 0
                ? "Enter a free-form answer"
                : flow.CurrentCustom;
            grid.AddRow(
                new Markup($"[{Theme.Accent}]{cursor}[/]"),
                new Markup($"[{Theme.User}]{MarkupText.Escape(marker + " Type your own answer")}[/]"),
                new Markup($"[{Theme.Muted}]{MarkupText.Escape(description)}[/]"));
        }

        blocks.Add(grid);
        var action = current.Multiple ? "Enter/Space Toggle" : "Enter Select";
        var navigation = flow.IsSingle ? string.Empty : "  Left/Right or Tab Questions";
        blocks.Add(new Markup(
            $"[{Theme.Muted}]Up/Down Move  {action}{navigation}  Esc Dismiss[/]"));
        return new Rows(blocks);
    }

    private static IRenderable ConfirmWidget(QuestionFlow flow)
    {
        var grid = new Grid();
        grid.AddColumn(new GridColumn().PadRight(2));
        grid.AddColumn();
        var answers = flow.Answers;
        for (var index = 0; index < flow.Questions.Count; index++)
        {
            var values = answers[index];
            var answer = values.Count == 0 ? "(not answered)" : string.Join(", ", values);
            grid.AddRow(
                new Markup($"[{Theme.User}]{MarkupText.Escape(flow.Questions[index].Header)}[/]"),
                new Markup($"[{(values.Count == 0 ? Theme.Muted : Theme.Review)}]{MarkupText.Escape(answer)}[/]"));
        }

        return grid;
    }

    private static bool TryChoiceNumber(InputKey key, int count, out int index)
    {
        index = key.Key switch
        {
            >= ConsoleKey.D1 and <= ConsoleKey.D9 => key.Key - ConsoleKey.D1,
            >= ConsoleKey.NumPad1 and <= ConsoleKey.NumPad9 => key.Key - ConsoleKey.NumPad1,
            _ => -1
        };
        return index >= 0 && index < count;
    }
}
