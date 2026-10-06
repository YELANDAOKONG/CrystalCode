using CrystalCode.Engine.Approvals;
using CrystalCode.Plugins.Approvals;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>Maps plugin risk and authority onto the host classification.</summary>
internal static class PluginRiskMap
{
    public static int Rank(Risk risk)
    {
        ArgumentNullException.ThrowIfNull(risk);
        if (risk == Risk.Read)
        {
            return 0;
        }

        if (risk == Risk.Write)
        {
            return 1;
        }

        if (risk == Risk.Privileged)
        {
            return 2;
        }

        if (risk == Risk.Forbidden)
        {
            return 3;
        }

        return 2;
    }

    public static int Rank(PluginRisk risk) =>
        risk switch
        {
            PluginRisk.Read => 0,
            PluginRisk.Write => 1,
            PluginRisk.Privileged => 2,
            PluginRisk.Forbidden => 3,
            _ => 2
        };

    public static Risk ToRisk(PluginRisk risk) =>
        risk switch
        {
            PluginRisk.Read => Risk.Read,
            PluginRisk.Write => Risk.Write,
            PluginRisk.Privileged => Risk.Privileged,
            PluginRisk.Forbidden => Risk.Forbidden,
            _ => Risk.Privileged
        };

    public static Authority ToAuthority(PluginAuthority authority) =>
        authority switch
        {
            PluginAuthority.Workspace => Authority.Workspace,
            PluginAuthority.OutsideWorkspace => Authority.OutsideWorkspace,
            PluginAuthority.Network => Authority.Network,
            PluginAuthority.PrivilegedEscalation => Authority.PrivilegedEscalation,
            _ => Authority.PrivilegedEscalation
        };
}
