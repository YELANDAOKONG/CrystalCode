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
        _renderer.SetOverlay(CardLines(question, options));
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
        }
    }

    private static List<string> CardLines(string question, IReadOnlyList<string>? options)
    {
        var lines = new List<string> { question };
        if (options is { Count: > 0 })
        {
            for (var index = 0; index < options.Count; index++)
            {
                lines.Add($"{index + 1}  {options[index]}");
            }

            lines.Add("number or type an answer");
        }

        return lines;
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
