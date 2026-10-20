using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Sessions;

/// <summary>Serialized child records stored with a parent session.</summary>
internal sealed class ChildSessionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ChildSessionDocument> _children = new(StringComparer.Ordinal);

    public ChildSessionStore(IEnumerable<ChildSessionDocument>? children = null)
    {
        foreach (var child in children ?? [])
        {
            if (child is not null && !string.IsNullOrWhiteSpace(child.Id))
            {
                _children[child.Id] = child with
                {
                    Status = child.Status is "running" or "queued" ? "interrupted" : child.Status,
                    StopReason = child.Status is "running" or "queued" ? "interrupted" : child.StopReason,
                    Items = child.Items ?? [], Archive = child.Archive ?? [], Images = child.Images ?? [],
                    Todos = child.Todos ?? [], Tools = child.Tools ?? [], Notes = child.Notes ?? [],
                    Model = child.Model ?? new Configuration.AgentModelSettings()
                };
            }
        }
    }

    public IReadOnlyList<ChildSessionDocument> Snapshot()
    {
        lock (_gate)
        {
            return _children.Values.ToArray();
        }
    }

    public bool TryGet(string id, out ChildSessionDocument child)
    {
        lock (_gate)
        {
            return _children.TryGetValue(id, out child!);
        }
    }

    public void Set(ChildSessionDocument child)
    {
        ArgumentNullException.ThrowIfNull(child);
        lock (_gate)
        {
            _children[child.Id] = child;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _children.Clear();
        }
    }
}
