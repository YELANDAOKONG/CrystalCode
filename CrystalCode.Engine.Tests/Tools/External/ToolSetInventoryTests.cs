using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools.External;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools.External;

public sealed class ToolSetInventoryTests
{
    [Fact]
    public void TrySetEnabled_PreservesTheCommandAndListsTheDisabledSet()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(home.Home.ToolsDirectory, "web");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ExternalFiles.FileName);
        File.WriteAllText(
            path,
            """
            {
              "runner": "exec",
              "approval": "always",
              "description": "Search.",
              "schema": { "type": "object", "properties": {} },
              "command": ["/bin/true"]
            }
            """);

        Assert.True(ToolSetInventory.TrySetEnabled(
            home.Home,
            workspace.Path,
            "web",
            null,
            false,
            out var entry,
            out var changed,
            out var error));
        Assert.True(changed);
        Assert.Equal(string.Empty, error);
        Assert.NotNull(entry);
        Assert.False(entry.Enabled);
        Assert.Equal("exec", entry.Runner);
        Assert.Equal("always", entry.Approval);
        Assert.Equal("web (always)", Assert.Single(entry.Tools));
        var json = File.ReadAllText(path);
        Assert.Contains("\"/bin/true\"", json, StringComparison.Ordinal);
        Assert.Contains("\"enabled\": false", json, StringComparison.Ordinal);

        var listed = ToolSetInventory.List(home.Home, workspace.Path);
        Assert.Contains(listed, item => item.DirectoryName == "web" && item.Enabled == false);
        var text = string.Join('\n', ToolSetInventory.Format(entry));
        Assert.Contains("Runner", text, StringComparison.Ordinal);
        Assert.Contains("exec", text, StringComparison.Ordinal);
    }
}
