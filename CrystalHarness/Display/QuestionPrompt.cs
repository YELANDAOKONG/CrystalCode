using CrystalHarness.Tools;

using Spectre.Console;

namespace CrystalHarness.Display;

/// <summary>
/// Asks a question inline. Numbered choices when the model supplied options.
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
        AnsiConsole.WriteLine();
        SessionRenderer.WriteRule();
        AnsiConsole.MarkupLine($"[{Theme.Review}]  {MarkupText.Escape(question)}[/]");
        if (options is { Count: > 0 })
        {
            for (var index = 0; index < options.Count; index++)
            {
                AnsiConsole.MarkupLine(
                    $"[{Theme.Chrome}]  {index + 1}  {MarkupText.Escape(options[index])}[/]");
            }

            AnsiConsole.MarkupLine($"[{Theme.Chrome}]  number or type an answer[/]");
        }

        SessionRenderer.WriteRule();
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
}
