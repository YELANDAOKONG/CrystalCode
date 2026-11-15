namespace CrystalCode.Plugins.Authentication.Interfaces;

/// <summary>
/// What a sign-in flow shows the operator and asks of them. The front end
/// decides how: a popup, a form, or console lines.
/// </summary>
public interface IPluginLogin
{
    /// <summary>Shows a line of text.</summary>
    Task ShowAsync(string text, CancellationToken cancellationToken);

    /// <summary>
    /// Shows a link with a short label so it can be copied whole. Call it when
    /// <see cref="OpenLinkAsync"/> returned false.
    /// </summary>
    Task ShowLinkAsync(string label, Uri link, CancellationToken cancellationToken);

    /// <summary>
    /// Asks for a line of text, such as the address a browser ended on. Throws
    /// <see cref="OperationCanceledException"/> when the operator cancels.
    /// </summary>
    Task<string> ReadAsync(string prompt, CancellationToken cancellationToken);

    /// <summary>
    /// Tries to open a link in the operator's browser. Returns false when the
    /// front end cannot or the operator asked not to, so the flow shows the link.
    /// </summary>
    Task<bool> OpenLinkAsync(Uri link, CancellationToken cancellationToken);
}
