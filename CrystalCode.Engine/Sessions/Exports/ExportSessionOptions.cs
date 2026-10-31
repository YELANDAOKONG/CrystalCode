namespace CrystalCode.Engine.Sessions.Exports;

/// <summary>
/// Parsed arguments for <c>/export</c>.
/// </summary>
public sealed record ExportSessionOptions(string Format, string? Path, bool IncludeSystem);
