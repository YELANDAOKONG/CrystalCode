using CrystalCode.Plugins.Syntax;

namespace CrystalCode.Engine.Plugins;

/// <summary>Runs registered tokenizers with bounded work and returns validated engine data.</summary>
public sealed class SyntaxService
{
    private const int MaximumInputLength = 65536;
    private const int MaximumTokens = 16384;
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(200);
    private readonly Dictionary<string, PluginTokenizerRegistration> _languages = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<IPluginTokenizer> _disabled = new(ReferenceEqualityComparer.Instance);
    private readonly object _gate = new();
    private readonly SemaphoreSlim _limiter;
    private readonly Action<string> _note;
    private readonly CancellationTokenSource _retirement = new();
    private int _retired;

    internal SyntaxService(
        IReadOnlyList<PluginTokenizerRegistration> registrations, SemaphoreSlim limiter, Action<string> note)
    {
        _limiter = limiter;
        _note = note;
        foreach (var registration in registrations)
        {
            foreach (var language in registration.Languages)
            {
                _languages.TryAdd(language, registration);
            }
        }
    }

    public bool Supports(string language)
    {
        lock (_gate)
        {
            return Volatile.Read(ref _retired) == 0
                && _languages.TryGetValue(language, out var registration)
                && !_disabled.Contains(registration.Tokenizer);
        }
    }

    public void Retire()
    {
        if (Interlocked.Exchange(ref _retired, 1) == 0)
        {
            _ = ObserveAsync(_retirement.CancelAsync());
        }
    }

    public async Task<IReadOnlyList<SyntaxToken>?> TokenizeAsync(
        string language, string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PluginTokenizerRegistration registration;
        lock (_gate)
        {
            if (text.Length > MaximumInputLength || !Supports(language)
                || !_languages.TryGetValue(language, out registration!) || !_limiter.Wait(0))
            {
                return null;
            }
        }

        var invocation = new CancellationTokenSource();
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _retirement.Token);
        // Both plugin entry and collection access run on a worker: either may block or throw.
        var work = Task.Run(async () =>
        {
            try
            {
                invocation.Token.ThrowIfCancellationRequested();
                var tokens = await registration.Tokenizer.TokenizeAsync(language.ToLowerInvariant(), text, invocation.Token);
                return Validate(tokens, text);
            }
            finally
            {
                _limiter.Release();
            }
        });
        try
        {
            var result = await work.WaitAsync(Budget, waiting.Token);
            cancellationToken.ThrowIfCancellationRequested();
            return Volatile.Read(ref _retired) == 0 && Supports(language) ? result : null;
        }
        catch (OperationCanceledException) when (waiting.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        catch (Exception)
        {
            Disable(registration);
            return null;
        }
        finally
        {
            var cancellation = invocation.CancelAsync();
            _ = CleanupAsync(work, cancellation, invocation);
        }
    }

    private void Disable(PluginTokenizerRegistration registration)
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _retired) != 0 || !_disabled.Add(registration.Tokenizer))
            {
                return;
            }
        }

        _note($"Plugin '{registration.DirectoryName}' syntax tokenizer was disabled until reload because it failed validation, failed to complete, or exceeded its time limit.");
    }

    private static IReadOnlyList<SyntaxToken> Validate(IReadOnlyList<PluginSyntaxToken> tokens, string text)
    {
        var count = tokens?.Count ?? -1;
        if (count < 0 || count > MaximumTokens)
        {
            throw new InvalidOperationException("Invalid syntax token count.");
        }

        var result = new SyntaxToken[count];
        var end = 0;
        for (var index = 0; index < result.Length; index++)
        {
            var token = tokens![index];
            if (token.Start < end || token.Length <= 0 || token.Start > text.Length
                || token.Length > text.Length - token.Start || !Enum.IsDefined(token.Kind)
                || SplitsSurrogate(text, token.Start) || SplitsSurrogate(text, token.Start + token.Length))
            {
                throw new InvalidOperationException("Invalid syntax token span.");
            }

            end = token.Start + token.Length;
            result[index] = new SyntaxToken(token.Start, token.Length, token.Kind.ToString().ToLowerInvariant());
        }

        return Array.AsReadOnly(result);
    }

    private static bool SplitsSurrogate(string text, int position) =>
        position > 0 && position < text.Length
        && char.IsHighSurrogate(text[position - 1]) && char.IsLowSurrogate(text[position]);

    private static async Task ObserveAsync(Task work)
    {
        try
        {
            await work;
        }
        catch (Exception)
        {
            // Plugin cancellation callbacks may throw; their text is never diagnostic output.
        }
    }

    private static async Task CleanupAsync(Task work, Task cancellation, CancellationTokenSource invocation)
    {
        await ObserveAsync(work);
        await ObserveAsync(cancellation);
        invocation.Dispose();
    }
}
