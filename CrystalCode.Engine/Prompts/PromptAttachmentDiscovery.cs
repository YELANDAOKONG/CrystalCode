using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Prompts;

/// <summary>
/// Finds prompt attachment directories in Home and the current workspace.
/// A workspace directory replaces the Home directory of the same name.
/// Parent directories are not walked.
/// </summary>
internal sealed class PromptAttachmentDiscovery
{
    public PromptAttachmentCatalog Collect(
        CrystalHome home,
        CrystalHome project,
        IList<string> notes)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(notes);
        var attachments = new Dictionary<string, PromptAttachmentDefinition>(StringComparer.Ordinal);
        AddRoot(attachments, notes, home.PromptAttachmentsDirectory, PromptAttachmentSource.Home);
        AddRoot(attachments, notes, project.PromptAttachmentsDirectory, PromptAttachmentSource.Workspace);
        return new PromptAttachmentCatalog(attachments);
    }

    private static void AddRoot(
        Dictionary<string, PromptAttachmentDefinition> attachments,
        IList<string> notes,
        string root,
        PromptAttachmentSource source)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(root);
        }
        catch (IOException)
        {
            notes.Add("Prompt attachments could not be read.");
            return;
        }
        catch (UnauthorizedAccessException)
        {
            notes.Add("Prompt attachments could not be read.");
            return;
        }

        Array.Sort(directories, StringComparer.Ordinal);
        foreach (var directory in directories)
        {
            var name = Path.GetFileName(directory);
            if (!PromptAttachmentNames.IsValid(name))
            {
                notes.Add($"Prompt attachment '{name}' was skipped: directory name is invalid.");
                continue;
            }

            if (!HasPrompt(directory))
            {
                notes.Add($"Prompt attachment '{name}' was skipped: no prompt files were found.");
                continue;
            }

            var replacedHome = source == PromptAttachmentSource.Workspace
                && attachments.ContainsKey(name);
            attachments[name] = new PromptAttachmentDefinition(name, directory, source, replacedHome);
        }
    }

    private static bool HasPrompt(string directory) =>
        PromptFiles.ReadNamed(directory, PromptNames.Work) is not null
        || PromptFiles.ReadNamed(directory, PromptNames.Plan) is not null
        || PromptFiles.ReadNamed(directory, PromptNames.Review) is not null;
}
