using CrystalCode.Bridge.Tests.Support;
using CrystalCode.Bridge.Transport;

using Xunit;

namespace CrystalCode.Bridge.Tests;

/// <summary>The protocol debug log: both directions are recorded, secrets and bulk are not.</summary>
public sealed class BridgeDebugLogTests
{
    private const string Key = "sk-debug-log-secret-value";

    [Fact]
    public async Task Debug_LogsBothDirections()
    {
        var debug = new StringWriter();
        await using (var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false), debug: debug))
        {
            await fixture.RequestAsync("initialize", new { protocolVersion = 1 });
        }

        var text = debug.ToString();
        Assert.Contains("[client -> bridge]", text);
        Assert.Contains("[bridge -> client]", text);
        Assert.Contains("\"method\":\"initialize\"", text);
        Assert.Contains("\"protocolVersion\":1", text);
    }

    [Fact]
    public async Task WithoutDebug_NothingIsLogged()
    {
        await using var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false));

        await fixture.RequestAsync("initialize", new { protocolVersion = 1 });

        Assert.DoesNotContain("[client -> bridge]", fixture.Error);
    }

    [Fact]
    public async Task Debug_NeverWritesEditorMcpValues()
    {
        var debug = new StringWriter();
        await using (var fixture = await BridgeFixture.StartAsync(new ScriptedModel(false), debug: debug))
        {
            await fixture.RequestAsync("initialize", new { protocolVersion = 1 });

            await fixture.RequestAsync("session/new", new
            {
                cwd = fixture.Workspace,
                mcpServers = new object[]
                {
                    new { name = "x", command = "/bin/echo", args = new[] { Key }, env = new[] { new { name = "K", value = Key } } },
                    new { type = "http", name = "y", url = "https://example.invalid/" + Key, headers = new[] { new { name = "Authorization", value = Key } } }
                }
            });
        }

        var text = debug.ToString();
        Assert.Contains("\"method\":\"session/new\"", text);
        Assert.DoesNotContain(Key, text);
    }

    [Fact]
    public void Sanitize_ReplacesSensitiveFieldsAtAnyDepth()
    {
        var result = ProtocolLog.Sanitize("""{"params":{"nested":{"apiKey":"abc","Authorization":"Bearer abc","ok":1}}}""");

        Assert.DoesNotContain("abc", result);
        Assert.Contains("\"ok\":1", result);
        Assert.Contains(ProtocolLog.Redacted, result);
    }

    [Fact]
    public void Sanitize_CutsLongStrings()
    {
        var result = ProtocolLog.Sanitize("{\"data\":\"" + new string('A', 10_000) + "\"}");

        Assert.True(result.Length < 1_000);
        Assert.Contains("10000 characters", result);
    }

    [Fact]
    public void Sanitize_DoesNotEchoInvalidJson()
    {
        var result = ProtocolLog.Sanitize("{not json " + Key);

        Assert.DoesNotContain(Key, result);
    }
}
