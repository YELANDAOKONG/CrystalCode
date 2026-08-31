using System.Reflection;
using System.Runtime.Loader;

using Crystal.Chat;
using Crystal.Tools;

namespace CrystalCode.Tools.External;

/// <summary>
/// Isolated load context for one tool set assembly. Shared contract types
/// come from the host context that already loaded Crystal.Tools.
/// </summary>
internal sealed class ExternalLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _directory;

    public ExternalLoadContext(string assemblyPath)
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
        if (string.IsNullOrEmpty(simple))
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

    private static Assembly Contract(string name) =>
        name.Equals("Crystal.Tools", StringComparison.OrdinalIgnoreCase)
            ? typeof(ITool).Assembly
            : typeof(ChatMessage).Assembly;

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
        || name.Equals("Crystal.Tools", StringComparison.OrdinalIgnoreCase);

    private static bool IsFramework(string name) =>
        name == "System"
        || name == "netstandard"
        || name == "mscorlib"
        || name.StartsWith("System.", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.", StringComparison.Ordinal);
}
