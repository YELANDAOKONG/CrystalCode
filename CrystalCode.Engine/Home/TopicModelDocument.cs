namespace CrystalCode.Engine.Home;

/// <summary>The <c>topicModel</c> object in <c>config.json</c>.</summary>
internal sealed class TopicModelDocument
{
    public bool? Enabled { get; set; }

    public string? Provider { get; set; }

    public string? Model { get; set; }

    public string? ThinkingEffort { get; set; }

    public string? ServiceTier { get; set; }

    public string? Speed { get; set; }

    public int? ContextWindow { get; set; }
}
