using System.Text.Json;

using Crystal.Chat;

namespace CrystalCode.Providers.Protocol;

internal interface IProtocolStreamParser
{
    bool IsComplete { get; }

    IReadOnlyList<ChatStreamEvent> Parse(JsonElement root);
}
