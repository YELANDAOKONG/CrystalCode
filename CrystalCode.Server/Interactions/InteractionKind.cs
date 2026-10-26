namespace CrystalCode.Server.Interactions;

/// <summary>Kinds of question the engine asks the operator through the server.</summary>
public static class InteractionKind
{
    public const string Approval = "approval";

    public const string Question = "question";

    public const string Trust = "trust";

    public const string ChooseSession = "choose_session";

    public const string ChooseChild = "choose_child";

    public static IReadOnlyList<string> All { get; } = [Approval, Question, Trust, ChooseSession, ChooseChild];
}
