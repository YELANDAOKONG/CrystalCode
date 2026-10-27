namespace CrystalCode.Bridge.Prompts;

/// <summary>The trusted marker of an attached image, or why it was not attached.</summary>
internal sealed record ImageAttachResult(string? Marker, string Error);
