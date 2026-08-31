namespace CrystalHarness.Display.Input;

/// <summary>
/// A decoded key. Platform CSI and empty <see cref="ConsoleKeyInfo.Key"/> are already resolved.
/// </summary>
public sealed record InputKey(ConsoleKey Key, char KeyChar, ConsoleModifiers Modifiers) : IInputEvent
{
    public static InputKey From(ConsoleKeyInfo info) =>
        new(info.Key, info.KeyChar, info.Modifiers);

    public ConsoleKeyInfo ToConsoleKeyInfo() =>
        new(
            KeyChar,
            Key,
            Modifiers.HasFlag(ConsoleModifiers.Shift),
            Modifiers.HasFlag(ConsoleModifiers.Alt),
            Modifiers.HasFlag(ConsoleModifiers.Control));
}
