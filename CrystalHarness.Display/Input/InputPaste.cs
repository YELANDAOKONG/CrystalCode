namespace CrystalHarness.Display.Input;

/// <summary>
/// Text to insert into the composer. Line endings are already normalized to \n.
/// </summary>
public sealed record InputPaste(string Text) : IInputEvent;
