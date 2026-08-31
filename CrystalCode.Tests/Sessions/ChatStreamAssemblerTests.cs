using Crystal;
using Crystal.Chat;

using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class ChatStreamAssemblerTests
{
    [Fact]
    public void ToResponse_AssemblesAssistantText()
    {
        var assembler = new ChatStreamAssembler();
        assembler.Apply(new ChatTextDelta(0, 0, ChatRole.Assistant, "Hel"));
        assembler.Apply(new ChatTextDelta(0, 0, ChatRole.Assistant, "lo"));
        assembler.Apply(new ChatCandidateCompleted(0, FinishReason.Stop));
        assembler.Apply(new ChatUsageReceived(new TokenUsage(3, 2)));

        var response = assembler.ToResponse();

        var message = Assert.IsType<ChatMessage>(response.Candidates[0].Items[0]);
        Assert.Equal("Hello", message.Text);
        Assert.Equal(3, response.Usage?.InputTokenCount);
        Assert.Equal(2, response.Usage?.OutputTokenCount);
    }
}
