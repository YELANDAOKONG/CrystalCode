using CrystalCode.Commands;

using Xunit;

namespace CrystalCode.Tests.Commands;

public sealed class PromptConsoleTests
{
    [Fact]
    public void Enable_ReportsTheHomeCopyWhenTheWorkspaceDirectoryHasNoManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "crystal-prompt-" + Guid.NewGuid().ToString("N"));
        var home = Path.Combine(root, "home");
        var workspace = Path.Combine(root, "workspace");
        var homeAlpha = Path.Combine(home, "prompt-attachments", "alpha");
        var workspaceAlpha = Path.Combine(workspace, ".crystal", "prompt-attachments", "alpha");
        Directory.CreateDirectory(homeAlpha);
        Directory.CreateDirectory(workspaceAlpha);
        File.WriteAllText(Path.Combine(homeAlpha, "work.md"), "home");
        File.WriteAllText(Path.Combine(homeAlpha, "prompt.json"), "{\n  \"enabled\": false\n}\n");
        File.WriteAllText(Path.Combine(workspaceAlpha, "work.md"), "workspace");
        var previous = Console.Out;
        var buffer = new StringWriter();
        Console.SetOut(buffer);
        try
        {
            var code = PromptConsole.Attachments(
                new PromptAttachmentCommandSettings
                {
                    Home = home,
                    Workspace = workspace,
                    Format = "text"
                },
                "alpha",
                "enable");

            Assert.Equal(0, code);
            var text = buffer.ToString();
            Assert.Contains(Path.Combine(homeAlpha, "prompt.json"), text, StringComparison.Ordinal);
            Assert.DoesNotContain("prompt.json is missing", text, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(previous);
            Directory.Delete(root, recursive: true);
        }
    }
}
