namespace CrystalHarness.Home;

internal sealed class SettingsDocument
{
    public string? Provider { get; set; }

    public string? Model { get; set; }

    public string? Approval { get; set; }

    public string? ThinkingEffort { get; set; }

    public double? CompactionThreshold { get; set; }

    public Dictionary<string, ProviderDocument>? Providers { get; set; }
}
