namespace CrystalCode.Plugins.Syntax;

public interface IPluginTokenizer
{
    IReadOnlyList<string> Languages { get; }

    Task<IReadOnlyList<PluginSyntaxToken>> TokenizeAsync(
        string language, string text, CancellationToken cancellationToken);
}
