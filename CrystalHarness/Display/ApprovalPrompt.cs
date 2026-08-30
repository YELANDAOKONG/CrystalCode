using Crystal.Tools;

using CrystalHarness.Approvals;

using Spectre.Console;

namespace CrystalHarness.Display;

/// <summary>
/// Inline permission card. Keys, not a Live selection widget.
/// </summary>
public sealed class ApprovalPrompt : IApprovalPrompt
{
    private readonly SessionRenderer _renderer;

    public ApprovalPrompt(SessionRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    public void NotifyPassed(
        ToolCall call,
        ToolClassification classification,
        ApprovalPassReason reason,
        ApprovalReviewVerdict? review = null)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(reason);
        _renderer.WriteApprovalPass(
            ApprovalCard.PassLines(call, classification, reason, review));
    }

    public async ValueTask<ApprovalChoice> AskAsync(
        ToolCall call,
        ToolClassification classification,
        ApprovalReviewVerdict? review = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(classification);
        _renderer.CloseStream();
        WriteCard(call, classification, review);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = await ReadKeyAsync(cancellationToken);
            switch (key.Key)
            {
                case ConsoleKey.D1:
                case ConsoleKey.Y:
                case ConsoleKey.Enter:
                    Console.WriteLine();
                    return ApprovalChoice.AllowOnce;
                case ConsoleKey.D2:
                    Console.WriteLine();
                    return ApprovalChoice.AllowSession;
                case ConsoleKey.D3:
                    Console.WriteLine();
                    return ApprovalChoice.AllowPersistent;
                case ConsoleKey.D4:
                case ConsoleKey.Escape:
                case ConsoleKey.N:
                    Console.WriteLine();
                    return ApprovalChoice.Deny;
                default:
                    break;
            }
        }
    }

    private static void WriteCard(
        ToolCall call,
        ToolClassification classification,
        ApprovalReviewVerdict? review)
    {
        AnsiConsole.WriteLine();
        SessionRenderer.WriteRule();
        AnsiConsole.MarkupLine(
            $"[{Theme.Review}]  {MarkupText.Escape(ApprovalCard.ActionLine(call))}[/]");
        AnsiConsole.MarkupLine(
            $"[{Theme.Chrome}]  {MarkupText.Escape(ApprovalCard.HostLine(classification))}[/]");
        if (review is not null)
        {
            AnsiConsole.MarkupLine(
                $"[{Theme.Review}]  {MarkupText.Escape(ApprovalCard.ReviewLine(review))}[/]");
            AnsiConsole.MarkupLine(
                $"[{Theme.Chrome}]  {MarkupText.Escape(review.Rationale)}[/]");
        }

        AnsiConsole.MarkupLine(
            $"[{Theme.Chrome}]  1 once  ·  2 session  ·  3 always  ·  4 deny[/]");
        AnsiConsole.MarkupLine(
            $"[{Theme.Chrome}]  enter once  ·  esc deny[/]");
        SessionRenderer.WriteRule();
    }

    private static async Task<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken)
    {
        while (!Console.KeyAvailable)
        {
            await Task.Delay(40, cancellationToken);
        }

        return Console.ReadKey(intercept: true);
    }
}
