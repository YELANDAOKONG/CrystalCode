using Crystal;

using CrystalHarness.Tools;

namespace CrystalHarness.Home;

internal static class SessionMapper
{
    public static List<SessionTodoDocument> WriteTodos(IEnumerable<TodoItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var documents = new List<SessionTodoDocument>();
        foreach (var item in items)
        {
            documents.Add(
                new SessionTodoDocument
                {
                    Id = item.Id,
                    Content = item.Content,
                    Status = TodoList.StatusName(item.Status)
                });
        }

        return documents;
    }

    public static List<TodoItem> ReadTodos(IEnumerable<SessionTodoDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var items = new List<TodoItem>();
        foreach (var document in documents)
        {
            if (string.IsNullOrWhiteSpace(document.Id)
                || string.IsNullOrWhiteSpace(document.Content)
                || !TodoList.TryParseStatus(document.Status, out var status))
            {
                continue;
            }

            items.Add(new TodoItem(document.Id, document.Content, status));
        }

        return items;
    }

    public static SessionUsageDocument? WriteUsage(TokenUsage? usage)
    {
        if (usage is null)
        {
            return null;
        }

        return new SessionUsageDocument
        {
            InputTokenCount = usage.InputTokenCount,
            OutputTokenCount = usage.OutputTokenCount,
            ReasoningTokenCount = usage.ReasoningTokenCount
        };
    }

    public static TokenUsage? ReadUsage(SessionUsageDocument? document)
    {
        if (document is null)
        {
            return null;
        }

        if (document.InputTokenCount < 0 || document.OutputTokenCount < 0)
        {
            return null;
        }

        if (document.ReasoningTokenCount is < 0)
        {
            return null;
        }

        return new TokenUsage(
            document.InputTokenCount,
            document.OutputTokenCount,
            document.ReasoningTokenCount);
    }
}
