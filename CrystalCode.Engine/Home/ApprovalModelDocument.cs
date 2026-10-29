namespace CrystalCode.Engine.Home;

internal sealed class ApprovalModelDocument
{
    public bool? Enabled { get; set; }

    public string? Provider { get; set; }

    public string? Model { get; set; }

    public string? ThinkingEffort { get; set; }

    public string? ServiceTier { get; set; }

    public string? Speed { get; set; }

    public int? ContextWindow { get; set; }
}
