using CrystalHarness.Skills;
using CrystalHarness.Tests.Home;
using CrystalHarness.Tests.Tools;

using Xunit;

namespace CrystalHarness.Tests.Skills;

public sealed class SkillDiscoveryTests
{
    [Fact]
    public void Collect_FindsGlobalCrystalAndProjectCrystal()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        home.Home.EnsureCreated();
        WriteSkill(Path.Combine(home.Home.Root, "skills"), "global-skill", "A global Crystal skill.");
        WriteSkill(
            Path.Combine(workspace.Path, ".crystal", "skills"),
            "project-skill",
            "A project Crystal skill.");
        var discovery = SkillDiscovery.Isolated(home.Home);

        var catalog = discovery.Collect(workspace.Path);

        Assert.Equal(2, catalog.Count);
        Assert.NotNull(catalog.Find("global-skill"));
        Assert.NotNull(catalog.Find("project-skill"));
        Assert.Equal("A project Crystal skill.", catalog.Find("project-skill")!.Description);
    }

    [Fact]
    public void Collect_FindsOpenCodeClaudeAndAgentsPaths()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        home.Home.EnsureCreated();
        var discovery = SkillDiscovery.Isolated(home.Home);
        WriteSkill(
            Path.Combine(home.Home.Root, "xdg-config", "opencode", "skills"),
            "opencode-global",
            "OpenCode global skill.");
        WriteSkill(
            Path.Combine(home.Home.Root, "profile", ".claude", "skills"),
            "claude-global",
            "Claude global skill.");
        WriteSkill(
            Path.Combine(home.Home.Root, "profile", ".agents", "skills"),
            "agents-global",
            "Agents global skill.");
        WriteSkill(
            Path.Combine(workspace.Path, ".opencode", "skill"),
            "opencode-project",
            "OpenCode project skill in skill/.");
        WriteSkill(
            Path.Combine(workspace.Path, ".claude", "skills"),
            "claude-project",
            "Claude project skill.");
        WriteSkill(
            Path.Combine(workspace.Path, ".agents", "skills"),
            "agents-project",
            "Agents project skill.");

        var catalog = discovery.Collect(workspace.Path);

        Assert.NotNull(catalog.Find("opencode-global"));
        Assert.NotNull(catalog.Find("claude-global"));
        Assert.NotNull(catalog.Find("agents-global"));
        Assert.NotNull(catalog.Find("opencode-project"));
        Assert.NotNull(catalog.Find("claude-project"));
        Assert.NotNull(catalog.Find("agents-project"));
    }

    [Fact]
    public void Collect_TreatsEntireSkillsTreeAsReadable()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        home.Home.EnsureCreated();
        var skillsRoot = Path.Combine(home.Home.Root, "profile", ".agents", "skills");
        WriteSkill(skillsRoot, "agents-global", "Agents global skill.");
        var loose = Path.Combine(skillsRoot, "notes.md");
        var nested = Path.Combine(skillsRoot, "agents-global", "scripts", "setup.sh");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        File.WriteAllText(loose, "extra");
        File.WriteAllText(nested, "echo");
        var outside = Path.Combine(home.Home.Root, "profile", ".agents", "other.txt");
        File.WriteAllText(outside, "no");
        var discovery = SkillDiscovery.Isolated(home.Home);

        var catalog = discovery.Collect(workspace.Path);

        Assert.True(catalog.ContainsReadablePath(skillsRoot));
        Assert.True(catalog.ContainsReadablePath(loose));
        Assert.True(catalog.ContainsReadablePath(nested));
        Assert.False(catalog.ContainsReadablePath(outside));
        Assert.False(catalog.ContainsReadablePath(Path.Combine(home.Home.Root, "profile", ".agents")));
    }

    [Fact]
    public void Collect_ProjectCrystalOverwritesGlobalAndOpenCode()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        home.Home.EnsureCreated();
        WriteSkill(Path.Combine(home.Home.Root, "skills"), "shared-skill", "from crystal home");
        WriteSkill(
            Path.Combine(workspace.Path, ".opencode", "skills"),
            "shared-skill",
            "from opencode project");
        WriteSkill(
            Path.Combine(workspace.Path, ".crystal", "skills"),
            "shared-skill",
            "from crystal project");
        var discovery = SkillDiscovery.Isolated(home.Home);

        var catalog = discovery.Collect(workspace.Path);

        Assert.Equal(1, catalog.Count);
        Assert.Equal("from crystal project", catalog.Find("shared-skill")!.Description);
    }

    [Fact]
    public void Collect_WalksToGitRootAndLaterDirectoryWins()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".git"));
        var nested = Path.Combine(workspace.Path, "src");
        Directory.CreateDirectory(nested);
        WriteSkill(
            Path.Combine(nested, ".agents", "skills"),
            "shared-skill",
            "from nested workspace");
        WriteSkill(
            Path.Combine(workspace.Path, ".agents", "skills"),
            "shared-skill",
            "from git root");
        var discovery = SkillDiscovery.Isolated(home.Home);

        var catalog = discovery.Collect(nested);

        Assert.Equal("from git root", catalog.Find("shared-skill")!.Description);
    }

    [Fact]
    public void Collect_SkipsInvalidFrontmatterAndNameMismatch()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteSkill(
            Path.Combine(workspace.Path, ".crystal", "skills"),
            "good-skill",
            "A valid skill.");
        var invalid = Path.Combine(workspace.Path, ".crystal", "skills", "no-frontmatter");
        Directory.CreateDirectory(invalid);
        File.WriteAllText(Path.Combine(invalid, "SKILL.md"), "# No frontmatter\n");
        var mismatch = Path.Combine(workspace.Path, ".crystal", "skills", "folder-name");
        Directory.CreateDirectory(mismatch);
        File.WriteAllText(
            Path.Combine(mismatch, "SKILL.md"),
            """
            ---
            name: other-name
            description: Directory does not match.
            ---
            body
            """);
        var discovery = SkillDiscovery.Isolated(home.Home);

        var catalog = discovery.Collect(workspace.Path);

        Assert.Equal(1, catalog.Count);
        Assert.NotNull(catalog.Find("good-skill"));
        Assert.Null(catalog.Find("no-frontmatter"));
        Assert.Null(catalog.Find("other-name"));
        Assert.Null(catalog.Find("folder-name"));
    }

    private static void WriteSkill(string root, string name, string description)
    {
        var directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "SKILL.md"),
            $"""
            ---
            name: {name}
            description: {description}
            ---

            # {name}

            Instructions.
            """);
    }
}
