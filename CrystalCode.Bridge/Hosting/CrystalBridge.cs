using CrystalCode.Bridge.Transport;

namespace CrystalCode.Bridge.Hosting;

/// <summary>
/// Runs the ACP Agent over two streams until the input ends or
/// <c>cancellationToken</c> is cancelled. The caller owns the streams and
/// must keep everything else off <paramref name="output"/>.
/// </summary>
public static class CrystalBridge
{
    public static async Task RunAsync(
        BridgeOptions options,
        Stream input,
        Stream output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        await using var connection = new JsonRpcConnection(
            input,
            output,
            options.Debug is null ? null : new ProtocolLog(options.Debug));
        var agent = new BridgeAgent(options, connection);
        await connection.RunAsync(agent, cancellationToken);
    }
}
