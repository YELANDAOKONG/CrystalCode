using CrystalCode.Tools;

namespace CrystalCode.Sessions;

/// <summary>
/// Selection state for one question request. Rendering and terminal input stay outside it.
/// </summary>
internal sealed class QuestionFlow
{
    private readonly IReadOnlyList<UserQuestion> _questions;
    private readonly List<List<string>> _answers;
    private readonly string?[] _custom;

    public QuestionFlow(IReadOnlyList<UserQuestion> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);
        if (questions.Count == 0)
        {
            throw new ArgumentException("At least one question is required.", nameof(questions));
        }

        _questions = questions;
        _answers = questions.Select(static _ => new List<string>()).ToList();
        _custom = new string[questions.Count];
    }

    public int Tab { get; private set; }

    public int Selected { get; private set; }

    public bool IsSingle => _questions.Count == 1 && !_questions[0].Multiple;

    public bool IsConfirm => !IsSingle && Tab == _questions.Count;

    public IReadOnlyList<UserQuestion> Questions => _questions;

    public UserQuestion? Current => IsConfirm ? null : _questions[Tab];

    public string CurrentCustom => IsConfirm ? string.Empty : _custom[Tab] ?? string.Empty;

    public int ChoiceCount
    {
        get
        {
            var current = Current;
            return current is null ? 0 : current.Options.Count + (current.Custom ? 1 : 0);
        }
    }

    public bool IsCustomChoice => Current is { Custom: true } current
        && Selected == current.Options.Count;

    public IReadOnlyList<IReadOnlyList<string>> Answers =>
        _answers.Select(static answer => (IReadOnlyList<string>)answer.ToArray()).ToArray();

    public void MoveSelection(int delta)
    {
        if (ChoiceCount == 0)
        {
            return;
        }

        Selected = (Selected + delta + ChoiceCount) % ChoiceCount;
    }

    public bool MoveToChoice(int index)
    {
        if (index < 0 || index >= ChoiceCount)
        {
            return false;
        }

        Selected = index;
        return true;
    }

    public void MoveTab(int delta)
    {
        if (IsSingle)
        {
            return;
        }

        var tabCount = _questions.Count + 1;
        Tab = (Tab + delta + tabCount) % tabCount;
        Selected = 0;
    }

    public (bool EditCustom, bool Submit) SelectCurrent()
    {
        var current = Current;
        if (current is null)
        {
            return (false, true);
        }

        if (IsCustomChoice)
        {
            var custom = CurrentCustom;
            if (current.Multiple
                && custom.Length > 0
                && _answers[Tab].Contains(custom, StringComparer.Ordinal))
            {
                _answers[Tab].Remove(custom);
                return (false, false);
            }

            return (true, false);
        }

        var option = current.Options[Selected];
        if (current.Multiple)
        {
            Toggle(option.Label);
            return (false, false);
        }

        _answers[Tab] = [option.Label];
        return CompleteSingleChoice();
    }

    public (bool EditCustom, bool Submit) SaveCustom(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var current = Current;
        var answer = text.Trim();
        if (current is null || answer.Length == 0)
        {
            return (false, false);
        }

        var previous = _custom[Tab];
        if (previous is not null)
        {
            _answers[Tab].Remove(previous);
        }

        _custom[Tab] = answer;
        if (current.Multiple)
        {
            if (!_answers[Tab].Contains(answer, StringComparer.Ordinal))
            {
                _answers[Tab].Add(answer);
            }

            return (false, false);
        }

        _answers[Tab] = [answer];
        return CompleteSingleChoice();
    }

    public bool IsPicked(string answer) =>
        !IsConfirm && _answers[Tab].Contains(answer, StringComparer.Ordinal);

    private (bool EditCustom, bool Submit) CompleteSingleChoice()
    {
        if (IsSingle)
        {
            return (false, true);
        }

        MoveTab(1);
        return (false, false);
    }

    private void Toggle(string answer)
    {
        if (!_answers[Tab].Remove(answer))
        {
            _answers[Tab].Add(answer);
        }
    }
}
