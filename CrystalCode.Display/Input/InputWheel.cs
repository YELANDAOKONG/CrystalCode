namespace CrystalCode.Display.Input;

/// <summary>
/// Transcript scroll from the mouse wheel or a 1007 arrow burst.
/// Positive delta moves toward older rows.
/// </summary>
public sealed record InputWheel(int Delta) : IInputEvent
{
    public const int LineStep = 3;
}
