using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Prompts;

public sealed class DirectoryAccessTextTests
{
    [Fact]
    public void Render_IsEmptyWhenTheSessionHasNoAddedRules()
    {
        Assert.Equal(string.Empty, DirectoryAccessText.Render([]));
    }

    [Fact]
    public void Render_ListsRulesInOrderWithTheSharedGuidance()
    {
        var text = DirectoryAccessText.Render(
        [
            new DirectoryAccess("/tmp/vendor", "read"),
            new DirectoryAccess("/tmp/sibling", "write")
        ]);

        Assert.StartsWith(
            """
            Session directory access (most-specific directory rule wins):
            - read: /tmp/vendor
            - write: /tmp/sibling
            """,
            text,
            StringComparison.Ordinal);
        Assert.Contains("Relative paths remain workspace-relative.", text, StringComparison.Ordinal);
        Assert.Contains("Only the operator can change access with /access.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("sourcePath", text, StringComparison.Ordinal);
    }
}
