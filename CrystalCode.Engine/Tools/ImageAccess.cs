using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Loads one image file with the same path fence as <see cref="ReadTool"/>.
/// </summary>
internal static class ImageAccess
{
    public static bool TryLoad(
        Workspace workspace,
        string path,
        out ImageAttachment? image,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        image = null;
        if (Workspace.IsCredentialPath(path))
        {
            error = "Reading credential paths is not allowed.";
            return false;
        }

        if (!workspace.TryResolveReadableFile(path, out var fullPath, out error))
        {
            return false;
        }

        if (Workspace.IsCredentialPath(fullPath))
        {
            error = "Reading credential paths is not allowed.";
            return false;
        }

        try
        {
            image = ImageFile.Load(fullPath, number: 1);
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or InvalidDataException
            or UnauthorizedAccessException)
        {
            error = exception.Message;
            return false;
        }
    }
}
