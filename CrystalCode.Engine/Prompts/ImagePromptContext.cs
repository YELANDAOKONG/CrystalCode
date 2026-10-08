namespace CrystalCode.Engine.Prompts;

/// <summary>
/// The request text placed into an image-description user template.
/// </summary>
public sealed record ImagePromptContext
{
    public ImagePromptContext(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        Question = question.Trim();
    }

    public string Question { get; }
}
