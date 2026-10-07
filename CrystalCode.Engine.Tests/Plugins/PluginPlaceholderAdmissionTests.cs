using CrystalCode.Engine.Plugins;
using CrystalCode.Plugins.Placeholders;

using Xunit;

namespace CrystalCode.Engine.Tests.Plugins;

public sealed class PluginPlaceholderAdmissionTests
{
    [Fact]
    public void TryAdmit_RejectsHostNamesAndDuplicates()
    {
        var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();

        Assert.True(PluginPlaceholderAdmission.TryAdmit(
            "Acme",
            new Named("build_id"),
            accepted,
            notes,
            out var first));
        Assert.False(PluginPlaceholderAdmission.TryAdmit(
            "Other",
            new Named("BUILD_ID"),
            accepted,
            notes,
            out _));
        Assert.False(PluginPlaceholderAdmission.TryAdmit(
            "Other",
            new Named("workspace"),
            accepted,
            notes,
            out _));
        Assert.False(PluginPlaceholderAdmission.TryAdmit(
            "Other",
            new Named("not-a-name"),
            accepted,
            notes,
            out _));

        Assert.Equal("build_id", first!.Name);
        Assert.Contains(notes, note => note.Contains("duplicate", StringComparison.Ordinal));
        Assert.Contains(notes, note => note.Contains("host owns", StringComparison.Ordinal));
        Assert.Contains(notes, note => note.Contains("not-a-name", StringComparison.Ordinal));
    }

    private sealed class Named : IPluginPlaceholder
    {
        public Named(string name) => Name = name;

        public string Name { get; }

        public string Resolve(PluginPlaceholderContext context) => string.Empty;
    }
}
