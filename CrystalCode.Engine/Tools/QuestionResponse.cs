namespace CrystalCode.Engine.Tools;

/// <summary>
/// Ordered answers to an operator prompt, or an explicit dismissal.
/// <paramref name="IsUnavailable"/> marks a dismissal because no operator can
/// answer at all, as in an unattended run.
/// </summary>
public sealed record QuestionResponse(
    IReadOnlyList<IReadOnlyList<string>> Answers,
    bool IsRejected,
    bool IsUnavailable = false);
