using CrystalCode.Engine.Home;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Disk;
using CrystalCode.Engine.Skills;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.External;

using Xunit;

namespace CrystalCode.Engine.Tests.Plugins;

public sealed class PluginEnvironmentFactoryTests
{
    [Fact]
    public void Create_ListsPluginsToolsAndSkills()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WritePlugin(
            home.Home.PluginsDirectory,
            "Quiet",
            """
            {
              "enabled": false,
              "assembly": "Quiet.dll",
              "type": "Quiet.Plugin"
            }
            """);
        WritePlugin(home.Home.PluginsDirectory, "Broken", "{");
        var tools = Path.Combine(home.Home.ToolsDirectory, "lint");
        Directory.CreateDirectory(tools);
        File.WriteAllText(
            Path.Combine(tools, ExternalFiles.FileName),
            """
            {
              "runner": "exec",
              "description": "Lint.",
              "schema": { "type": "object", "properties": {} },
              "command": ["/bin/true"],
              "catalogs": ["work"]
            }
            """);
        var skillFile = Path.Combine(workspace.Path, "SKILL.md");
        File.WriteAllText(skillFile, "body");
        var skills = new SkillCatalog(
            [new SkillInfo("review-diff", "Review the diff.", skillFile, "body")]);
        var plugins = PluginCatalog.Load(home.Home, new Workspace(workspace.Path), enabled: true);
        var external = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);

        var environment = PluginEnvironmentFactory.Create(
            home.Home,
            workspace.Path,
            plugins,
            external,
            skills);

        var quiet = Assert.Single(environment.Plugins, peer => peer.Directory == "Quiet");
        Assert.Equal("home", quiet.Source);
        Assert.False(quiet.Enabled);
        Assert.False(quiet.Loaded);
        Assert.Equal(string.Empty, quiet.Name);
        var broken = Assert.Single(environment.Plugins, peer => peer.Directory == "Broken");
        Assert.False(broken.Loaded);
        Assert.NotEqual(string.Empty, broken.Error);
        var lintSet = Assert.Single(environment.ToolSets, peer => peer.Directory == "lint");
        Assert.True(lintSet.Loaded);
        Assert.Equal("home", lintSet.Source);
        var lint = Assert.Single(environment.ExternalTools);
        Assert.Equal("lint", lint.Name);
        Assert.Equal("lint", lint.Set);
        Assert.False(lint.Plan);
        Assert.True(lint.Work);
        var skill = Assert.Single(environment.Skills);
        Assert.Equal("review-diff", skill.Name);
        Assert.Equal("Review the diff.", skill.Description);
    }

    [Fact]
    public void Create_OmitsSkillsWhenTheCatalogIsAbsent()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();

        var environment = PluginEnvironmentFactory.Create(
            home.Home,
            workspace.Path,
            PluginCatalog.Empty,
            ExternalCatalog.Empty,
            skills: null);

        Assert.Empty(environment.Skills);
        Assert.Empty(environment.Plugins);
        Assert.Empty(environment.ExternalTools);
    }

    private static void WritePlugin(string root, string name, string json)
    {
        var directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, PluginFiles.FileName), json);
    }
}
