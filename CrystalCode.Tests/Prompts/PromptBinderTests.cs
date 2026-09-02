using CrystalCode.Prompts;

using Xunit;

namespace CrystalCode.Tests.Prompts;

public sealed class PromptBinderTests
{
    [Fact]
    public void Apply_SubstitutesSessionPlaceholdersInTemplate()
    {
        var context = PromptContext.Create(
            "/tmp/demo",
            "deepseek",
            "deepseek-v4-flash",
            "work",
            "Skills provide specialized instructions.",
            "prefer tests",
            new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero));

        var text = PromptBinder.Apply(
            """
            {{product_name}} {{mode}}
            {{env}}
            {{skills}}
            {{instructions_section}}
            """,
            context);

        Assert.Contains("Crystal Code work", text, StringComparison.Ordinal);
        Assert.Contains("<env>", text, StringComparison.Ordinal);
        Assert.Contains("Skills provide", text, StringComparison.Ordinal);
        Assert.Contains("## Workspace instructions", text, StringComparison.Ordinal);
        Assert.Contains("prefer tests", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_HonorsCustomPlaceholderPlacement()
    {
        var context = PromptContext.Create(
            "/tmp/demo",
            "openai",
            "gpt-4.1",
            "plan",
            "skill guidance",
            "repo rules",
            new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero));

        var text = PromptBinder.Apply(
            """
            intro
            {{instructions_section}}
            middle
            {{env}}
            tail
            """,
            context);

        var intro = text.IndexOf("intro", StringComparison.Ordinal);
        var instructions = text.IndexOf("## Workspace instructions", StringComparison.Ordinal);
        var middle = text.IndexOf("middle", StringComparison.Ordinal);
        var env = text.IndexOf("<env>", StringComparison.Ordinal);
        var tail = text.IndexOf("tail", StringComparison.Ordinal);
        Assert.True(intro < instructions);
        Assert.True(instructions < middle);
        Assert.True(middle < env);
        Assert.True(env < tail);
        Assert.DoesNotContain("skill guidance", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_SubstitutesAtomicPlaceholdersOnly()
    {
        var context = PromptContext.Create(
            "/tmp/demo",
            "deepseek",
            "deepseek-v4-flash",
            "work",
            "skill guidance",
            "repo rules",
            new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero));

        var text = PromptBinder.Apply(
            "Workspace={{workspace}} git={{is_git_repo}} date={{date}} model={{model_line}} mode={{mode}} product={{product_name}}",
            context);

        Assert.Equal(
            "Workspace=/tmp/demo git=no date=Monday Aug 31, 2026 model=deepseek / deepseek-v4-flash mode=work product=Crystal Code",
            text);
    }

    [Fact]
    public void Apply_LeavesUnknownPlaceholdersUntouched()
    {
        var text = PromptBinder.Apply(
            "before {{unknown_slot}} after",
            PromptContext.InstructionsOnly(string.Empty));

        Assert.Equal("before {{unknown_slot}} after", text);
    }

    [Fact]
    public void Apply_IsCaseInsensitive()
    {
        var context = PromptContext.Create(
            "/tmp/demo",
            "openai",
            "gpt-4.1",
            "work",
            string.Empty,
            string.Empty,
            new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero));

        var text = PromptBinder.Apply("{{ WORKSPACE }}", context);

        Assert.Equal("/tmp/demo", text);
    }

    [Fact]
    public void Apply_SubstitutesReviewPlaceholders()
    {
        var text = PromptBinder.Apply(
            ApprovalReviewPrompt.UserTemplate,
            new PromptBinding(
                Review: new ReviewPromptContext(
                    "[User]: Add tests.",
                    "write",
                    """{"path":"App.cs"}""",
                    "write",
                    "workspace",
                    "Write App.cs")));

        Assert.Contains("[User]: Add tests.", text, StringComparison.Ordinal);
        Assert.Contains("Tool: write", text, StringComparison.Ordinal);
        Assert.Contains("Host risk: write", text, StringComparison.Ordinal);
        Assert.Contains("""{"path":"App.cs"}""", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_SubstitutesCompactionPlaceholders()
    {
        var text = PromptBinder.Apply(
            CompactionPrompt.UserTemplate,
            new PromptBinding(
                Compaction: new CompactionPromptContext(
                    "turn one",
                    "prior block",
                    "summarize",
                    "template body",
                    "Open todos:\none")));

        Assert.Contains("turn one", text, StringComparison.Ordinal);
        Assert.Contains("prior block", text, StringComparison.Ordinal);
        Assert.Contains("summarize", text, StringComparison.Ordinal);
        Assert.Contains("template body", text, StringComparison.Ordinal);
        Assert.Contains("Open todos:", text, StringComparison.Ordinal);
    }
}
