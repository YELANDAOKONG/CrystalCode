namespace CrystalCode.Engine.Home.Settings;

internal sealed class ServerHostDocument
{
    public List<string>? AllowOrigins { get; set; }

    public List<string>? AllowHosts { get; set; }

    public List<string>? ExposeHeaders { get; set; }

    public bool? AllowPrivateNetwork { get; set; }
}
