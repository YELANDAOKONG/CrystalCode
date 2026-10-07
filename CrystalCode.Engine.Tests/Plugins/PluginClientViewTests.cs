using Crystal.Chat;

using CrystalCode.Engine.Plugins;

using Xunit;

namespace CrystalCode.Engine.Tests.Plugins;

public sealed class PluginClientViewTests
{
    [Fact]
    public void Review_WhenReviewUsesTheSessionModel_DoesNotCreateAClient()
    {
        var created = 0;
        var view = new PluginClientView(
            () => false,
            () => new LaneClient(),
            () => null,
            () =>
            {
                created++;
                return new LaneClient();
            });

        Assert.False(view.IndependentReview);
        Assert.Null(view.Review);
        Assert.Equal(0, created);
    }

    [Fact]
    public void Review_WhenIndependentAndCreationFails_DoesNotSubstituteTheSessionClient()
    {
        var session = new LaneClient();
        var fail = true;
        var view = new PluginClientView(
            () => true,
            () => session,
            () => null,
            () =>
            {
                if (fail)
                {
                    throw new InvalidOperationException("The approval model could not be created.");
                }

                return new LaneClient();
            });

        var error = Assert.Throws<InvalidOperationException>(() => view.Review);
        Assert.Equal("The approval model could not be created.", error.Message);

        fail = false;
        var review = view.Review;

        Assert.NotNull(review);
        Assert.NotSame(session, review);
    }

    [Fact]
    public void ReleaseSession_DropsTheCachedClients()
    {
        var first = new LaneClient();
        var second = new LaneClient();
        var next = first;
        var imageCalls = 0;
        var view = new PluginClientView(
            () => false,
            () => next,
            () =>
            {
                imageCalls++;
                return null;
            },
            () => throw new InvalidOperationException("Review is using the session model."));

        Assert.Same(first, view.Session);
        Assert.Null(view.Images);
        Assert.Equal(1, imageCalls);
        Assert.Null(view.Images);
        Assert.Equal(1, imageCalls);

        next = second;
        view.ReleaseSession();

        Assert.True(first.Disposed);
        Assert.Same(second, view.Session);
        Assert.Null(view.Images);
        Assert.Equal(2, imageCalls);
    }

    [Fact]
    public void Review_WhenTheSwitchTurnsOff_DropsTheReviewClient()
    {
        var independent = true;
        var review = new LaneClient();
        var view = new PluginClientView(
            () => independent,
            () => new LaneClient(),
            () => null,
            () => review);

        Assert.Same(review, view.Review);

        independent = false;

        Assert.False(view.IndependentReview);
        Assert.Null(view.Review);
        Assert.True(review.Disposed);
    }

    private sealed class LaneClient : IStreamingChatClient, IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;

        public Task<ChatResponse> CompleteAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<ChatStreamEvent> StreamAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
