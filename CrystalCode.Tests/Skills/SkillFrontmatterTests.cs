using CrystalCode.Skills;

using Xunit;

namespace CrystalCode.Tests.Skills;

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
    public void TryRead_AcceptsDisplayTitleName()
    {
        var ok = SkillFrontmatter.TryRead(
            """
            ---
            name: C# Clean Code
            description: Use when writing C# code
            ---
            body
            """,
            out var frontmatter,
            out _);

        Assert.True(ok);
        Assert.Equal("C# Clean Code", frontmatter!.Name);
        Assert.Equal("Use when writing C# code", frontmatter.Description);
    }

    [Fact]
    public void TryRead_ReadsFoldedDescription()
    {
        var ok = SkillFrontmatter.TryRead(
            """
            ---
            name: csharp-clean-code
            description: >
              Use this skill when writing C# code. Covers
              file-scoped namespaces and naming conventions.
            license: MIT
            ---
            body
            """,
            out var frontmatter,
            out var body);

        Assert.True(ok);
        Assert.Equal("csharp-clean-code", frontmatter!.Name);
        Assert.Equal(
            "Use this skill when writing C# code. Covers file-scoped namespaces and naming conventions.",
            frontmatter.Description);
        Assert.Equal("body", body);
    }

    [Fact]
    public void TryRead_ReadsLiteralDescription()
    {
        var ok = SkillFrontmatter.TryRead(
            """
            ---
            name: notes
            description: |
              First line
              Second line
            ---
            body
            """,
            out var frontmatter,
            out _);

        Assert.True(ok);
        Assert.Equal("First line\nSecond line", frontmatter!.Description);
    }

    [Fact]
    public void TryRead_RejectsEmptyFoldedDescription()
    {
        Assert.False(
            SkillFrontmatter.TryRead(
                """
                ---
                name: git-release
                description: >
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
