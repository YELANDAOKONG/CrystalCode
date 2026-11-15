namespace CrystalCode.Engine.Authentication.Interfaces;

/// <summary>
/// The surface a sign-in flow uses to talk to the operator. A front end
/// implements it. It carries no vendor concept.
/// </summary>
public interface ILoginInteraction
{
    /// <summary>
    /// Shows a line of text to the operator.
    /// </summary>
    Task ShowAsync(string text, CancellationToken cancellationToken);

    /// <summary>
    /// Shows a link with a short label. A front end that draws shows it so it
    /// can be copied whole, without wrapping it inside a frame.
    /// </summary>
    Task ShowLinkAsync(string label, Uri link, CancellationToken cancellationToken);

    /// <summary>
    /// Asks the operator for a line of text. Throws
    /// <see cref="OperationCanceledException"/> when the token is cancelled
    /// before an answer arrives.
    /// </summary>
    Task<string> ReadAsync(string prompt, CancellationToken cancellationToken);

    /// <summary>
    /// Tries to open a link in the operator's browser. Returns false when the
    /// front end cannot, so the flow can show the link instead.
    /// </summary>
    Task<bool> OpenLinkAsync(Uri link, CancellationToken cancellationToken);
}
