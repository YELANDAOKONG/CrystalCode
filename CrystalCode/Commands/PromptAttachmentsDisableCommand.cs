using Spectre.Console.Cli;

namespace CrystalCode.Commands;

/// <summary>Disables one prompt attachment.</summary>
public sealed class PromptAttachmentsDisableCommand : AsyncCommand<NamedPromptAttachmentCommandSettings>
{
    public override Task<int> ExecuteAsync(
        CommandContext context,
        NamedPromptAttachmentCommandSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PromptConsole.Attachments(settings, settings.Directory, "disable"));
    }
}
