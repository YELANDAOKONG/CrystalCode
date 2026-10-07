namespace CrystalCode.Engine.Prompts;

internal sealed record PromptAttachmentDefinition(
    string Name,
    string Directory,
    PromptAttachmentSource Source,
    bool ReplacedHome);
