using Crystal.Chat;
using Crystal.Multimodal.Chat;

namespace CrystalCode.Engine.Sessions;

internal sealed record ChildClients(IStreamingChatClient Text, IStreamingMultimodalChatClient? Images);
