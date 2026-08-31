using CrystalHarness.Skills;

using Xunit;

namespace CrystalHarness.Tests.Skills;

public sealed class SkillFrontmatterTests
{
    [Fact]
    public void TryRead_ReadsNameAndDescription()
    {
        var ok = SkillFrontmatter.TryRead(
            """
            ---
            name: git-release
            description: Create consistent releases and changelogs
            license: MIT
            ---

            ## What I do
            Draft notes.
            """,
            out var frontmatter,
            out var body);

        Assert.True(ok);
        Assert.Equal("git-release", frontmatter!.Name);
        Assert.Equal("Create consistent releases and changelogs", frontmatter.Description);
        Assert.Contains("Draft notes.", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TryRead_AcceptsUnquotedColonInDescription()
    {
        var ok = SkillFrontmatter.TryRead(
            """
            ---
            name: git-release
            description: Use when preparing a release: notes and tags
            ---
            body
            """,
            out var frontmatter,
            out _);

        Assert.True(ok);
        Assert.Equal("Use when preparing a release: notes and tags", frontmatter!.Description);
    }

    [Fact]
    public void TryRead_RejectsMissingFrontmatter()
    {
        Assert.False(SkillFrontmatter.TryRead("# No frontmatter\n", out var frontmatter, out _));
        Assert.Null(frontmatter);
    }

    [Fact]
    public void TryRead_RejectsInvalidName()
    {
        Assert.False(
            SkillFrontmatter.TryRead(
                """
                ---
                name: Git_Release
                description: Invalid name
                ---
                body
                """,
                out _,
                out _));
    }

    [Fact]
    public void IsValidName_MatchesOpenCodePattern()
    {
        Assert.True(SkillFrontmatter.IsValidName("git-release"));
        Assert.True(SkillFrontmatter.IsValidName("a"));
        Assert.False(SkillFrontmatter.IsValidName("-git"));
        Assert.False(SkillFrontmatter.IsValidName("git-"));
        Assert.False(SkillFrontmatter.IsValidName("git--release"));
        Assert.False(SkillFrontmatter.IsValidName("GitRelease"));
    }
}
