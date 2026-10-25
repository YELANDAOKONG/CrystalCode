using System.Reflection;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Events;
using CrystalCode.Engine.Tools.External;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

/// <summary>
/// Events cross to front ends that may serialize them. No event may carry
/// settings, provider definitions (which can hold a literal API key), a live
/// tool catalog, or a plugin object.
/// </summary>
public sealed class SessionEventDataTests
{
    private static readonly Type[] Forbidden =
    [
        typeof(HarnessSettings),
        typeof(ProviderCatalog),
        typeof(ProviderDefinition),
        typeof(ExternalCatalog)
    ];

    [Fact]
    public void Events_CarryNoSettingsCatalogsOrPluginObjects()
    {
        var events = typeof(SessionEvent).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(SessionEvent)) && !type.IsAbstract)
            .ToArray();
        Assert.NotEmpty(events);

        var offenders = new List<string>();
        foreach (var type in events)
        {
            Walk(type, type.Name, new HashSet<Type>(), offenders);
        }

        Assert.Empty(offenders);
    }

    private static void Walk(Type type, string path, HashSet<Type> seen, List<string> offenders)
    {
        if (!seen.Add(type) || type.IsPrimitive || type.IsEnum || type == typeof(string))
        {
            return;
        }

        if (Forbidden.Contains(type)
            || type.Namespace?.StartsWith("CrystalCode.Engine.Plugins", StringComparison.Ordinal) == true
            || type.Namespace?.StartsWith("CrystalCode.Plugins", StringComparison.Ordinal) == true)
        {
            offenders.Add(path + " -> " + type.FullName);
            return;
        }

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                Walk(argument, path + "<" + argument.Name + ">", seen, offenders);
            }

            if (type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            {
                return;
            }
        }

        if (type.IsArray)
        {
            Walk(type.GetElementType()!, path + "[]", seen, offenders);
            return;
        }

        if (type.Namespace?.StartsWith("CrystalCode", StringComparison.Ordinal) != true)
        {
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length == 0)
            {
                Walk(property.PropertyType, path + "." + property.Name, seen, offenders);
            }
        }
    }
}
