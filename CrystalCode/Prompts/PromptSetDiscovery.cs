using CrystalCode.Home;

namespace CrystalCode.Prompts;

internal sealed class PromptSetDiscovery
{
    private readonly CrystalHome _home;

    public PromptSetDiscovery(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        _home = home;
    }

    public PromptSetCatalog Collect(IList<string> notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var sets = new Dictionary<string, PromptSetDefinition>(StringComparer.Ordinal);
        if (!Directory.Exists(_home.PromptSetsDirectory))
        {
            return new PromptSetCatalog(sets);
        }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(_home.PromptSetsDirectory);
        }
        catch (IOException)
        {
            notes.Add("Prompt sets could not be read.");
            return new PromptSetCatalog(sets);
        }
        catch (UnauthorizedAccessException)
        {
            notes.Add("Prompt sets could not be read.");
            return new PromptSetCatalog(sets);
        }

        Array.Sort(directories, StringComparer.Ordinal);
        foreach (var directory in directories)
        {
            var name = Path.GetFileName(directory);
            if (!PromptSetNames.IsValid(name)
                || string.Equals(name, PromptSetNames.Default, StringComparison.Ordinal))
            {
                notes.Add($"Prompt set '{name}' was skipped: directory name is invalid.");
                continue;
            }

            if (!HasPrompt(directory))
            {
                notes.Add($"Prompt set '{name}' was skipped: no prompt files were found.");
                continue;
            }

            sets[name] = new PromptSetDefinition(name, directory);
        }

        return new PromptSetCatalog(sets);
    }

    private static bool HasPrompt(string directory) =>
        PromptFiles.ReadNamed(directory, PromptNames.Work) is not null
        || PromptFiles.ReadNamed(directory, PromptNames.Plan) is not null
        || PromptFiles.ReadNamed(directory, PromptNames.Review) is not null;
}
