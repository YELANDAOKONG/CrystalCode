namespace CrystalCode.Engine.Sessions;

/// <summary>
/// What one submitted input did, collected on its own async flow: the first
/// error the engine wrote for it, whether it recorded a next-turn setting,
/// and whether it queued a follow-up.
/// A turn or side question it starts does not report here.
/// </summary>
internal sealed class SessionInputCapture
{
    private readonly object _gate = new();
    private string? _error;
    private bool _pending;
    private bool _queued;

    public string? Error
    {
        get
        {
            lock (_gate)
            {
                return _error;
            }
        }
    }

    public bool Pending
    {
        get
        {
            lock (_gate)
            {
                return _pending;
            }
        }
    }

    public bool Queued
    {
        get
        {
            lock (_gate)
            {
                return _queued;
            }
        }
    }

    public void MarkQueued()
    {
        lock (_gate)
        {
            _queued = true;
        }
    }

    public void Fail(string text)
    {
        lock (_gate)
        {
            _error ??= text;
        }
    }

    public void Defer()
    {
        lock (_gate)
        {
            _pending = true;
        }
    }
}
