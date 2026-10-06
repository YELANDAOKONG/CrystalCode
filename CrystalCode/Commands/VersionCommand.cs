using System.Runtime.InteropServices;

using CrystalCode.Engine.Version;

using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>
/// Prints the build identity and exits.
/// </summary>
public sealed class VersionCommand : AsyncCommand<VersionSettings>
{
    public static bool IsVersionRequest(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args is ["--version"];
    }

    public static void Write(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        var identity = BuildIdentityReader.Read(
            typeof(Program).Assembly,
            RuntimeInformation.FrameworkDescription);
        writer.WriteLine(BuildIdentityText.Format(identity));
    }

    public override Task<int> ExecuteAsync(
        CommandContext context,
        VersionSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        Write(Console.Out);
        return Task.FromResult(0);
    }
}
