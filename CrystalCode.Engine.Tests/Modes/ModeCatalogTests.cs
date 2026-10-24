using CrystalCode.Engine.Home;
using CrystalCode.Engine.Modes;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Modes;

public sealed class ModeCatalogTests
{
    private static void WriteMode(string root, string name, string manifest, string? prompt = "Be careful.")
    {
        var directory = Path.Combine(root, "modes", name);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "mode.json"), manifest);
        if (prompt is not null)
        {
            File.WriteAllText(Path.Combine(directory, "prompt.md"), prompt);
        }
    }

    private const string Enabled = """{"description":"Audit","enabled":true,"tools":["read","grep"]}""";

    [Fact]
    public void Load_ReadsEnabledModesInNameOrder()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteMode(home.Root, "zeta", Enabled);
        WriteMode(home.Root, "alpha", Enabled);

        var catalog = ModeCatalog.Load(home.Home, workspace.Path);

        Assert.Equal(["alpha", "zeta"], catalog.Definitions.Select(mode => mode.Name));
        Assert.Equal(["read", "grep"], catalog.Definitions[0].Tools);
        Assert.False(catalog.Definitions[0].HasWriteTools);
    }

    [Fact]
    public void Load_DisabledModeIsOmittedWithoutANote()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteMode(home.Root, "idle", """{"description":"x","tools":["read"]}""");

        var catalog = ModeCatalog.Load(home.Home, workspace.Path);

        Assert.Empty(catalog.Definitions);
        Assert.Empty(catalog.Notes);
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("Work")]
    [InlineData("default")]
    public void Load_ReservedNameIsSkipped(string name)
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteMode(home.Root, name, Enabled);

        var catalog = ModeCatalog.Load(home.Home, workspace.Path);

        Assert.Empty(catalog.Definitions);
        Assert.Contains(catalog.Notes, note => note.Contains("reserved", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"enabled":true,"tools":["read"]}""", "description")]
    [InlineData("""{"description":"x","enabled":true}""", "tools")]
    [InlineData("""{"description":"x","enabled":true,"tools":["nope"]}""", "Unknown built-in tool")]
    [InlineData("{ not json", "")]
    public void Load_InvalidManifestIsSkippedWithANote(string manifest, string expected)
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteMode(home.Root, "bad", manifest);

        var catalog = ModeCatalog.Load(home.Home, workspace.Path);

        Assert.Empty(catalog.Definitions);
        var note = Assert.Single(catalog.Notes);
        Assert.Contains("Mode 'bad' skipped", note, StringComparison.Ordinal);
        Assert.Contains(expected, note, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_MissingPromptIsSkipped()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteMode(home.Root, "nopromptfile", Enabled, prompt: null);

        var catalog = ModeCatalog.Load(home.Home, workspace.Path);

        Assert.Empty(catalog.Definitions);
        Assert.Single(catalog.Notes);
    }

    [Fact]
    public void Load_WorkspaceEntryReplacesHomeEntryEvenWhenDisabled()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteMode(home.Root, "review", Enabled);
        WriteMode(Path.Combine(workspace.Path, ".crystal"), "review", """{"description":"x","enabled":false,"tools":["read"]}""");

        var catalog = ModeCatalog.Load(home.Home, workspace.Path);

        Assert.Empty(catalog.Definitions);
    }

    [Fact]
    public void Load_WorkspaceEntryOverridesHomePrompt()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteMode(home.Root, "review", Enabled, "HOME");
        WriteMode(Path.Combine(workspace.Path, ".crystal"), "review", Enabled, "PROJECT");

        var catalog = ModeCatalog.Load(home.Home, workspace.Path);

        Assert.Equal("PROJECT", Assert.Single(catalog.Definitions).Prompt);
    }

    [Theory]
    [InlineData("""["read","edit"]""", true)]
    [InlineData("""["read","bash"]""", true)]
    [InlineData("""["read","bash_start"]""", true)]
    [InlineData("""["read","task"]""", false)]
    public void HasWriteTools_FollowsListedTools(string tools, bool expected)
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteMode(home.Root, "m", $$"""{"description":"x","enabled":true,"tools":{{tools}}}""");

        var mode = Assert.Single(ModeCatalog.Load(home.Home, workspace.Path).Definitions);

        Assert.Equal(expected, mode.HasWriteTools);
    }

    [Fact]
    public void ModeSelection_AcceptsBuiltInsAndEnabledModesOnly()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WriteMode(home.Root, "audit", Enabled);

        Assert.True(ModeSelection.TryResolve(home.Home, workspace.Path, "PLAN", out _));
        Assert.True(ModeSelection.TryResolve(home.Home, workspace.Path, "audit", out _));
        Assert.False(ModeSelection.TryResolve(home.Home, workspace.Path, "ghost", out var error));
        Assert.Contains("ghost", error, StringComparison.Ordinal);
    }
}
