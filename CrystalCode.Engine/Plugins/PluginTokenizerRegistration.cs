using CrystalCode.Plugins.Syntax;

namespace CrystalCode.Engine.Plugins;

internal sealed record PluginTokenizerRegistration(
    string DirectoryName, IPluginTokenizer Tokenizer, IReadOnlyList<string> Languages);
