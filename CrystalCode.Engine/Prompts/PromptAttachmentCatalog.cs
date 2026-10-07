namespace CrystalCode.Engine.Prompts;

internal sealed class PromptAttachmentCatalog
{
    private readonly IReadOnlyDictionary<string, PromptAttachmentDefinition> _attachments;

    public PromptAttachmentCatalog(
        IReadOnlyDictionary<string, PromptAttachmentDefinition> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        _attachments = attachments;
    }

    public IReadOnlyList<string> Names => [.. _attachments.Keys.Order(StringComparer.Ordinal)];

    public bool TryGet(string name, out PromptAttachmentDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _attachments.TryGetValue(name.Trim(), out definition!);
    }
}
