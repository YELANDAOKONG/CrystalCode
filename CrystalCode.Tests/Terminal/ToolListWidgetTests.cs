using Crystal.Tools;

using CrystalCode.Display.Paint;
using CrystalCode.Engine.Configuration;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.External;
using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Terminal;

public sealed class ToolListWidgetTests
{
    [Fact]
    public void Create_ListsHostAndExternalToolsWithinWidth()
    {
        var root = Directory.CreateTempSubdirectory("crystal-tool-widget-").FullName;
        try
        {
            var home = new CrystalHome(Path.Combine(root, "home"));
            var workspaceRoot = Path.Combine(root, "workspace");
            Directory.CreateDirectory(workspaceRoot);
            var directory = Path.Combine(home.ToolsDirectory, "web");
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
            var workspace = new Workspace(workspaceRoot);
            var external = ExternalCatalog.Load(
                home,
                workspace,
                enabled: true,
                approvalSettings: ExternalToolApprovalSettings.Default);
            var host = new ReadTool(workspace).Definition;

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
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Loaded tool assemblies can stay mapped until process exit.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
