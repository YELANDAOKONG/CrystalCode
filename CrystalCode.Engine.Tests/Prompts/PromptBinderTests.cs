using System.Runtime.InteropServices;

using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Plugins.Environment;
using CrystalCode.Plugins.Models;
using CrystalCode.Plugins.Placeholders;

using Xunit;

namespace CrystalCode.Engine.Tests.Prompts;

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
            "Workspace=" + Path.GetFullPath("/tmp/demo")
                + " git=no date=Monday Aug 31, 2026 model=deepseek / deepseek-v4-flash mode=work product=Crystal Code",
            text);
    }

    [Fact]
    public void Apply_SubstitutesSessionAndSystemPlaceholders()
    {
        var context = PromptContext.Create(
            "/tmp/demo",
            "deepseek",
            "deepseek-v4-flash",
            "work",
            string.Empty,
            string.Empty,
            new DateTimeOffset(2026, 8, 31, 15, 4, 5, TimeSpan.FromHours(8)),
            "sess-1",
            "audit");

        var text = PromptBinder.Apply(
            "{{session_id}}|{{approval}}|{{time}}|{{os}}|{{architecture}}\n{{env}}",
            context);

        Assert.StartsWith(
            "sess-1|audit|15:04:05 +08:00|"
                + RuntimeInformation.OSDescription.Trim()
                + "|"
                + RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            text,
            StringComparison.Ordinal);
        Assert.Contains("Session: sess-1", text, StringComparison.Ordinal);
        Assert.Contains("Approval: audit", text, StringComparison.Ordinal);
        Assert.Contains("Local time: 15:04:05 +08:00", text, StringComparison.Ordinal);
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

        Assert.Equal(Path.GetFullPath("/tmp/demo"), text);
    }

    [Fact]
    public void Apply_SubstitutesGitRootSeparatelyFromTheWorkspaceFlag()
    {
        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".git"));
        var nested = Path.Combine(workspace.Path, "src");
        Directory.CreateDirectory(nested);
        var context = PromptContext.Create(
            nested,
            "openai",
            "gpt-4.1",
            "work",
            string.Empty,
            string.Empty);

        var text = PromptBinder.Apply("repo={{is_git_repo}} root={{git_root}}", context);

        Assert.Equal("repo=no root=" + Path.GetFullPath(workspace.Path), text);
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

    [Fact]
    public void Apply_InsertsPluginPlaceholderWithoutExpandingItsValue()
    {
        var context = PromptContext.Create(
            "/tmp/demo",
            "openai",
            "gpt-4.1",
            "work",
            string.Empty,
            string.Empty,
            sessionId: "sess",
            approval: "audit");
        var table = new PluginPlaceholderTable(
            [new PluginPlaceholderRegistration("build", "Acme", new FixedPlaceholder("build", "{{workspace}}"))],
            _ => { });

        var text = PromptBinder.Apply("{{build}} {{workspace}}", context, table);

        Assert.StartsWith("{{workspace}} ", text, StringComparison.Ordinal);
        Assert.EndsWith(Path.GetFullPath("/tmp/demo"), text, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_LeavesPluginPlaceholderWhenResolveFails()
    {
        var notes = new List<string>();
        var context = PromptContext.Create(
            "/tmp/demo",
            "openai",
            "gpt-4.1",
            "plan",
            string.Empty,
            string.Empty);
        var table = new PluginPlaceholderTable(
            [
                new PluginPlaceholderRegistration("boom", "Acme", new ThrowingPlaceholder()),
                new PluginPlaceholderRegistration("blank", "Acme", new FixedPlaceholder("blank", null))
            ],
            notes.Add);

        var text = PromptBinder.Apply("{{boom}} {{blank}}", context, table);

        Assert.Equal("{{boom}} {{blank}}", text);
        Assert.Contains(notes, note => note.Contains("failed", StringComparison.Ordinal));
        Assert.Contains(notes, note => note.Contains("returned no text", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_HostPlaceholderWinsOverPluginValue()
    {
        var context = PromptContext.Create(
            "/tmp/demo",
            "openai",
            "gpt-4.1",
            "work",
            string.Empty,
            string.Empty);
        var table = new PluginPlaceholderTable(
            [new PluginPlaceholderRegistration("workspace", "Acme", new FixedPlaceholder("workspace", "nope"))],
            _ => { });

        var text = PromptBinder.Apply("{{workspace}}", context, table);

        Assert.Equal(Path.GetFullPath("/tmp/demo"), text);
    }

    [Fact]
    public void Apply_GivesPluginPlaceholderTheReviewSwitch()
    {
        var context = PromptContext.Create(
            "/tmp/demo",
            "openai",
            "gpt-4.1",
            "work",
            string.Empty,
            string.Empty);
        var table = new PluginPlaceholderTable(
            [new PluginPlaceholderRegistration("review_model", "Acme", new ReviewPlaceholder())],
            _ => { });

        Assert.Equal("session", PromptBinder.Apply("{{review_model}}", context, table));

        table.SetModels(new PluginModels(
            new PluginModel(
                "openai",
                "openai",
                "gpt",
                contextWindow: 8,
                maxTokens: null,
                temperature: null,
                topP: null,
                imageInput: false,
                thinking: "low"),
            new PluginReview(
                true,
                new PluginModel(
                    "anthropic",
                    "anthropic",
                    "claude",
                    contextWindow: 8,
                    maxTokens: null,
                    temperature: null,
                    topP: null,
                    imageInput: false,
                    thinking: "default"))));

        Assert.Equal("own:anthropic:claude", PromptBinder.Apply("{{review_model}}", context, table));
    }

    private sealed class FixedPlaceholder : IPluginPlaceholder
    {
        private readonly string? _value;

        public FixedPlaceholder(string name, string? value)
        {
            Name = name;
            _value = value;
        }

        public string Name { get; }

        public string Resolve(PluginPlaceholderContext context) => _value!;
    }

    private sealed class ThrowingPlaceholder : IPluginPlaceholder
    {
        public string Name => "boom";

        public string Resolve(PluginPlaceholderContext context) =>
            throw new InvalidOperationException("broken");
    }

    private sealed class ReviewPlaceholder : IPluginPlaceholder
    {
        public string Name => "review_model";

        public string Resolve(PluginPlaceholderContext context)
        {
            var review = context.Models.Review;
            if (!review.Independent || review.Model is null)
            {
                return "session";
            }

            return "own:" + review.Model.Provider + ":" + review.Model.Name;
        }
    }
}
