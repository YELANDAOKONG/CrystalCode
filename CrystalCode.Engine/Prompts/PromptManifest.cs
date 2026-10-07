namespace CrystalCode.Engine.Prompts;

/// <summary>
/// Display fields from one <c>prompt.json</c>. An empty title means the
/// directory name is the label. A null order sorts after numbered attachments.
/// </summary>
internal sealed record PromptManifest(
    string Title,
    string Description,
    bool Enabled,
    int? Order);
