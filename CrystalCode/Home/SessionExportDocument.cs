namespace CrystalCode.Home;

/// <summary>
/// JSON shape for <c>/export json</c>.
/// </summary>
public sealed class SessionExportDocument
{
    public const string FormatName = "crystalcode.session.export";

    public const int FormatVersion = 1;

    public string Format { get; set; } = FormatName;

    public int Version { get; set; } = FormatVersion;

    public DateTimeOffset ExportedUtc { get; set; }

    public SessionDocument Session { get; set; } = new();

    public SessionExportRuntimeDocument Runtime { get; set; } = new();

    public string? System { get; set; }
}
