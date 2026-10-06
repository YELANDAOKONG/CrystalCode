namespace CrystalCode.Plugins.Approvals;

/// <summary>
/// Risk a plugin may assign or raise. Higher values are more restrictive.
/// </summary>
public enum PluginRisk
{
    Read,
    Write,
    Privileged,
    Forbidden
}
