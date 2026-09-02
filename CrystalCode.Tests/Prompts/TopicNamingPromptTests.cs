using CrystalCode.Prompts;
using CrystalCode.Tests.Home;
using CrystalCode.Tests.Tools;

using Xunit;

namespace CrystalCode.Tests.Prompts;

public sealed class TopicNamingPromptTests
{
    [Fact]
    public void Text_ContainsStableTitleRules()
    {
        Assert.Contains("return only one descriptive title", TopicNamingPrompt.Text, StringComparison.Ordinal);
        Assert.Contains("New conversation", TopicNamingPrompt.Text, StringComparison.Ordinal);
        Assert.Contains("secrets", TopicNamingPrompt.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadTopicNaming_ProjectOverrideWins()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        home.Home.EnsureCreated();
        Directory.CreateDirectory(home.Home.PromptsDirectory);
        var projectPrompts = Path.Combine(workspace.Path, ".crystal", "prompts");
        Directory.CreateDirectory(projectPrompts);
        File.WriteAllText(Path.Combine(home.Home.PromptsDirectory, "topic.md"), "home topic");
        File.WriteAllText(Path.Combine(projectPrompts, "topic.md"), "project topic");

        var prompt = new PromptStore(home.Home).LoadTopicNaming(workspace.Path);

        Assert.Equal("project topic", prompt);
    }
}
