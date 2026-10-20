using Crystal.Chat;

namespace CrystalCode.Engine.Sessions;

/// <summary>Reserves one logical compaction call; its retries retain the reservation.</summary>
internal sealed class ChildSummaryClient(IChatClient inner, SharedTurnBudget? budget, Action started) : IChatClient
{
    private bool _reserved;

    public Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        if (!_reserved)
        {
            if (budget?.TryModelCall() == false)
            {
                throw new InvalidOperationException("The shared model-call budget is exhausted.");
            }
            _reserved = true;
            started();
        }
        return inner.CompleteAsync(request, cancellationToken);
    }
}
