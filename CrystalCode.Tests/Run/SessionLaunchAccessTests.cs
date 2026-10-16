using CrystalCode.Commands;
using CrystalCode.Engine.Tools;
using CrystalCode.Run;

using Xunit;

namespace CrystalCode.Tests.Run;

public sealed class SessionLaunchAccessTests
{
    [Fact]
    public void TryRead_RepeatedPathsAreCanonicalAndModesAreDistinct()
    {
        var root = Directory.CreateTempSubdirectory("crystal-access-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "reference"));
            Directory.CreateDirectory(Path.Combine(root, "output"));
            var settings = new SessionLaunchSettings
            {
                AllowRead = ["reference", "./reference"], AllowWrite = ["output"]
            };

            Assert.True(SessionLaunchAccess.TryRead(settings, root, out var rules, out var error), error);
            Assert.Equal(2, rules.Count);
            Assert.Contains(new DirectoryAccess(new Workspace(Path.Combine(root, "reference")).Root, "read"), rules);
            Assert.Contains(new DirectoryAccess(new Workspace(Path.Combine(root, "output")).Root, "write"), rules);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryRead_ConflictingModesAreRejected()
    {
        var root = Directory.CreateTempSubdirectory("crystal-access-").FullName;
        try
        {
            var settings = new SessionLaunchSettings { AllowRead = ["."], AllowWrite = [root] };
            Assert.False(SessionLaunchAccess.TryRead(settings, root, out var rules, out var error));
            Assert.Empty(rules);
            Assert.Contains("both --allow-read and --allow-write", error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData(".ssh")]
    public void TryRead_MissingOrCredentialDirectoryIsRejected(string name)
    {
        var root = Directory.CreateTempSubdirectory("crystal-access-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".ssh"));
            Assert.False(SessionLaunchAccess.TryRead(new SessionLaunchSettings { AllowRead = [name] }, root, out _, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
