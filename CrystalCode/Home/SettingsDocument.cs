namespace CrystalCode.Home;

internal sealed class SettingsDocument
{
    public string? Provider { get; set; }

    public string? Model { get; set; }

    public string? Approval { get; set; }

    public string? ThinkingEffort { get; set; }

    public bool? Skills { get; set; }

    public bool? ExternalTools { get; set; }

    public ExternalToolApprovalDocument? ExternalToolApproval { get; set; }

    public bool? EstimatedTokens { get; set; }

    public bool? VerboseTools { get; set; }

    public bool? VerboseCommands { get; set; }

    public string? PromptSet { get; set; }

    public string? ExportDirectory { get; set; }

    public bool? CustomStatusLine { get; set; }

    public List<string>? StatusLine { get; set; }

    public double? CompactionThreshold { get; set; }

    public Dictionary<string, ProviderDocument>? Providers { get; set; }
}
