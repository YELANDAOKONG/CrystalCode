using Crystal.Chat;
using Crystal.Reasoning;

using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

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
            Usage = new SessionUsageDocument { InputTokenCount = 10, OutputTokenCount = 2 },
            CumulativeUsage = new SessionUsageDocument { InputTokenCount = 30, OutputTokenCount = 6 }
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
        Assert.Null(fork.Archive);
        Assert.Equal("work", fork.Todos[0].Content);
        Assert.Equal(10, fork.Usage?.InputTokenCount);
        Assert.Equal(30, fork.CumulativeUsage?.InputTokenCount);
        Assert.NotSame(source.Items[0], fork.Items[0]);
        Assert.NotSame(source.Todos[0], fork.Todos[0]);
        Assert.NotSame(source.Usage, fork.Usage);
        Assert.NotSame(source.CumulativeUsage, fork.CumulativeUsage);
    }

    [Fact]
    public void Create_CopiesTheArchive()
    {
        var source = new SessionDocument
        {
            Id = "source",
            Workspace = "/tmp/source",
            Items = [new SessionItemDocument { Kind = "message", Role = "user", Text = "tail" }],
            Archive = [new SessionItemDocument { Kind = "message", Role = "user", Text = "original" }]
        };

        var fork = SessionFork.Create(source, "branch", "/tmp/current", DateTimeOffset.UnixEpoch);

        var archived = Assert.Single(fork.Archive!);
        Assert.Equal("original", archived.Text);
        Assert.NotSame(source.Archive![0], archived);
    }

    [Fact]
    public void Create_CopiesReasoningTextAndState()
    {
        var segment = new SessionReasoningSegment { Text = "looked", Kind = "summary" };
        var sourceItem = new SessionItemDocument
        {
            Kind = "reasoning",
            Segments = [segment],
            StateFormat = "gemini.part",
            StateData = Convert.ToBase64String("signed"u8.ToArray())
        };
        var archived = new SessionItemDocument
        {
            Kind = "reasoning",
            StateFormat = "anthropic.messages.thinking",
            StateData = Convert.ToBase64String("{\"signature\":\"sig\"}"u8.ToArray())
        };
        var source = new SessionDocument
        {
            Id = "source",
            Workspace = "/tmp/source",
            Items = [sourceItem],
            Archive = [archived]
        };

        var fork = SessionFork.Create(source, "branch", "/tmp/current", DateTimeOffset.UnixEpoch);

        var item = Assert.Single(fork.Items);
        Assert.NotSame(sourceItem, item);
        Assert.NotSame(sourceItem.Segments, item.Segments);
        var copied = Assert.Single(item.Segments!);
        Assert.NotSame(segment, copied);
        Assert.Equal("looked", copied.Text);
        Assert.Equal("summary", copied.Kind);
        Assert.Equal(sourceItem.StateFormat, item.StateFormat);
        Assert.Equal(sourceItem.StateData, item.StateData);

        var archiveItem = Assert.Single(fork.Archive!);
        Assert.NotSame(archived, archiveItem);
        Assert.Null(archiveItem.Segments);
        Assert.Equal(archived.StateFormat, archiveItem.StateFormat);
        Assert.Equal(archived.StateData, archiveItem.StateData);

        var restored = TranscriptCodec.Read(fork.Items);
        var reasoning = Assert.IsType<ChatReasoningItem>(Assert.Single(restored));
        Assert.Equal("looked", Assert.Single(reasoning.Content.TextSegments).Text);
        Assert.Equal(ReasoningTextKind.Summary, reasoning.Content.TextSegments[0].Kind);
        Assert.Equal(sourceItem.StateFormat, reasoning.Content.State!.Format);

        var restoredArchive = TranscriptCodec.Read(fork.Archive!);
        var archivedReasoning = Assert.IsType<ChatReasoningItem>(Assert.Single(restoredArchive));
        Assert.Empty(archivedReasoning.Content.TextSegments);
        Assert.Equal(archived.StateFormat, archivedReasoning.Content.State!.Format);
    }
}
