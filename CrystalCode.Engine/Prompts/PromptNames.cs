namespace CrystalCode.Engine.Prompts;

/// <summary>
/// File stems under <c>prompts/</c> in home and project <c>.crystal</c>.
/// </summary>
public static class PromptNames
{
    public const string Work = "work";

    public const string Plan = "plan";

    public const string Review = "review";

    public const string ReviewUser = "review.user";

    public const string Topic = "topic";

    public const string CompactionSystem = "compaction.system";

    public const string CompactionUser = "compaction.user";

    public const string ImageSystem = "image.system";

    public const string ImageUser = "image.user";

    public static IReadOnlyList<string> Overlay { get; } =
    [
        Work,
        Plan,
        Review,
        ReviewUser,
        Topic,
        CompactionSystem,
        CompactionUser,
        ImageSystem,
        ImageUser
    ];
}
