namespace CrystalCode.Home;

/// <summary>
/// Runtime facts stored in a session export JSON file.
/// </summary>
public sealed class SessionExportRuntimeDocument
{
    public string Workspace { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string ModelLine { get; set; } = string.Empty;

    public string PromptSet { get; set; } = string.Empty;

    public bool PlanMode { get; set; }
}
