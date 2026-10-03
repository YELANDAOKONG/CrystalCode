using Crystal.Tools;

using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.External;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class ToolListTextTests
{
    [Fact]
    public void Format_ListsHostAndExternalApprovalDetails()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(home.Home.ToolsDirectory, "web");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            """
            {
              "runner": "exec",
              "approval": "always",
              "description": "Search.",
              "schema": { "type": "object", "properties": {} },
              "command": ["/bin/true"]
            }
            """);
        var external = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true,
            approvalSettings: ExternalToolApprovalSettings.Default);
        var host = new ReadTool(new Workspace(workspace.Path)).Definition;

        var text = ToolListText.Format(
            [host, external.PlanTools[0].Definition],
            [external.WorkTools[0].Definition],
            external,
            HarnessSettings.CreateDefault());

        Assert.Contains("Tools  2 loaded  ·  Plan 2  ·  Work 1", text, StringComparison.Ordinal);
        Assert.Contains("Host tools (1)", text, StringComparison.Ordinal);
        Assert.Contains("read  Plan     Host", text, StringComparison.Ordinal);
        Assert.Contains("External tools (1, On)", text, StringComparison.Ordinal);
        Assert.Contains("web   Plan+Work  Home/web  Always  Always", text, StringComparison.Ordinal);
        Assert.Contains("Home     Author", text, StringComparison.Ordinal);
        Assert.Contains("Project  Host", text, StringComparison.Ordinal);
        Assert.Contains("/tools home|project author|host", text, StringComparison.Ordinal);
    }
}
