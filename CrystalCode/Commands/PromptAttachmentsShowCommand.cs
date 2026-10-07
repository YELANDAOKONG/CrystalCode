using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Shows one prompt attachment manifest.</summary>
public sealed class PromptAttachmentsShowCommand : AsyncCommand<NamedPromptAttachmentCommandSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        NamedPromptAttachmentCommandSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PromptConsole.Attachments(settings, settings.Directory, "show"));
    }
}
