namespace CrystalCode.Display.Input;

/// <summary>
/// Transcript scroll from a mouse wheel report.
/// Positive delta moves toward older rows.
/// </summary>
public sealed record InputWheel(int Delta) : IInputEvent
{
    public const int LineStep = 3;
}
