namespace CrystalCode.Engine.Home;

/// <summary>
/// JSON shape for Plan and Work built-in tool disablement lists.
/// </summary>
public sealed class BuiltInToolDisablementDocument
{
    public List<string>? Plan { get; set; }

    public List<string>? Work { get; set; }
}
