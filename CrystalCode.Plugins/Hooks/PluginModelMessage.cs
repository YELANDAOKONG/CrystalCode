using Crystal.Chat;

namespace CrystalCode.Plugins.Hooks;

/// <summary>
/// One chat message in an outbound model request. <see cref="Text"/> uses the
/// visible image marker spelling. <see cref="Images"/> lists the attachments.
/// </summary>
public sealed record PluginModelMessage : PluginModelItem
{
    public PluginModelMessage(
        string id,
        ChatRole role,
        string text,
        IEnumerable<PluginModelImage>? images = null)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(text);
        Role = role;
        Text = text;
        Images = images is null ? [] : [.. images];
    }

    public ChatRole Role { get; }

    public string Text { get; }

    public IReadOnlyList<PluginModelImage> Images { get; }

    public override string ToString() => nameof(PluginModelMessage);
}
