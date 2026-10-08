using Crystal.Media;
using Crystal.Multimodal;
using Crystal.Multimodal.Tools;
using Crystal.Tools;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Returns an image file to a model that can see images directly.
/// </summary>
public sealed class ViewImageTool : IMultimodalTool
{
    public const string ToolName = "view_image";

    private const string ToolDescription =
        "Shows an image file to you. "
        + "path may be workspace-relative or absolute. "
        + "Paths inside the workspace run without asking. "
        + "Paths outside the workspace require approval. "
        + "PNG, JPEG, GIF, and WebP files up to 20 MiB are accepted.";

    private readonly Workspace _workspace;

    public ViewImageTool(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
        Definition = new ToolDefinition(
            ToolName,
            ToolSchema.Parse(
                """
                {
                  "type": "object",
                  "properties": {
                    "path": {
                      "type": "string",
                      "description": "Workspace-relative or absolute image path."
                    }
                  },
                  "required": ["path"]
                }
                """),
            ToolDescription);
    }

    public ToolDefinition Definition { get; }

    public static IReadOnlyList<IMultimodalTool> ForSession(
        Workspace workspace,
        bool sessionAcceptsImages)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!sessionAcceptsImages)
        {
            return [];
        }

        return [new ViewImageTool(workspace)];
    }

    public ValueTask<MultimodalToolOutput> InvokeAsync(
        MultimodalToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);
        if (!ToolArguments.TryReadRequiredString(call.Arguments, "path", out var path))
        {
            return ValueTask.FromResult(Failed(
                "Arguments must include path."));
        }

        if (!ImageAccess.TryLoad(_workspace, path, out var image, out var error) || image is null)
        {
            return ValueTask.FromResult(Failed(error));
        }

        return ValueTask.FromResult(new MultimodalToolOutput(
        [
            new TextContent("Image loaded."),
            new ImageContent(new ImageMedia(
                new InlineMediaSource(image.Data!.Value),
                new MediaMimeType(image.MimeType)))
        ]));
    }

    private static MultimodalToolOutput Failed(string text) =>
        new([new TextContent(text)], MultimodalToolResultStatus.Failure);
}
