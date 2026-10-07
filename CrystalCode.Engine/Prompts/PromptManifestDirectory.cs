namespace CrystalCode.Engine.Prompts;

internal static class PromptManifestDirectory
{
    public static bool TryAccept(
        string directory,
        string name,
        string kind,
        IList<string> notes,
        out PromptManifest? manifest)
    {
        manifest = null;
        if (!HasPrompt(directory))
        {
            notes.Add($"{kind} '{name}' was skipped: no prompt files were found.");
            return false;
        }

        if (!PromptManifestFile.TryRead(directory, out manifest, out var error))
        {
            notes.Add($"{kind} '{name}' was skipped: {error}");
            return false;
        }

        return true;
    }

    public static bool HasPrompt(string directory) =>
        PromptFiles.ReadNamed(directory, PromptNames.Work) is not null
        || PromptFiles.ReadNamed(directory, PromptNames.Plan) is not null
        || PromptFiles.ReadNamed(directory, PromptNames.Review) is not null;

    public static string Title(string directoryName, PromptManifest manifest) =>
        manifest.Title.Length == 0 ? directoryName : manifest.Title;
}
