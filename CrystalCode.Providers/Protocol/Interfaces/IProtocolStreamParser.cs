using System.Text.Json;

using Crystal.Chat;

namespace CrystalCode.Providers.Protocol.Interfaces;
internal interface IProtocolStreamParser
{
    bool IsComplete { get; }

    IReadOnlyList<ChatStreamEvent> Parse(JsonElement root);
}
