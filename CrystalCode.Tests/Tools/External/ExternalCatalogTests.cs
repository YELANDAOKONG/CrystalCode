using Crystal.Tools;

using CrystalCode.Home;
using CrystalCode.Tests.Home;
using CrystalCode.Tests.Tools;
using CrystalCode.Tools;
using CrystalCode.Tools.External;

using Xunit;

namespace CrystalCode.Tests.Tools.External;

public sealed class ExternalCatalogTests
{
    [Fact]
    public async Task Load_ExecStdinJson_RunsCommand()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(workspace.Path, ".crystal", "tools", "echojson");
        Directory.CreateDirectory(directory);
        var script = WriteStdinScript(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            $$"""
            {
              "runner": "exec",
              "description": "Echo stdin.",
              "schema": { "type": "object", "properties": { "tag": { "type": "string" } } },
              "command": ["{{script.Replace("\\", "/")}}"]
            }
            """);
        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);

        Assert.Empty(catalog.Notes);
        var tool = Assert.Single(catalog.WorkTools);
        Assert.Contains(catalog.PlanTools, item => item.Definition.Name == "echojson");
        var output = await tool.InvokeAsync(
            new ToolCall("1", "echojson", """{"tag":"ok"}"""));

        Assert.True(output.Status == ToolResultStatus.Success, output.Text);
        Assert.Contains("ok", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_ExecStdinJson_DoesNotWriteUtf8Bom()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(workspace.Path, ".crystal", "tools", "echojson");
        Directory.CreateDirectory(directory);
        var script = WriteStdinProbeScript(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            $$"""
            {
              "runner": "exec",
              "description": "Capture stdin bytes.",
              "schema": { "type": "object", "properties": {} },
              "command": ["{{script.Replace("\\", "/")}}"]
            }
            """);
        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);

        var output = await catalog.WorkTools[0].InvokeAsync(
            new ToolCall("1", "echojson", """{"tag":"ok"}"""));

        Assert.True(output.Status == ToolResultStatus.Success, output.Text);
        var probe = Path.Combine(workspace.Path, "stdin.bin");
        Assert.True(File.Exists(probe), output.Text);
        var bytes = File.ReadAllBytes(probe);
        Assert.NotEmpty(bytes);
        Assert.NotEqual((byte)0xEF, bytes[0]);
        Assert.Equal((byte)'{', bytes[0]);
    }

    [Fact]
    public async Task Load_ExecArgv_AppendsFlags()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(workspace.Path, ".crystal", "tools", "echoargv");
        Directory.CreateDirectory(directory);
        var script = WriteArgvScript(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            $$"""
            {
              "runner": "exec",
              "description": "Echo argv.",
              "schema": { "type": "object", "properties": { "environment": { "type": "string" } } },
              "command": ["{{script.Replace("\\", "/")}}"],
              "stdin": false,
              "argv": { "environment": "--env" }
            }
            """);
        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);

        Assert.Empty(catalog.Notes);
        var output = await catalog.WorkTools[0].InvokeAsync(
            new ToolCall("1", "echoargv", """{"environment":"prod"}"""));

        Assert.True(output.Status == ToolResultStatus.Success, output.Text);
        Assert.Contains("prod", output.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_Disabled_IsEmpty()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteManifest(workspace.Path, "echojson", """["/bin/true"]""");
        var catalog = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: false);

        Assert.Empty(catalog.WorkTools);
        Assert.Empty(catalog.PlanTools);
    }

    [Fact]
    public void Load_AddsExternalToolToWorkspaceCatalog()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(workspace.Path, ".crystal", "tools", "echojson");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            """
            {
              "runner": "exec",
              "description": "Echo.",
              "schema": { "type": "object", "properties": {} },
              "command": ["/bin/true"],
              "catalogs": ["work"]
            }
            """);
        var external = ExternalCatalog.Load(
            home.Home,
            new Workspace(workspace.Path),
            enabled: true);

        var plan = WorkspaceCatalog.CreatePlan(
            new Workspace(workspace.Path),
            new TodoList(),
            new FixedUserPrompt("ok"),
            external: external);
        var work = WorkspaceCatalog.CreateWork(
            new Workspace(workspace.Path),
            new TodoList(),
            new FixedUserPrompt("ok"),
            external: external);

        Assert.Null(plan.Find("echojson"));
        Assert.NotNull(work.Find("echojson"));
    }

    private static void WriteManifest(string workspace, string name, string commandJson)
    {
        var directory = Path.Combine(workspace, ".crystal", "tools", name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, ExternalFiles.FileName),
            $$"""
            {
              "runner": "exec",
              "description": "Echo.",
              "schema": { "type": "object", "properties": {} },
              "command": {{commandJson}}
            }
            """);
    }

    private static string WriteStdinScript(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(directory, "echo-stdin.cmd");
            File.WriteAllText(script, "@echo off\r\nmore\r\n");
            return script;
        }

        var path = Path.Combine(directory, "echo-stdin.sh");
        File.WriteAllText(path, "#!/bin/sh\ncat\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherExecute);
        return path;
    }

    private static string WriteArgvScript(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(directory, "echo-argv.cmd");
            File.WriteAllText(script, "@echo off\r\necho %*\r\n");
            return script;
        }

        var path = Path.Combine(directory, "echo-argv.sh");
        File.WriteAllText(path, "#!/bin/sh\nprintf '%s\\n' \"$@\"\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherExecute);
        return path;
    }

    private static string WriteStdinProbeScript(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(directory, "probe-stdin.cmd");
            File.WriteAllText(
                script,
                """
                @echo off
                powershell -NoProfile -Command "$s=[Console]::OpenStandardInput(); $f=[IO.File]::Create('stdin.bin'); $s.CopyTo($f); $f.Close()"
                """);
            return script;
        }

        var path = Path.Combine(directory, "probe-stdin.sh");
        File.WriteAllText(path, "#!/bin/sh\ncat > stdin.bin\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherExecute);
        return path;
    }
}
