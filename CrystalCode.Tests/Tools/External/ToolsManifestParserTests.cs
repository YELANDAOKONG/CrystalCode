using CrystalCode.Tests.Tools;
using CrystalCode.Tools.External;

using Xunit;

namespace CrystalCode.Tests.Tools.External;

public sealed class ToolsManifestParserTests
{
    [Fact]
    public void TryParse_ExecShorthand_UsesDirectoryNameAndBothCatalogs()
    {
        using var root = new TemporaryWorkspace();
        var directory = Path.Combine(root.Path, "deploy");
        Directory.CreateDirectory(directory);

        var parsed = ToolsManifestParser.TryParse(
            directory,
            """
            {
              "name": "ignored-set-name",
              "runner": "exec",
              "description": "Ship it.",
              "schema": { "type": "object", "properties": {} },
              "command": ["/bin/true"]
            }
            """,
            out var set,
            out var error);

        Assert.True(parsed, error);
        Assert.NotNull(set);
        Assert.Equal(ExternalRunnerKind.Exec, set.Runner);
        Assert.True(set.Catalogs.Plan);
        Assert.True(set.Catalogs.Work);
        Assert.Equal("deploy", set.Tools[0].Name);
        Assert.True(set.Tools[0].Catalogs.Plan);
        Assert.True(set.Tools[0].Catalogs.Work);
        Assert.True(set.Enabled);
    }

    [Fact]
    public void TryParse_ExecToolsArray_ReadsMultipleNames()
    {
        using var root = new TemporaryWorkspace();
        var directory = Path.Combine(root.Path, "Acme.Tools");
        Directory.CreateDirectory(directory);

        var parsed = ToolsManifestParser.TryParse(
            directory,
            """
            {
              "runner": "exec",
              "command": ["acme"],
              "catalogs": ["work"],
              "tools": [
                {
                  "name": "acme_deploy",
                  "description": "Deploy.",
                  "schema": { "type": "object", "properties": { "environment": { "type": "string" } } },
                  "command": ["deploy"],
                  "argv": { "environment": "--env" }
                },
                {
                  "name": "acme_inventory",
                  "description": "Inventory.",
                  "schema": { "type": "object", "properties": {} },
                  "command": ["inventory"],
                  "catalogs": ["plan", "work"]
                }
              ]
            }
            """,
            out var set,
            out var error);

        Assert.True(parsed, error);
        Assert.NotNull(set);
        Assert.Equal(2, set.Tools.Count);
        Assert.False(set.Tools[0].Catalogs.Plan);
        Assert.True(set.Tools[0].Catalogs.Work);
        Assert.True(set.Tools[1].Catalogs.Plan);
        Assert.Equal("--env", set.Tools[0].Argv["environment"]);
    }

    [Fact]
    public void TryParse_MixesShorthandAndTools_Fails()
    {
        using var root = new TemporaryWorkspace();
        var directory = Path.Combine(root.Path, "mixed");
        Directory.CreateDirectory(directory);

        var parsed = ToolsManifestParser.TryParse(
            directory,
            """
            {
              "runner": "exec",
              "description": "nope",
              "schema": { "type": "object", "properties": {} },
              "command": ["acme"],
              "tools": [
                {
                  "name": "acme_deploy",
                  "description": "Deploy.",
                  "schema": { "type": "object", "properties": {} }
                }
              ]
            }
            """,
            out _,
            out var error);

        Assert.False(parsed);
        Assert.Contains("shorthand", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_ReservedToolName_Fails()
    {
        using var root = new TemporaryWorkspace();
        var directory = Path.Combine(root.Path, "read");
        Directory.CreateDirectory(directory);

        var parsed = ToolsManifestParser.TryParse(
            directory,
            """
            {
              "runner": "exec",
              "description": "nope",
              "schema": { "type": "object", "properties": {} },
              "command": ["/bin/true"]
            }
            """,
            out _,
            out var error);

        Assert.False(parsed);
        Assert.Contains("not a valid tool name", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParse_EnabledFalse_Parses()
    {
        using var root = new TemporaryWorkspace();
        var directory = Path.Combine(root.Path, "deploy");
        Directory.CreateDirectory(directory);

        var parsed = ToolsManifestParser.TryParse(
            directory,
            """
            {
              "runner": "exec",
              "enabled": false,
              "description": "Ship it.",
              "schema": { "type": "object", "properties": {} },
              "command": ["/bin/true"]
            }
            """,
            out var set,
            out var error);

        Assert.True(parsed, error);
        Assert.NotNull(set);
        Assert.False(set.Enabled);
    }

    [Fact]
    public void TryParse_EnabledNotBoolean_Fails()
    {
        using var root = new TemporaryWorkspace();
        var directory = Path.Combine(root.Path, "deploy");
        Directory.CreateDirectory(directory);

        var parsed = ToolsManifestParser.TryParse(
            directory,
            """
            {
              "runner": "exec",
              "enabled": "no",
              "description": "Ship it.",
              "schema": { "type": "object", "properties": {} },
              "command": ["/bin/true"]
            }
            """,
            out _,
            out var error);

        Assert.False(parsed);
        Assert.Contains("enabled must be a boolean", error, StringComparison.Ordinal);
    }
}
