using System.Reflection;
using System.Runtime.Loader;

using Crystal.Chat;
using Crystal.Tools;

using CrystalCode.Engine.Tools.External;
using CrystalCode.Plugins;
using CrystalCode.Tools;

namespace CrystalCode.Engine.Plugins.Disk;

/// <summary>
/// Isolated load context for one plugin assembly. Shared contracts come from
/// the host. The host executable and engine are never loaded from the plugin
/// directory.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _directory;

    public PluginLoadContext(string assemblyPath)
        : base(isCollectible: false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        _resolver = new AssemblyDependencyResolver(assemblyPath);
        _directory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath))
            ?? throw new ArgumentException("Assembly path has no directory.", nameof(assemblyPath));
        Resolving += (_, name) => ResolveContract(name);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);
        var simple = assemblyName.Name;
        if (string.IsNullOrEmpty(simple) || IsHostAssembly(simple))
        {
            return null;
        }

        var contract = ResolveContract(assemblyName);
        if (contract is not null)
        {
            return contract;
        }

        var shared = FindLoaded(simple);
        if (shared is not null && IsFramework(simple))
        {
            return shared;
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path is null)
        {
            return null;
        }

        var full = Path.GetFullPath(path);
        if (!ExternalPath.IsInside(_directory, full) || IsContract(simple))
        {
            return null;
        }

        return LoadFromAssemblyPath(full);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(unmanagedDllName);
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (path is null)
        {
            return nint.Zero;
        }

        var full = Path.GetFullPath(path);
        if (!ExternalPath.IsInside(_directory, full))
        {
            return nint.Zero;
        }

        return LoadUnmanagedDllFromPath(full);
    }

    private static Assembly? ResolveContract(AssemblyName assemblyName)
    {
        var simple = assemblyName.Name;
        if (string.IsNullOrEmpty(simple) || !IsContract(simple))
        {
            return null;
        }

        return Contract(simple);
    }

    private static AssemblyLoadContext HostContext =>
        GetLoadContext(typeof(ITool).Assembly) ?? Default;

    private static Assembly Contract(string name)
    {
        if (name.Equals("Crystal.Tools", StringComparison.OrdinalIgnoreCase))
        {
            return typeof(ITool).Assembly;
        }

        if (name.Equals("CrystalCode.Tools", StringComparison.OrdinalIgnoreCase))
        {
            return typeof(IHostTool).Assembly;
        }

        if (name.Equals("CrystalCode.Plugins", StringComparison.OrdinalIgnoreCase))
        {
            return typeof(IPlugin).Assembly;
        }

        return typeof(ChatMessage).Assembly;
    }

    private static Assembly? FindLoaded(string name)
    {
        foreach (var assembly in HostContext.Assemblies)
        {
            if (string.Equals(assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return assembly;
            }
        }

        return null;
    }

    private static bool IsContract(string name) =>
        name.Equals("Crystal", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Crystal.Tools", StringComparison.OrdinalIgnoreCase)
        || name.Equals("CrystalCode.Tools", StringComparison.OrdinalIgnoreCase)
        || name.Equals("CrystalCode.Plugins", StringComparison.OrdinalIgnoreCase);

    internal static bool IsHostAssemblyName(string name) =>
        IsHostAssembly(name);

    private static bool IsHostAssembly(string name) =>
        name.Equals("CrystalCode", StringComparison.OrdinalIgnoreCase)
        || name.Equals("CrystalCode.Engine", StringComparison.OrdinalIgnoreCase)
        || name.Equals("CrystalCode.Display", StringComparison.OrdinalIgnoreCase)
        || name.Equals("CrystalCode.Providers", StringComparison.OrdinalIgnoreCase);

    private static bool IsFramework(string name) =>
        name == "System"
        || name == "netstandard"
        || name == "mscorlib"
        || name.StartsWith("System.", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.", StringComparison.Ordinal);
}
