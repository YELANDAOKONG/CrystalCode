using CrystalCode.Skills;
using CrystalCode.Tests.Tools;

using Xunit;

namespace CrystalCode.Tests.Skills;

public sealed class SkillGuidanceTests
{
    [Fact]
    public void Render_EmptyCatalog_SaysNoneAvailable()
    {
        var text = SkillGuidance.Render(SkillCatalog.Empty);

        Assert.Contains("No skills are currently available.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<available_skills>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ListsNameAndDescriptionWithoutLocation()
    {
        using var temp = new TemporaryWorkspace();
        var path = Path.Combine(temp.Path, "skills", "git-release", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "body");
        var catalog = new SkillCatalog(
        [
            new SkillInfo(
                "git-release",
                "Create releases <and> changelogs",
                path,
                "body")
        ]);

        var text = SkillGuidance.Render(catalog);

        Assert.Contains("<available_skills>", text, StringComparison.Ordinal);
        Assert.Contains("<name>git-release</name>", text, StringComparison.Ordinal);
        Assert.Contains("&lt;and&gt;", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<location>", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetFullPath(path), text, StringComparison.Ordinal);
    }
}
