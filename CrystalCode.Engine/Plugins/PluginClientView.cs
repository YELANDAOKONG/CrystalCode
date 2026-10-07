using Crystal.Chat;
using Crystal.Multimodal.Chat;

using CrystalCode.Plugins.Clients;

namespace CrystalCode.Engine.Plugins;

/// <summary>
/// Lazily builds side-channel clients and drops them when the session
/// replaces the model they serve. A plugin call must not share the client
/// that is already streaming a turn or a review.
/// </summary>
internal sealed class PluginClientView : IPluginClients
{
    private readonly Func<bool> _independentReview;
    private readonly Func<IStreamingChatClient> _createSession;
    private readonly Func<IStreamingMultimodalChatClient?> _createImages;
    private readonly Func<IStreamingChatClient> _createReview;
    private readonly object _gate = new();
    private IStreamingChatClient? _session;
    private IStreamingMultimodalChatClient? _images;
    private IStreamingChatClient? _review;
    private bool _imagesReady;

    public PluginClientView(
        Func<bool> independentReview,
        Func<IStreamingChatClient> createSession,
        Func<IStreamingMultimodalChatClient?> createImages,
        Func<IStreamingChatClient> createReview)
    {
        ArgumentNullException.ThrowIfNull(independentReview);
        ArgumentNullException.ThrowIfNull(createSession);
        ArgumentNullException.ThrowIfNull(createImages);
        ArgumentNullException.ThrowIfNull(createReview);
        _independentReview = independentReview;
        _createSession = createSession;
        _createImages = createImages;
        _createReview = createReview;
    }

    public bool IndependentReview => _independentReview();

    public IStreamingChatClient Session
    {
        get
        {
            lock (_gate)
            {
                if (_session is not null)
                {
                    return _session;
                }

                var created = _createSession();
                _session = created;
                return created;
            }
        }
    }

    public IStreamingMultimodalChatClient? Images
    {
        get
        {
            lock (_gate)
            {
                if (_imagesReady)
                {
                    return _images;
                }

                var created = _createImages();
                _images = created;
                _imagesReady = true;
                return created;
            }
        }
    }

    public IStreamingChatClient? Review
    {
        get
        {
            lock (_gate)
            {
                if (!_independentReview())
                {
                    DisposeClient(_review);
                    _review = null;
                    return null;
                }

                if (_review is not null)
                {
                    return _review;
                }

                var created = _createReview();
                _review = created;
                return created;
            }
        }
    }

    public void ReleaseSession()
    {
        lock (_gate)
        {
            DisposeClient(_session);
            DisposeClient(_images);
            _session = null;
            _images = null;
            _imagesReady = false;
        }
    }

    public void ReleaseReview()
    {
        lock (_gate)
        {
            DisposeClient(_review);
            _review = null;
        }
    }

    public override string ToString() => nameof(PluginClientView);

    private static void DisposeClient(object? client)
    {
        (client as IDisposable)?.Dispose();
    }
}
