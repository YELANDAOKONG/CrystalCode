using System.Text.Json;

using Crystal.Multimodal.Chat;

namespace CrystalCode.Providers.Protocol;

internal interface IMultimodalProtocolStreamParser
{
    bool IsComplete { get; }

    IReadOnlyList<MultimodalChatStreamEvent> Parse(JsonElement root);
}
