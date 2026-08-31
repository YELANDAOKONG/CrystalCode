using CrystalCode.Skills;
using CrystalCode.Tests.Tools;

using Xunit;

namespace CrystalCode.Tests.Skills;

public sealed class SkillCatalogTests
{
    [Fact]
    public void ContainsReadablePath_AcceptsEntireSkillsTree()
    {
        using var root = new TemporaryWorkspace();
        var skillsRoot = Path.Combine(root.Path, "skills");
        var skillDir = Path.Combine(skillsRoot, "demo-skill");
        Directory.CreateDirectory(skillDir);
        var skillFile = Path.Combine(skillDir, "SKILL.md");
        var nested = Path.Combine(skillDir, "scripts", "setup.sh");
        var loose = Path.Combine(skillsRoot, "notes.md");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        File.WriteAllText(skillFile, "body");
        File.WriteAllText(nested, "echo");
        File.WriteAllText(loose, "extra");
        var catalog = new SkillCatalog(
            [new SkillInfo("demo-skill", "A demo skill.", skillFile, "body")],
            [skillsRoot]);

        Assert.True(catalog.ContainsReadablePath(skillsRoot));
        Assert.True(catalog.ContainsReadablePath(skillFile));
        Assert.True(catalog.ContainsReadablePath(nested));
        Assert.True(catalog.ContainsReadablePath(loose));
        Assert.False(catalog.ContainsReadablePath(root.Path));
        Assert.False(catalog.ContainsReadablePath(Path.Combine(root.Path, "other.txt")));
    }

    [Fact]
    public void ContainsReadablePath_DoesNotMatchNeighborDirectory()
    {
        using var root = new TemporaryWorkspace();
        var skillsRoot = Path.Combine(root.Path, "skills");
        var neighbor = Path.Combine(root.Path, "skills-extra");
        Directory.CreateDirectory(skillsRoot);
        Directory.CreateDirectory(neighbor);
        var neighborFile = Path.Combine(neighbor, "secret.txt");
        File.WriteAllText(neighborFile, "no");
        var catalog = new SkillCatalog([], [skillsRoot]);

        Assert.False(catalog.ContainsReadablePath(neighborFile));
        Assert.False(catalog.ContainsReadablePath(neighbor));
    }

    [Fact]
    public void ContainsReadablePath_WithoutReadRoots_IsFalse()
    {
        using var root = new TemporaryWorkspace();
        var skillFile = Path.Combine(root.Path, "demo-skill", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(skillFile)!);
        File.WriteAllText(skillFile, "body");
        var catalog = new SkillCatalog(
            [new SkillInfo("demo-skill", "A demo skill.", skillFile, "body")]);

        Assert.False(catalog.ContainsReadablePath(skillFile));
    }
}
