namespace CrystalCode.Engine.Events;

/// <summary>
/// One selectable value of a <see cref="SessionOption"/>. <see cref="Command"/>
/// is the exact slash command that selects it, so a front end changes the
/// value through the same validation, refusal, and save path as the terminal.
/// An empty command means the value can be shown but not chosen directly.
/// <see cref="Group"/> is an optional grouping label, such as a provider.
/// </summary>
public sealed record SessionOptionValue(
    string Value,
    string Name,
    string Command,
    string? Description = null,
    string? Group = null);
