using CrystalCode.Engine.Home;
using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Prompts;

public sealed class PromptInventoryTests
{
    [Fact]
    public void Enable_TurnsOffEveryOtherPromptSet()
    {
        using var home = new TemporaryHome();
        var concise = Set(home.Home, "concise", enabled: true);
        var strict = Set(home.Home, "strict", enabled: false);

        Assert.True(PromptSetInventory.TryEnable(home.Home, "strict", out var error), error);

        Assert.Contains("\"enabled\": false", File.ReadAllText(Path.Combine(concise, "prompt.json")), StringComparison.Ordinal);
        Assert.Contains("\"enabled\": true", File.ReadAllText(Path.Combine(strict, "prompt.json")), StringComparison.Ordinal);
        var rows = PromptSetInventory.List(home.Home);
        Assert.False(rows.Single(row => row.DirectoryName == "concise").Effective);
        Assert.True(rows.Single(row => row.DirectoryName == "strict").Effective);
    }

    [Fact]
    public void Enable_AppendsAnAttachmentAndRewritesOrder()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        Attachment(home.Home, "alpha", enabled: true, order: 0);
        var beta = Attachment(home.Home, "beta", enabled: false, order: null);

        Assert.True(
            PromptAttachmentInventory.TrySetEnabled(
                home.Home,
                workspace.Path,
                "beta",
                source: null,
                enabled: true,
                out var error),
            error);

        var rows = PromptAttachmentInventory.List(home.Home, workspace.Path)
            .OrderBy(row => row.Order)
            .ToArray();
        Assert.Equal(["alpha", "beta"], rows.Select(row => row.DirectoryName));
        Assert.Equal([0, 1], rows.Select(row => row.Order));
        Assert.Contains("\"order\": 1", File.ReadAllText(Path.Combine(beta, "prompt.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Move_SwapsEnabledAttachments()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        Attachment(home.Home, "alpha", enabled: true, order: 0);
        Attachment(home.Home, "beta", enabled: true, order: 1);

        Assert.True(
            PromptAttachmentInventory.TryMove(home.Home, workspace.Path, "beta", earlier: true, out var error),
            error);
        Assert.False(
            PromptAttachmentInventory.TryMove(home.Home, workspace.Path, "beta", earlier: true, out var first));

        var rows = PromptAttachmentInventory.List(home.Home, workspace.Path)
            .OrderBy(row => row.Order)
            .Select(row => row.DirectoryName);
        Assert.Equal(["beta", "alpha"], rows);
        Assert.Contains("already first", first, StringComparison.Ordinal);
    }

    private static string Set(CrystalHome home, string name, bool enabled)
    {
        var directory = Path.Combine(home.PromptSetsDirectory, name);
        Write(directory, enabled, order: null);
        return directory;
    }

    private static string Attachment(CrystalHome home, string name, bool enabled, int? order)
    {
        var directory = Path.Combine(home.PromptAttachmentsDirectory, name);
        Write(directory, enabled, order);
        return directory;
    }

    private static void Write(string directory, bool enabled, int? order)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "work.md"), "body");
        var orderLine = order is int value ? ",\n  \"order\": " + value : string.Empty;
        File.WriteAllText(
            Path.Combine(directory, "prompt.json"),
            "{\n  \"enabled\": " + (enabled ? "true" : "false") + orderLine + "\n}\n");
    }
}
