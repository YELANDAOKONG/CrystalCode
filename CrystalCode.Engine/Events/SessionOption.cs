namespace CrystalCode.Engine.Events;

/// <summary>
/// One session setting a front end may offer as a picker or a switch.
/// <see cref="Current"/> is the value of the selected entry in
/// <see cref="Values"/>. A switch has exactly the values <c>on</c> and
/// <c>off</c>.
/// </summary>
public sealed record SessionOption(
    string Id,
    string Name,
    string Category,
    string Current,
    IReadOnlyList<SessionOptionValue> Values,
    bool IsSwitch = false,
    string? Description = null);
