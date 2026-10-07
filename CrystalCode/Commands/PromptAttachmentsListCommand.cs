using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Lists prompt attachments, including disabled ones.</summary>
public sealed class PromptAttachmentsListCommand : AsyncCommand<PromptAttachmentCommandSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        PromptAttachmentCommandSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PromptConsole.Attachments(settings, null, "list"));
    }
}
