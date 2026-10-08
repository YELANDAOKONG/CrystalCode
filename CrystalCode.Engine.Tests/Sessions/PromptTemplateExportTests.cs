using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tests.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class PromptTemplateExportTests
{
    [Fact]
    public void Write_WritesBuiltInTemplatesWithPlaceholders()
    {
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(workspace.Path, "prompt-export");

        var written = PromptTemplateExport.Write(directory);

        Assert.Equal(10, written.Count);
        var work = File.ReadAllText(Path.Combine(directory, "work.md"));
        Assert.Contains("{{env}}", work, StringComparison.Ordinal);
        Assert.Equal(WorkPrompt.Text, work);
        Assert.Contains("{{conversation}}", File.ReadAllText(Path.Combine(directory, "review.user.md")), StringComparison.Ordinal);
        Assert.Contains("{{prior_summary_section}}", File.ReadAllText(Path.Combine(directory, "compaction.user.md")), StringComparison.Ordinal);
        var guide = File.ReadAllText(Path.Combine(directory, "placeholders.md"));
        foreach (var name in PromptPlaceholder.All)
        {
            Assert.Contains("{{" + name + "}}", guide, StringComparison.Ordinal);
        }
        Assert.Contains("New conversation", File.ReadAllText(Path.Combine(directory, "topic.md")), StringComparison.Ordinal);
    }
}
