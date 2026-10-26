namespace CrystalCode.Engine.Home;

internal sealed class OperatorNotifyDocument
{
    public bool? Enabled { get; set; }

    public List<string>? Command { get; set; }

    public List<string>? Events { get; set; }

    public int? DelaySeconds { get; set; }

    public int? TimeoutSeconds { get; set; }
}
