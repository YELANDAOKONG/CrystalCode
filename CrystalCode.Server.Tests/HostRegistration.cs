using System.Runtime.CompilerServices;

using CrystalCode.Connections;

namespace CrystalCode.Server.Tests;

/// <summary>
/// Registers the contribution assembly the way the executable does, because
/// the engine ships no provider definitions of its own.
/// </summary>
internal static class HostRegistration
{
    [ModuleInitializer]
    internal static void Register() => ConnectionsHost.Register();
}
