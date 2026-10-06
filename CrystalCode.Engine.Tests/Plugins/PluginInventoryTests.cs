using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Disk;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Plugins;

public sealed class PluginInventoryTests
{
    [Fact]
    public void List_MarksTheProjectCopyEffectiveAndKeepsADisabledPlugin()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WritePlugin(home.Home.PluginsDirectory, "acme", enabled: true, note: "home");
        WritePlugin(ProjectRoot(workspace.Path), "acme", enabled: false, note: "project");
        WritePlugin(home.Home.PluginsDirectory, "beta", enabled: null, note: "keep");

        var entries = PluginInventory.List(home.Home, workspace.Path);

        var project = Assert.Single(entries, entry => entry.Source == PluginSource.Project);
        var homeAcme = Assert.Single(
            entries,
            entry => entry.Source == PluginSource.Home && entry.DirectoryName == "acme");
        var beta = Assert.Single(entries, entry => entry.DirectoryName == "beta");
        Assert.False(project.Enabled);
        Assert.True(project.Effective);
        Assert.True(homeAcme.Enabled);
        Assert.False(homeAcme.Effective);
        Assert.True(beta.Enabled);
        Assert.True(beta.Effective);
        var text = string.Join('\n', PluginInventory.Format(entries));
        Assert.Contains("Directory", text, StringComparison.Ordinal);
        Assert.Contains("acme", text, StringComparison.Ordinal);
        Assert.Contains("No", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TrySetEnabled_PreservesOtherFieldsAndSkipsAnUnchangedValue()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var path = WritePlugin(home.Home.PluginsDirectory, "acme", enabled: true, note: "keep");

        Assert.True(PluginInventory.TrySetEnabled(
            home.Home,
            workspace.Path,
            "acme",
            null,
            false,
            out var disabled,
            out var changed,
            out var error));
        Assert.True(changed);
        Assert.NotNull(disabled);
        Assert.False(disabled.Enabled);
        Assert.Equal(string.Empty, error);
        var json = File.ReadAllText(path);
        Assert.Contains("\"note\": \"keep\"", json, StringComparison.Ordinal);
        Assert.Contains("\"enabled\": false", json, StringComparison.Ordinal);

        Assert.True(PluginInventory.TrySetEnabled(
            home.Home,
            workspace.Path,
            "acme",
            PluginSource.Home,
            false,
            out _,
            out changed,
            out error));
        Assert.False(changed);
        Assert.Equal(string.Empty, error);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void TrySetEnabled_UsesTheProjectCopyAndRefusesABrokenManifest()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WritePlugin(home.Home.PluginsDirectory, "acme", enabled: true, note: "home");
        var project = WritePlugin(ProjectRoot(workspace.Path), "acme", enabled: true, note: "project");

        Assert.True(PluginInventory.TrySetEnabled(
            home.Home,
            workspace.Path,
            "acme",
            null,
            false,
            out var entry,
            out var changed,
            out _));
        Assert.True(changed);
        Assert.Equal(PluginSource.Project, entry!.Source);
        Assert.Contains("\"note\": \"home\"", File.ReadAllText(
            Path.Combine(home.Home.PluginsDirectory, "acme", PluginFiles.FileName)), StringComparison.Ordinal);
        Assert.DoesNotContain("\"enabled\": false", File.ReadAllText(
            Path.Combine(home.Home.PluginsDirectory, "acme", PluginFiles.FileName)), StringComparison.Ordinal);

        File.WriteAllText(project, "{ \"enabled\": \"yes\" }");
        var broken = File.ReadAllText(project);
        Assert.False(PluginInventory.TrySetEnabled(
            home.Home,
            workspace.Path,
            "acme",
            PluginSource.Project,
            true,
            out _,
            out changed,
            out var error));
        Assert.False(changed);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(broken, File.ReadAllText(project));
    }

    [Fact]
    public void TryFind_ReportsAMissingDirectory()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();

        Assert.False(PluginInventory.TryFind(
            home.Home,
            workspace.Path,
            "missing",
            null,
            out var entry,
            out var error));
        Assert.Null(entry);
        Assert.Contains("not found", error, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogCommand_ParsesSourceAndRejectsAMissingName()
    {
        Assert.True(CatalogCommand.TryParse(["project", "disable", "acme"], out var command, out _));
        Assert.Equal("disable", command!.Verb);
        Assert.Equal("acme", command.DirectoryName);
        Assert.Equal("project", command.Source);
        Assert.False(CatalogCommand.TryParse(["enable"], out _, out var error));
        Assert.Contains("Usage", error, StringComparison.Ordinal);
        Assert.False(CatalogCommand.LooksLike(["home", "author"]));
    }

    private static string ProjectRoot(string workspace) =>
        Path.Combine(workspace, PluginFiles.CrystalDirectory, PluginFiles.DirectoryName);

    private static string WritePlugin(string root, string name, bool? enabled, string note)
    {
        var directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, PluginFiles.FileName);
        var enabledLine = enabled is bool value ? $",\n  \"enabled\": {(value ? "true" : "false")}" : string.Empty;
        File.WriteAllText(
            path,
            "{\n"
            + $"  \"note\": \"{note}\",\n"
            + "  \"assembly\": \"Acme.dll\",\n"
            + $"  \"type\": \"Acme.Plugin\"{enabledLine}\n"
            + "}\n");
        return path;
    }
}
