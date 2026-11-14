namespace CrystalCode.Plugins.Syntax.Interfaces;

public interface IPluginTokenizer
{
    IReadOnlyList<string> Languages { get; }

    Task<IReadOnlyList<PluginSyntaxToken>> TokenizeAsync(
        string language, string text, CancellationToken cancellationToken);
}
