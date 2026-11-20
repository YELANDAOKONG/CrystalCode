using System.Text.Json;

using CrystalCode.Plugins.Clients;

namespace CrystalCode.Providers.Protocol;

/// <summary>Captures routing identity and reports usage once for a complete or interrupted call.</summary>
internal sealed class ProtocolRequestTracking : IDisposable
{
    private readonly ProviderRequestContext? _context;
    private readonly Func<JsonElement, ProviderRequestUsage, ProviderRequestUsage?> _readUsage;
    private ProviderRequestUsage _usage = new(0);
    private int _attempts;

    public ProtocolRequestTracking(
        Func<ProviderRequestContext>? requestContext,
        Func<JsonElement, ProviderRequestUsage, ProviderRequestUsage?> readUsage)
    {
        _context = requestContext?.Invoke();
        _readUsage = readUsage;
    }

    public ProtocolOptions Bind(ProtocolOptions options) =>
        _context is null ? options : options with { ActiveRequest = _context };

    public void RecordAttempts(HttpRequestMessage request)
    {
        if (_context is null)
        {
            return;
        }

        _attempts = request.Options.TryGetValue(ProviderHttpRequestOptions.InferenceAttempts, out var attempts)
            ? attempts
            : 1;
    }

    public void Observe(JsonElement root)
    {
        if (_context is not null && _readUsage(root, _usage) is { } usage)
        {
            _usage = usage with
            {
                InputTokens = usage.InputTokens ?? _usage.InputTokens,
                OutputTokens = usage.OutputTokens ?? _usage.OutputTokens,
                ReasoningTokens = usage.ReasoningTokens ?? _usage.ReasoningTokens,
                CacheReadTokens = usage.CacheReadTokens ?? _usage.CacheReadTokens,
                CacheWriteTokens = usage.CacheWriteTokens ?? _usage.CacheWriteTokens,
                UncachedInputTokens = usage.UncachedInputTokens ?? _usage.UncachedInputTokens,
                CacheWritesByLifetime = _usage.CacheWritesByLifetime is { } previous
                    && usage.CacheWritesByLifetime is { } reported
                    ? previous.SetItems(reported)
                    : usage.CacheWritesByLifetime ?? _usage.CacheWritesByLifetime
            };
        }
    }

    public void Dispose()
    {
        if (_attempts > 0)
        {
            _context!.ReportUsage(_usage with { HttpAttempts = _attempts });
        }
    }
}
