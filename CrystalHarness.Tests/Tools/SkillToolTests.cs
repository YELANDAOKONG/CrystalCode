using Crystal.Tools;

using CrystalHarness.Skills;
using CrystalHarness.Tools;

using Xunit;

namespace CrystalHarness.Tests.Tools;

public sealed class SkillToolTests
{
    [Fact]
    public async Task InvokeAsync_ReturnsSkillBodyAndSampledFiles()
    {
        using var root = new TemporaryWorkspace();
        var skillDir = Path.Combine(root.Path, "skills", "git-release");
        Directory.CreateDirectory(Path.Combine(skillDir, "scripts"));
        var skillPath = Path.Combine(skillDir, "SKILL.md");
        File.WriteAllText(
            skillPath,
            """
            ---
            name: git-release
            description: Create consistent releases.
            ---

            Draft the notes.
            """);
        var script = Path.Combine(skillDir, "scripts", "tag.sh");
        File.WriteAllText(script, "echo tag");
        var catalog = new SkillCatalog(
        [
            new SkillInfo("git-release", "Create consistent releases.", skillPath, "Draft the notes.")
        ]);
        var tool = new SkillTool(catalog);

        var output = await tool.InvokeAsync(
            new ToolCall("1", SkillTool.ToolName, """{"name":"git-release"}"""));

        Assert.Equal(ToolResultStatus.Success, output.Status);
        Assert.Contains("<skill_content name=\"git-release\">", output.Text, StringComparison.Ordinal);
        Assert.Contains("Draft the notes.", output.Text, StringComparison.Ordinal);
        Assert.Contains(Path.GetFullPath(skillDir), output.Text, StringComparison.Ordinal);
        Assert.Contains(Path.GetFullPath(script), output.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("SKILL.md", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_UnknownName_Fails()
    {
        var tool = new SkillTool(SkillCatalog.Empty);

        var output = await tool.InvokeAsync(
            new ToolCall("1", SkillTool.ToolName, """{"name":"missing"}"""));

        Assert.Equal(ToolResultStatus.Failure, output.Status);
        Assert.Contains("was not found", output.Text, StringComparison.Ordinal);
        Assert.Contains("none", output.Text, StringComparison.Ordinal);
    }
}
