using Crystal;
using CrystalCode.Sessions;
using CrystalCode.Tools;

namespace CrystalCode.Home;

internal static class SessionMapper
{
    public static List<SessionImageDocument> WriteImages(
        IEnumerable<ImageAttachment> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        return
        [
            .. images.OrderBy(static image => image.Number).Select(static image =>
                new SessionImageDocument
                {
                    Number = image.Number,
                    MimeType = image.MimeType,
                    Data = image.Data?.ToArray(),
                    Uri = image.Uri?.AbsoluteUri
                })
        ];
    }

    public static IReadOnlyDictionary<int, ImageAttachment> ReadImages(
        IEnumerable<SessionImageDocument>? documents)
    {
        var images = new Dictionary<int, ImageAttachment>();
        foreach (var document in documents ?? [])
        {
            if (document.Number <= 0
                || string.IsNullOrWhiteSpace(document.MimeType)
                || (document.Data is not { Length: > 0 }
                    && string.IsNullOrWhiteSpace(document.Uri))
                || document.Data?.Length > ImageFile.MaximumBytes)
            {
                continue;
            }

            ImageAttachment image;
            if (document.Data is { Length: > 0 })
            {
                image = new ImageAttachment(
                    document.Number,
                    document.MimeType,
                    document.Data);
            }
            else if (System.Uri.TryCreate(
                         document.Uri,
                         UriKind.Absolute,
                         out var uri)
                     && uri.Scheme is "http" or "https"
                     && uri.AbsoluteUri.Length <= 8192)
            {
                image = new ImageAttachment(
                    document.Number,
                    document.MimeType,
                    uri);
            }
            else
            {
                continue;
            }

            images.TryAdd(document.Number, image);
        }

        return images;
    }

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
