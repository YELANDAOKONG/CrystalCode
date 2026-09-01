using Crystal.Tools;

using CrystalCode.Configuration;
using CrystalCode.Home;
using CrystalCode.Sessions;
using CrystalCode.Tests.Home;
using CrystalCode.Tests.Tools;
using CrystalCode.Tools;
using CrystalCode.Tools.External;

using Xunit;

namespace CrystalCode.Tests.Sessions;

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

        Assert.Contains("Approval: Home Author, Project Host", text, StringComparison.Ordinal);
        Assert.Contains("read  Host  Plan", text, StringComparison.Ordinal);
        Assert.Contains("web  Home:web  Plan+Work  Author Always  Effective Always", text, StringComparison.Ordinal);
    }
}
