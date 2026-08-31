using Crystal;

using CrystalCode.Home;
using CrystalCode.Tools;

using Xunit;

namespace CrystalCode.Tests.Home;

public sealed class SessionMapperTests
{
    [Fact]
    public void WriteTodos_ThenReadTodos_RoundTripsKnownStatus()
    {
        var written = SessionMapper.WriteTodos(
            [new TodoItem("1", "add tests", TodoStatus.InProgress)]);

        var read = SessionMapper.ReadTodos(written);

        Assert.Equal("in_progress", written[0].Status);
        Assert.Equal([new TodoItem("1", "add tests", TodoStatus.InProgress)], read);
    }

    [Fact]
    public void ReadTodos_SkipsInvalidRows()
    {
        var items = SessionMapper.ReadTodos(
            [
                new SessionTodoDocument { Id = "1", Content = "ok", Status = "pending" },
                new SessionTodoDocument { Id = "", Content = "missing id", Status = "pending" },
                new SessionTodoDocument { Id = "2", Content = "bad", Status = "nope" }
            ]);

        Assert.Equal([new TodoItem("1", "ok", TodoStatus.Pending)], items);
    }

    [Fact]
    public void ReadUsage_RejectsNegativeCounts()
    {
        Assert.Null(
            SessionMapper.ReadUsage(
                new SessionUsageDocument
                {
                    InputTokenCount = -1,
                    OutputTokenCount = 1
                }));
        Assert.Equal(
            new TokenUsage(10, 2, 1),
            SessionMapper.ReadUsage(
                new SessionUsageDocument
                {
                    InputTokenCount = 10,
                    OutputTokenCount = 2,
                    ReasoningTokenCount = 1
                }));
    }
}
