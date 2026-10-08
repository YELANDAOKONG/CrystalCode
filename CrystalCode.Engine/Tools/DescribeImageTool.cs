using Crystal.Tools;

using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Describes an image file through a configured vision model so a model
/// that cannot see images can still read one.
/// </summary>
public sealed class DescribeImageTool : ITool
{
    public const string ToolName = "describe_image";

    private const string ToolDescription =
        "Describes an image file through a vision model and returns text. "
        + "path may be workspace-relative or absolute. "
        + "Paths inside the workspace run without asking. "
        + "Paths outside the workspace require approval. "
        + "Use question when you need a specific answer from the image. "
        + "PNG, JPEG, GIF, and WebP files up to 20 MiB are accepted.";

    private readonly Workspace _workspace;
    private readonly ImageDescriber _describer;

    public DescribeImageTool(Workspace workspace, ImageDescriber describer)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(describer);
        _workspace = workspace;
        _describer = describer;
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
                    },
                    "question": {
                      "type": "string",
                      "description": "Optional question to answer from the image."
                    }
                  },
                  "required": ["path"]
                }
                """),
            ToolDescription);
    }

    public ToolDefinition Definition { get; }

    public async ValueTask<ToolOutput> InvokeAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);
        if (!ToolArguments.TryReadRequiredString(call.Arguments, "path", out var path)
            || !ToolArguments.TryReadOptionalString(call.Arguments, "question", out var question))
        {
            return new ToolOutput(
                "Arguments must include path, with an optional question string.",
                ToolResultStatus.Failure);
        }

        if (!ImageAccess.TryLoad(_workspace, path, out var image, out var error) || image is null)
        {
            return new ToolOutput(error, ToolResultStatus.Failure);
        }

        try
        {
            var text = await _describer.DescribeAsync(
                image.Data!.Value,
                image.MimeType,
                question,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
            {
                return new ToolOutput(
                    "The image model returned no description.",
                    ToolResultStatus.Failure);
            }

            return new ToolOutput(ToolOutputText.Truncate(text.Trim()));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ToolOutput(
                "The image model failed: " + exception.Message,
                ToolResultStatus.Failure);
        }
    }
}
