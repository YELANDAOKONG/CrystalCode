namespace CrystalCode.Tools;

/// <summary>
/// One question in an operator prompt.
/// </summary>
public sealed record UserQuestion(
    string Header,
    string Text,
    IReadOnlyList<QuestionOption> Options,
    bool Multiple,
    bool Custom);
