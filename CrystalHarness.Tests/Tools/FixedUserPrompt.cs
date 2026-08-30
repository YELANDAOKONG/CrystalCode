using CrystalHarness.Tools;

namespace CrystalHarness.Tests.Tools;

internal sealed class FixedUserPrompt : IUserPrompt
{
    private readonly string _answer;

    public FixedUserPrompt(string answer)
    {
        _answer = answer;
    }

    public ValueTask<string> AskAsync(
        string question,
        IReadOnlyList<string>? options,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastQuestion = question;
        LastOptions = options;
        return ValueTask.FromResult(_answer);
    }

    public string? LastQuestion { get; private set; }

    public IReadOnlyList<string>? LastOptions { get; private set; }
}
