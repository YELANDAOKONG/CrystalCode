using Spectre.Console;
using Spectre.Console.Rendering;

using CrystalHarness.Tools;

namespace CrystalHarness.Display;

/// <summary>
/// Asks a question in the overlay. Digits select; anything else uses the composer.
/// </summary>
public sealed class QuestionPrompt : IUserPrompt
{
    private readonly SessionRenderer _renderer;

    public QuestionPrompt(SessionRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    public async ValueTask<string> AskAsync(
        string question,
        IReadOnlyList<string>? options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        _renderer.CloseStream();
        _renderer.PauseComposer();
        _renderer.SetOverlay(CardWidget(question, options));
        try
        {
            if (options is { Count: > 0 })
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var key = await _renderer.ReadKeyAsync(cancellationToken);
                    if (TryDigit(key, options.Count, out var index))
                    {
                        return options[index];
                    }

                    if (!char.IsControl(key.KeyChar))
                    {
                        _renderer.SeedComposer(key.KeyChar.ToString());
                        break;
                    }
                }
            }

            var answer = await _renderer.ReadInputAsync(
                planMode: false,
                static () => false,
                cancellationToken);
            if (options is { Count: > 0 }
                && int.TryParse(answer.Trim(), out var number)
                && number >= 1
                && number <= options.Count)
            {
                return options[number - 1];
            }

            return answer;
        }
        finally
        {
            _renderer.ClearOverlay();
            _renderer.ResumeComposer();
        }
    }

    private static IRenderable CardWidget(string question, IReadOnlyList<string>? options)
    {
        var blocks = new List<IRenderable>
        {
            new Markup($"[{Theme.Review} bold]{MarkupText.Escape(question)}[/]")
        };
        if (options is { Count: > 0 })
        {
            var grid = new Grid();
            grid.AddColumn(new GridColumn().PadRight(2));
            grid.AddColumn();
            for (var index = 0; index < options.Count; index++)
            {
                grid.AddRow(
                    new Markup($"[{Theme.Accent}]{index + 1}.[/]"),
                    new Markup($"[{Theme.User}]{MarkupText.Escape(options[index])}[/]"));
            }

            blocks.Add(grid);
            blocks.Add(new Markup($"[{Theme.Muted}]Type a number (1-{options.Count}) or an answer[/]"));
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

    private static bool TryDigit(ConsoleKeyInfo key, int count, out int index)
    {
        index = -1;
        if (key.Key is < ConsoleKey.D1 or > ConsoleKey.D9)
        {
            return false;
        }

        index = key.Key - ConsoleKey.D1;
        return index >= 0 && index < count;
    }
}
