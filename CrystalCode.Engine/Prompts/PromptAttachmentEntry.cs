namespace CrystalCode.Engine.Prompts;

/// <summary>
/// One attachment in list order. A null source is unused; every listed row was discovered.
/// </summary>
internal sealed record PromptAttachmentEntry(
    string Name,
    string Title,
    string Description,
    PromptAttachmentSource Source,
    bool Enabled,
    bool Effective,
    int? Order);
