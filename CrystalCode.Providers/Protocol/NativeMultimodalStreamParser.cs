using System.Text.Json;

using Crystal.Multimodal.Chat;
using CrystalCode.Providers.Compatible;

namespace CrystalCode.Providers.Protocol;

internal sealed class NativeMultimodalStreamParser : IMultimodalProtocolStreamParser
{
    private readonly IProtocolStreamParser _inner;
    private readonly CompatibleMultimodalOutput _output = new();

    public NativeMultimodalStreamParser(IProtocolStreamParser inner) => _inner = inner;

    public bool IsComplete => _inner.IsComplete;

    public IReadOnlyList<MultimodalChatStreamEvent> Parse(JsonElement root) =>
        _inner.Parse(root).SelectMany(_output.Convert).ToArray();
}
