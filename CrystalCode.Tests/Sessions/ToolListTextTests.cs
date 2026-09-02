using Crystal.Tools;

using CrystalCode.Configuration;
using CrystalCode.Display.Paint;
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

        Assert.Contains("Tools  2 loaded  ·  Plan 2  ·  Work 1", text, StringComparison.Ordinal);
        Assert.Contains("Host tools (1)", text, StringComparison.Ordinal);
        Assert.Contains("read  Plan     Host", text, StringComparison.Ordinal);
        Assert.Contains("External tools (1, On)", text, StringComparison.Ordinal);
        Assert.Contains("web   Plan+Work  Home/web  Always  Always", text, StringComparison.Ordinal);
        Assert.Contains("Home     Author", text, StringComparison.Ordinal);
        Assert.Contains("Project  Host", text, StringComparison.Ordinal);
        Assert.Contains("/tools home|project author|host", text, StringComparison.Ordinal);

        var lines = WidgetPaint.Lines(
            ToolListWidget.Create(
                [host, external.PlanTools[0].Definition],
                [external.WorkTools[0].Definition],
                external,
                HarnessSettings.CreateDefault()),
            88);

        Assert.Contains(lines, line => line.Plain.Contains("Host tools (1)", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("External tools (1, On)", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Plain.Contains("Effective", StringComparison.Ordinal));
        Assert.All(lines, line => Assert.True(TextWidth.Measure(line.Plain) <= 88));
    }
}
