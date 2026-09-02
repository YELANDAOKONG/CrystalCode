using CrystalCode.Prompts;
using CrystalCode.Sessions;
using CrystalCode.Tests.Tools;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class PromptTemplateExportTests
{
    [Fact]
    public void Write_WritesBuiltInTemplatesWithPlaceholders()
    {
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(workspace.Path, "prompt-export");

        var written = PromptTemplateExport.Write(directory);

        Assert.Equal(7, written.Count);
        var work = File.ReadAllText(Path.Combine(directory, "work.md"));
        Assert.Contains("{{env}}", work, StringComparison.Ordinal);
        Assert.Equal(WorkPrompt.Text, work);
        Assert.Contains("{{conversation}}", File.ReadAllText(Path.Combine(directory, "review.user.md")), StringComparison.Ordinal);
        Assert.Contains("{{prior_summary_section}}", File.ReadAllText(Path.Combine(directory, "compaction.user.md")), StringComparison.Ordinal);
        Assert.Contains("{{product_name}}", File.ReadAllText(Path.Combine(directory, "placeholders.md")), StringComparison.Ordinal);
    }
}
