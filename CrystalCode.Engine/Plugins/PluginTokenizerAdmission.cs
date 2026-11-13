using CrystalCode.Plugins.Syntax;

namespace CrystalCode.Engine.Plugins;

internal static class PluginTokenizerAdmission
{
    internal static void Add(
        string directoryName,
        IEnumerable<IPluginTokenizer> tokenizers,
        List<PluginTokenizerRegistration> registrations,
        IList<string> notes)
    {
        var names = registrations.SelectMany(item => item.Languages)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tokenizer in tokenizers)
        {
            try
            {
                var languages = tokenizer?.Languages?.ToArray();
                if (languages is null || languages.Length == 0 || languages.Any(language => !IsLanguage(language)))
                {
                    notes.Add($"Plugin '{directoryName}' omitted a tokenizer with invalid language aliases.");
                    continue;
                }

                var accepted = new List<string>();
                foreach (var language in languages)
                {
                    if (names.Add(language))
                    {
                        accepted.Add(language.ToLowerInvariant());
                    }
                    else
                    {
                        notes.Add($"Plugin '{directoryName}' omitted duplicate tokenizer language '{language}'.");
                    }
                }

                if (accepted.Count > 0)
                {
                    registrations.Add(new PluginTokenizerRegistration(directoryName, tokenizer!, accepted.ToArray()));
                }
            }
            catch (Exception)
            {
                notes.Add($"Plugin '{directoryName}' omitted a tokenizer because registration failed.");
            }
        }
    }

    private static bool IsLanguage(string? language) =>
        language is { Length: > 0 and <= 64 }
        && char.IsAsciiLetter(language[0])
        && language.All(character => char.IsAsciiLetterOrDigit(character) || character is '+' or '#' or '_' or '-');
}
