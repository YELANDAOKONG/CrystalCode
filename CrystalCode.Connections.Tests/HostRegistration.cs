using System.Runtime.CompilerServices;

namespace CrystalCode.Connections.Tests;

/// <summary>
/// Registers the contribution the way the executable does, because the engine
/// ships no provider definitions of its own.
/// </summary>
internal static class HostRegistration
{
    [ModuleInitializer]
    internal static void Register() => ConnectionsHost.Register();
}
