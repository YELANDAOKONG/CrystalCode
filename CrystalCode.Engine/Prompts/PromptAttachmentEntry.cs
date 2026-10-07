namespace CrystalCode.Engine.Prompts;

/// <summary>
/// One attachment in list order. A null source means the enabled name was not
/// discovered in Home or the current workspace.
/// </summary>
internal sealed record PromptAttachmentEntry(
    string Name,
    PromptAttachmentSource? Source,
    bool Enabled);
