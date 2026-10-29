namespace CrystalCode.Engine.Home;

internal sealed class ImageTrimDocument
{
    public bool? Enabled { get; set; }

    public int? KeepRecent { get; set; }

    public int? Slack { get; set; }

    public long? MaxBytes { get; set; }

    public bool? IncludeUserImages { get; set; }
}
