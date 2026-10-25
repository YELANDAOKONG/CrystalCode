using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Launch;

/// <summary>
/// A validated launch: the settings, workspace, saved session, mode, and
/// directory rules a front end hands to <see cref="CodingSession.Create"/>.
/// </summary>
/// <param name="Settings">Live settings with launch overrides applied.</param>
/// <param name="Persisted">The disk baseline later preference saves start from.</param>
/// <param name="Mode">The launch mode name, or null to keep a resumed mode.</param>
/// <param name="Notes">Plain-text notes, such as an unavailable thinking gear.</param>
public sealed record SessionLaunch(
    CrystalHome Home,
    DiskSettingsStore SettingsStore,
    CredentialStore Credentials,
    HarnessSettings Settings,
    HarnessSettings Persisted,
    string Workspace,
    SessionDocument? Resume,
    string? Mode,
    IReadOnlyList<DirectoryAccess> Access,
    IReadOnlyList<string> Notes)
{
    public CodingSession CreateSession(SessionFrontEnd frontEnd, PluginRegistry plugins)
    {
        ArgumentNullException.ThrowIfNull(frontEnd);
        ArgumentNullException.ThrowIfNull(plugins);
        return CodingSession.Create(
            Settings,
            SettingsStore,
            Credentials,
            Home,
            Workspace,
            frontEnd,
            plugins,
            Resume,
            Persisted,
            Access);
    }
}
