namespace CrystalCode.Engine.Home;

internal sealed class ExtendedApprovalDocument
{
    public bool? Enabled { get; set; }

    public string? Provider { get; set; }

    public string? Model { get; set; }

    public string? ThinkingEffort { get; set; }

    public string? ServiceTier { get; set; }

    public string? Speed { get; set; }

    public int? ContextWindow { get; set; }

    public string? Trigger { get; set; }

    public bool? IncludePrimaryReview { get; set; }
}
