using Crystal.Chat;
using Crystal.Multimodal.Chat;

namespace CrystalCode.Engine.Sessions.Children;

internal sealed record ChildClients(IStreamingChatClient Text, IStreamingMultimodalChatClient? Images);
