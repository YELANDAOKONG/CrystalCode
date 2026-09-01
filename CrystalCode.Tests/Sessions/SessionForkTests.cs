using CrystalCode.Home;
using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class SessionForkTests
{
    [Fact]
    public void Create_CopiesStateWithNewIdentityAndWorkspace()
    {
        var source = new SessionDocument
        {
            Id = "source",
            Workspace = "/tmp/source",
            PlanMode = true,
            CreatedUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            UpdatedUtc = DateTimeOffset.Parse("2026-01-02T00:00:00Z"),
            Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "hello" }],
            Todos = [new SessionTodoDocument { Id = "1", Content = "work", Status = "pending" }],
            UserTurns = 3,
            ModelCalls = 4,
            ToolCalls = 5,
            Usage = new SessionUsageDocument { InputTokenCount = 10, OutputTokenCount = 2 }
        };
        var created = DateTimeOffset.Parse("2026-02-01T00:00:00Z");

        var fork = SessionFork.Create(source, "branch", "/tmp/current", created);

        Assert.Equal("branch", fork.Id);
        Assert.Equal(Path.GetFullPath("/tmp/current"), fork.Workspace);
        Assert.Equal(created, fork.CreatedUtc);
        Assert.Null(fork.UpdatedUtc);
        Assert.True(fork.PlanMode);
        Assert.Equal(3, fork.UserTurns);
        Assert.Equal(4, fork.ModelCalls);
        Assert.Equal(5, fork.ToolCalls);
        Assert.Equal("hello", fork.Items[0].Text);
        Assert.Equal("work", fork.Todos[0].Content);
        Assert.Equal(10, fork.Usage?.InputTokenCount);
        Assert.NotSame(source.Items[0], fork.Items[0]);
        Assert.NotSame(source.Todos[0], fork.Todos[0]);
        Assert.NotSame(source.Usage, fork.Usage);
    }
}
