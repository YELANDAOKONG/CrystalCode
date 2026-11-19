using System.Text.Json;

using CrystalCode.Plugins.Clients;

namespace CrystalCode.Providers.Protocol;

/// <summary>Captures routing identity and reports usage once for a complete or interrupted call.</summary>
internal sealed class ProtocolRequestTracking : IDisposable
{
    private readonly Func<JsonElement, ProviderRequestUsage?> _readUsage;
    private ProviderRequestUsage _usage = new(0);
    private int _attempts;

    public ProtocolRequestTracking(ProtocolOptions options, Func<JsonElement, ProviderRequestUsage?> readUsage)
    {
        _readUsage = readUsage;
        Options = options.RequestContext is { } context
            ? options with { ActiveRequest = context() }
            : options;
    }

    public ProtocolOptions Options { get; }

    public void RecordAttempts(HttpRequestMessage request)
    {
        if (Options.ActiveRequest is null)
        {
            return;
        }

        _attempts = request.Options.TryGetValue(ProviderHttpRequestOptions.InferenceAttempts, out var attempts)
            ? attempts
            : 1;
    }

    public void Observe(JsonElement root)
    {
        if (Options.ActiveRequest is not null && _readUsage(root) is { } usage)
        {
            _usage = usage;
        }
    }

    public void Dispose()
    {
        if (_attempts > 0)
        {
            Options.ActiveRequest!.ReportUsage(_usage with { HttpAttempts = _attempts });
        }
    }
}
