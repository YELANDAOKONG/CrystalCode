using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Approvals;

public sealed class GrantStoreTests
{
    [Fact]
    public void Remember_PersistentGrant_SurvivesReload()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var call = new ToolCall(
            "1",
            WriteTool.ToolName,
            """{"path":"src/App.cs","contents":"x"}""");
        var first = new GrantStore(home.Home);
        first.Remember(workspace.Path, call, GrantScope.Persistent);

        var second = new GrantStore(home.Home);

        Assert.True(second.Contains(workspace.Path, call));
        Assert.True(File.Exists(home.Home.PermissionsPath));
    }
}
