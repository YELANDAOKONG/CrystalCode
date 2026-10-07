namespace CrystalCode.Engine.Version;

/// <summary>
/// Build identity shown by <c>crystal version</c>.
/// A null or blank value is omitted from the report.
/// </summary>
public sealed record BuildIdentity(
    string? ProductRevision,
    string? LibraryRevision,
    string? SdkVersion,
    string? Configuration,
    string? Runtime,
    string? OperatingSystem);
