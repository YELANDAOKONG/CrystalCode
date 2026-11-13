using CrystalCode.Display.Paint;
using CrystalCode.Engine.Plugins;

namespace CrystalCode.Terminal;

/// <summary>Queues tokenization away from painting and retains theme-independent token spans.</summary>
internal sealed class TerminalSyntaxCache : IDisposable
{
    internal const int MaximumEntries = 128;
    internal const long MaximumBytes = 8 * 1024 * 1024;
    internal const int MaximumQueued = 32;
    private const int MaximumInputLength = 65536;
    private const int WorkerCount = 2;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(25);
    private readonly object _gate = new();
    private readonly Dictionary<(string Language, string Text), Entry> _entries = [];
    private readonly LinkedList<(string Language, string Text)> _lru = [];
    private readonly List<Request> _queue = [];
    private readonly HashSet<(string Language, string Text)> _active = [];
    private readonly CancellationTokenSource _stop = new();
    private readonly TimeSpan _debounce;
    private readonly Task[] _workers;
    private CancellationTokenSource _generationStop = new();
    private Func<string, string, CancellationToken, Task<IReadOnlyList<SyntaxToken>?>>? _tokenize;
    private Func<string, bool>? _supports;
    private long _generation;
    private long _bytes;
    private int _dirty;
    private bool _disposed;

    internal TerminalSyntaxCache(TimeSpan? debounce = null)
    {
        _debounce = debounce ?? TimeSpan.FromMilliseconds(150);
        _workers = Enumerable.Range(0, WorkerCount).Select(_ => Task.Run(WorkAsync)).ToArray();
    }

    public void SetService(SyntaxService service) => SetService(service.TokenizeAsync, service.Supports);

    internal void SetService(
        Func<string, string, CancellationToken, Task<IReadOnlyList<SyntaxToken>?>> tokenize,
        Func<string, bool> supports)
    {
        CancellationTokenSource previous;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            previous = _generationStop;
            _generationStop = new CancellationTokenSource();
            _generation++;
            _tokenize = tokenize;
            _supports = supports;
            ClearUnlocked();
        }

        _ = CancelAsync(previous);
    }

    public IReadOnlyList<SyntaxSpan>? GetTokens(string language, string text) => GetTokens(language, text, schedule: true);

    public IReadOnlyList<SyntaxSpan>? LookupTokens(string language, string text) => GetTokens(language, text, schedule: false);

    private IReadOnlyList<SyntaxSpan>? GetTokens(string language, string text, bool schedule)
    {
        lock (_gate)
        {
            if (_disposed || _tokenize is null || text.Length > MaximumInputLength || !_supports!(language))
            {
                return null;
            }

            var key = (language.ToLowerInvariant(), text);
            if (_entries.TryGetValue(key, out var entry))
            {
                _lru.Remove(entry.Node);
                _lru.AddFirst(entry.Node);
                return _supports!(key.Item1) ? entry.Spans : null;
            }

            if (!schedule || _active.Contains(key) || _queue.Any(request => request.Key == key))
            {
                return null;
            }

            if (_queue.Count >= MaximumQueued)
            {
                _queue.RemoveAt(0);
            }

            _queue.Add(new Request(key, DateTimeOffset.UtcNow + _debounce));
            return null;
        }
    }

    public bool ConsumeRefresh()
    {
        lock (_gate)
        {
            var unsupported = _entries.Where(pair => !_supports!(pair.Key.Language)).ToArray();
            foreach (var (key, entry) in unsupported)
            {
                _entries.Remove(key);
                _lru.Remove(entry.Node);
                _bytes -= entry.Bytes;
                if (entry.Spans is { Count: > 0 })
                {
                    Interlocked.Exchange(ref _dirty, 1);
                }
            }

            return Interlocked.Exchange(ref _dirty, 0) != 0;
        }
    }

    internal (int Entries, long Bytes, int Queued, int Active) Counts
    {
        get
        {
            lock (_gate)
            {
                return (_entries.Count, _bytes, _queue.Count, _active.Count);
            }
        }
    }

    private async Task WorkAsync()
    {
        var stopToken = _stop.Token;
        try
        {
            while (true)
            {
                await Task.Delay(PollInterval, stopToken);
                Request? request;
                long generation;
                CancellationToken token;
                Func<string, string, CancellationToken, Task<IReadOnlyList<SyntaxToken>?>> tokenize;
                lock (_gate)
                {
                    request = _queue.FirstOrDefault(item => item.ReadyAt <= DateTimeOffset.UtcNow);
                    if (request is null || _tokenize is null)
                    {
                        continue;
                    }

                    _queue.Remove(request);
                    _active.Add(request.Key);
                    generation = _generation;
                    token = _generationStop.Token;
                    tokenize = _tokenize;
                }

                IReadOnlyList<SyntaxSpan>? spans = null;
                try
                {
                    var work = tokenize(request.Key.Language, request.Key.Text, token);
                    _ = ObserveAsync(work);
                    var tokens = await work.WaitAsync(token);
                    spans = tokens is null ? null : Array.AsReadOnly(tokens.Select(item =>
                        new SyntaxSpan(item.Start, item.Length, item.Kind)).ToArray());
                }
                catch (Exception)
                {
                    // Optional highlighting fails back to the ordinary code palette.
                }

                lock (_gate)
                {
                    if (_disposed || generation != _generation)
                    {
                        continue;
                    }

                    _active.Remove(request.Key);
                    var bytes = 128L + (request.Key.Language.Length + request.Key.Text.Length) * 2L
                        + (spans?.Sum(span => 64L + span.Kind.Length * 2L) ?? 0);
                    if (bytes <= MaximumBytes)
                    {
                        while (_lru.Last is { } last
                            && (_entries.Count >= MaximumEntries || _bytes + bytes > MaximumBytes))
                        {
                            _bytes -= _entries[last.Value].Bytes;
                            _entries.Remove(last.Value);
                            _lru.RemoveLast();
                        }

                        var node = _lru.AddFirst(request.Key);
                        _entries.Add(request.Key, new Entry(spans, bytes, node));
                        _bytes += bytes;
                        if (spans is { Count: > 0 })
                        {
                            Interlocked.Exchange(ref _dirty, 1);
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
    }

    private void ClearUnlocked()
    {
        _entries.Clear();
        _lru.Clear();
        _queue.Clear();
        _active.Clear();
        _bytes = 0;
        Interlocked.Exchange(ref _dirty, 1);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _generation++;
            ClearUnlocked();
            _tokenize = null;
            _supports = null;
        }

        _ = CancelAsync(_generationStop);
        _ = StopWorkersAsync();
    }

    internal Task Completion => Task.WhenAll(_workers);

    internal long Generation
    {
        get
        {
            lock (_gate)
            {
                return _generation;
            }
        }
    }

    private async Task StopWorkersAsync()
    {
        await ObserveAsync(_stop.CancelAsync());
        await ObserveAsync(Completion);
        _stop.Dispose();
    }

    private static async Task CancelAsync(CancellationTokenSource source)
    {
        await ObserveAsync(source.CancelAsync());
        source.Dispose();
    }

    private static async Task ObserveAsync(Task work)
    {
        try
        {
            await work;
        }
        catch (Exception)
        {
            // Late plugin failures and cancellation callbacks must remain observed.
        }
    }

    private sealed record Request((string Language, string Text) Key, DateTimeOffset ReadyAt);
    private sealed record Entry(IReadOnlyList<SyntaxSpan>? Spans, long Bytes,
        LinkedListNode<(string Language, string Text)> Node);
}
