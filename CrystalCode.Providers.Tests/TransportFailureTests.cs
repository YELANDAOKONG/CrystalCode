using System.Net.Sockets;

using CrystalCode.Providers;

using Xunit;

namespace CrystalCode.Providers.Tests;

public sealed class TransportFailureTests
{
    [Fact]
    public void Describe_AppendsDistinctCauses()
    {
        var exception = new HttpRequestException(
            "Error while copying content to a stream.",
            new IOException(
                "Unable to write data to the transport connection: Broken pipe.",
                // Windows formats SocketError.Shutdown as a distinct WSAESHUTDOWN
                // sentence. Pin the text so the duplicate-cause check is stable.
                new SocketException((int)SocketError.Shutdown, "Broken pipe.")));

        var message = TransportFailure.Describe("DeepSeek chat request failed.", exception);

        Assert.Equal(
            "DeepSeek chat request failed. Error while copying content to a stream. Unable to write data to the transport connection: Broken pipe.",
            message);
    }

    [Fact]
    public void Describe_KeepsSummaryWhenTheCauseRepeatsIt()
    {
        var exception = new HttpRequestException("DeepSeek chat request failed.");

        Assert.Equal(
            "DeepSeek chat request failed.",
            TransportFailure.Describe("DeepSeek chat request failed.", exception));
    }
}
