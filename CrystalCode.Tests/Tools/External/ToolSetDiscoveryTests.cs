using CrystalCode.Home;
using CrystalCode.Tests.Home;
using CrystalCode.Tests.Tools;
using CrystalCode.Tools.External;

using Xunit;

namespace CrystalCode.Tests.Tools.External;

public sealed class ToolSetDiscoveryTests
{
    [Fact]
    public void Collect_ProjectDirectoryReplacesHome()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteSet(home.Home.ToolsDirectory, "echojson", "home");
        WriteSet(
            Path.Combine(workspace.Path, ".crystal", "tools"),
            "echojson",
            "project");
        var notes = new List<string>();

        var sets = new ToolSetDiscovery(home.Home).Collect(workspace.Path, notes);

        Assert.Single(sets);
        Assert.Equal("project", sets[0].Tools[0].Description);
        Assert.Empty(notes);
    }

    [Fact]
    public void Collect_SkipsInvalidManifestWithNote()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(home.Home.ToolsDirectory, "broken");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ExternalFiles.FileName), "{");
        var notes = new List<string>();

        var sets = new ToolSetDiscovery(home.Home).Collect(workspace.Path, notes);

        Assert.Empty(sets);
        Assert.Contains(notes, note => note.Contains("broken", StringComparison.Ordinal));
    }

    [Fact]
    public void Collect_DisabledSet_IsOmitted()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteSet(home.Home.ToolsDirectory, "echojson", "home", enabled: false);
        var notes = new List<string>();

        var sets = new ToolSetDiscovery(home.Home).Collect(workspace.Path, notes);

        Assert.Empty(sets);
        Assert.Empty(notes);
    }

    [Fact]
    public void Collect_ProjectDisabledReplacesHomeSet()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteSet(home.Home.ToolsDirectory, "echojson", "home");
        WriteSet(
            Path.Combine(workspace.Path, ".crystal", "tools"),
            "echojson",
            "project",
            enabled: false);
        var notes = new List<string>();

        var sets = new ToolSetDiscovery(home.Home).Collect(workspace.Path, notes);

        Assert.Empty(sets);
        Assert.Empty(notes);
    }

    private static void WriteSet(
        string toolsRoot,
        string name,
        string description,
        bool enabled = true)
    {
        var directory = Path.Combine(toolsRoot, name);
        Directory.CreateDirectory(directory);
        var enabledJson = enabled ? string.Empty : """
              "enabled": false,
            """;
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            $$"""
            {
              "runner": "exec",
            {{enabledJson}}  "description": "{{description}}",
              "schema": { "type": "object", "properties": {} },
              "command": ["/bin/true"]
            }
            """);
    }
}
