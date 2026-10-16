namespace CrystalCode.Engine.Home;

/// <summary>A local user-message template, or a visible file-level failure.</summary>
public sealed record QuickPrompt(string Name, string Text, string? Error = null);
