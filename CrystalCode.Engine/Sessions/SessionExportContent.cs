namespace CrystalCode.Engine.Sessions;

/// <summary>
/// An export rendered in memory for a front end to deliver, for example as a
/// download. <see cref="FileName"/> is a suggested name with no directory.
/// </summary>
public sealed record SessionExportContent(string FileName, string MediaType, string Text);
