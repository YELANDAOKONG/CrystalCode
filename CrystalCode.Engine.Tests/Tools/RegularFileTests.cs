using System.Diagnostics;

using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class RegularFileTests
{
    [Fact]
    public void IsRegular_AcceptsANormalFile()
    {
        using var workspace = new TemporaryWorkspace();
        var file = Path.Combine(workspace.Path, "note.txt");
        File.WriteAllText(file, "hi");

        Assert.True(RegularFile.IsRegular(file));
    }

    [Fact(Timeout = 5000)]
    public async Task IsRegular_RejectsFifo()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using var workspace = new TemporaryWorkspace();
        var pipe = Path.Combine(workspace.Path, "shot.pipe");
        using var created = Process.Start(new ProcessStartInfo
        {
            FileName = "mkfifo",
            ArgumentList = { pipe },
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        Assert.NotNull(created);
        Assert.True(created.WaitForExit(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, created.ExitCode);

        Assert.False(RegularFile.IsRegular(pipe));
        await Task.CompletedTask;
    }
}
