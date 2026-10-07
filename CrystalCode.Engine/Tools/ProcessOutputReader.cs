using System.Diagnostics;
using System.Text;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Drains both redirected streams while retaining only the model-visible prefix.
/// </summary>
internal static class ProcessOutputReader
{
    public static async Task<string> ReadAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        var streams = await ReadStreamsAsync(process, cancellationToken);
        if (streams.Stderr.Length == 0)
        {
            return ToolOutputText.Truncate(streams.Stdout);
        }

        return ToolOutputText.Truncate(
            streams.Stdout.Length == 0
                ? streams.Stderr
                : streams.Stdout + ToolOutputText.LineSeparator + streams.Stderr);
    }

    public static async Task<Streams> ReadStreamsAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(process);
        var stdoutTask = ReadStreamAsync(process.StandardOutput, cancellationToken);
        var stderrTask = ReadStreamAsync(process.StandardError, cancellationToken);
        await Task.WhenAll(stdoutTask, stderrTask);
        return new Streams(await stdoutTask, await stderrTask);
    }

    internal readonly record struct Streams(string Stdout, string Stderr);

    private static async Task<string> ReadStreamAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var retained = new StringBuilder();
        var buffer = new char[4096];
        var limit = WorkspaceLimits.MaximumToolOutputCharacters + 1;
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0)
            {
                return retained.ToString();
            }

            var remaining = limit - retained.Length;
            if (remaining > 0)
            {
                retained.Append(buffer, 0, Math.Min(count, remaining));
            }
        }
    }
}
