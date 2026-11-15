using System.Collections.Immutable;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Plugins;

namespace CrystalCode.Engine.Prompts;

/// <summary>Resolved templates for describing an audio, video, or document input.</summary>
internal sealed record ResourceDescriptionPrompt(
    ResourceKind Kind,
    string SystemTemplate,
    string UserTemplate = "{{question}}")
{
    public static ImmutableArray<ResourceDescriptionPrompt> BuiltIns { get; } =
    [
        new(ResourceKind.Audio, AudioDescriptionPrompt.SystemText),
        new(ResourceKind.Video, VideoDescriptionPrompt.SystemText),
        new(ResourceKind.Document, DocumentDescriptionPrompt.SystemText)
    ];

    public string SystemName => ResourceKinds.Word(Kind) + ".system";

    public string UserName => ResourceKinds.Word(Kind) + ".user";

    public ImmutableArray<string> SystemAttachments { get; init; } = [];

    public ImmutableArray<string> UserAttachments { get; init; } = [];

    public string ComposeSystem(PromptContext context, PluginPlaceholderTable? placeholders = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var binding = new PromptBinding(context.WithMode(ResourceKinds.Word(Kind)), Placeholders: placeholders);
        return PromptSet.ComposeWithAttachments(SystemTemplate, SystemAttachments, binding);
    }

    public string ComposeUser(string? question, PluginPlaceholderTable? placeholders = null)
    {
        var request = string.IsNullOrWhiteSpace(question)
            ? $"Describe this {ResourceKinds.Word(Kind)}."
            : "Question: " + question.Trim();
        var binding = new PromptBinding(Placeholders: placeholders) { ResourceDescription = (Kind, request) };
        return PromptSet.ComposeWithAttachments(UserTemplate, UserAttachments, binding);
    }
}
