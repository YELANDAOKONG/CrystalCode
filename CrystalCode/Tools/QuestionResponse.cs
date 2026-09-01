namespace CrystalCode.Tools;

/// <summary>
/// Ordered answers to an operator prompt, or an explicit dismissal.
/// </summary>
public sealed record QuestionResponse(
    IReadOnlyList<IReadOnlyList<string>> Answers,
    bool IsRejected);
