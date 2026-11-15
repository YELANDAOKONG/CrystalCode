namespace CrystalCode.Engine.Tests.Plugins;

/// <summary>Fires the current one-shot syntax deadline only when the test requests it.</summary>
internal sealed class SyntaxTestTimeProvider : TimeProvider
{
    private OneShotTimer? _timer;

    public TimeSpan DueTime { get; private set; }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        DueTime = dueTime;
        _timer = new OneShotTimer(() => callback(state));
        return _timer;
    }

    public void Expire() => _timer!.Fire();

    private sealed class OneShotTimer(Action callback) : ITimer
    {
        private Action? _callback = callback;

        public void Fire() => Interlocked.Exchange(ref _callback, null)?.Invoke();

        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();

        public void Dispose() => Interlocked.Exchange(ref _callback, null);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
