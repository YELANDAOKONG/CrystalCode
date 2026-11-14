using CrystalCode.Plugins.Syntax;
using CrystalCode.Plugins.Syntax.Interfaces;

namespace CrystalCode.Engine.Plugins;

internal sealed record PluginTokenizerRegistration(
    string DirectoryName, IPluginTokenizer Tokenizer, IReadOnlyList<string> Languages);
