namespace CrystalCode.Engine.Home.Settings;

internal sealed class MicroCompactionDocument
{
    public bool? Enabled { get; set; }

    public int? ProtectTurns { get; set; }

    public int? ProtectTokens { get; set; }

    public int? MinimumTokens { get; set; }
}
