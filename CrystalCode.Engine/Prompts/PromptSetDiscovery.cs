using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Prompts;

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
            if (string.Equals(name, PromptSetNames.Default, StringComparison.Ordinal))
            {
                notes.Add($"Prompt set '{name}' was skipped: '{PromptSetNames.Default}' is reserved.");
                continue;
            }

            if (!PromptSetNames.IsValid(name))
            {
                notes.Add($"Prompt set '{name}' was skipped: directory name is invalid.");
                continue;
            }

            if (!PromptManifestDirectory.TryAccept(directory, name, "Prompt set", notes, out var manifest)
                || manifest is null)
            {
                continue;
            }

            sets[name] = new PromptSetDefinition(name, directory, manifest);
        }

        return new PromptSetCatalog(sets);
    }
}
