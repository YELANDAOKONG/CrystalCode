using System.Text.Json;

using CrystalCode.Engine.Home;

namespace CrystalCode.Server.Hosting;

/// <summary>
/// The owner-only file <c>~/.crystal/server/&lt;pid&gt;.json</c> a wrapper reads
/// to find a running server. It holds the token only when the server
/// generated it. It is deleted on exit.
/// </summary>
internal sealed class ServerDiscovery
{
    private readonly string _path;

    private ServerDiscovery(string path)
    {
        _path = path;
    }

    public string Path => _path;

    public static ServerDiscovery Write(
        CrystalHome home,
        string url,
        string workspace,
        string? token,
        Action<Utf8JsonWriter> build)
    {
        var directory = System.IO.Path.Combine(home.Root, "server");
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var path = System.IO.Path.Combine(directory, Environment.ProcessId + ".json");
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(path, options))
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("url", url);
            writer.WriteNumber("pid", Environment.ProcessId);
            writer.WriteString("workspace", workspace);
            if (token is not null)
            {
                writer.WriteString("token", token);
            }

            writer.WriteStartObject("build");
            build(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        if (!OperatingSystem.IsWindows())
        {
            // A file that already existed keeps its old mode on Create.
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return new ServerDiscovery(path);
    }

    public void Delete()
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover file names a dead pid; wrappers check the pid.
        }
    }
}
