using System.Text;

namespace CrystalCode.Engine.Home;

/// <summary>Reloads direct Markdown children without creating or watching files.</summary>
public sealed class QuickPromptStore(CrystalHome home)
{
    public const int MaximumFileBytes = 64 * 1024;
    private const int MaximumFiles = 256;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _directory = home.ShortcutsDirectory;

    public async Task<QuickPromptCatalog> LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Enumeration and opening a file can block even when its body is read asynchronously.
        return await Task.Run(() => LoadCoreAsync(cancellationToken), cancellationToken);
    }

    private async Task<QuickPromptCatalog> LoadCoreAsync(CancellationToken cancellationToken)
    {
        var entries = new List<QuickPrompt>();
        try
        {
            var files = Directory.EnumerateFiles(_directory, "*", SearchOption.TopDirectoryOnly)
                .Where(path => string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase))
                .Take(MaximumFiles + 1)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => path, StringComparer.Ordinal)
                .ToArray();
            foreach (var path in files.Take(MaximumFiles))
            {
                cancellationToken.ThrowIfCancellationRequested();
                entries.Add(await ReadAsync(path, cancellationToken));
            }

            return new QuickPromptCatalog(_directory, entries.AsReadOnly(),
                files.Length > MaximumFiles ? $"Only the first {MaximumFiles} Markdown files are shown." : null);
        }
        catch (DirectoryNotFoundException)
        {
            return new QuickPromptCatalog(_directory, entries.AsReadOnly());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new QuickPromptCatalog(_directory, entries.AsReadOnly(), "Quick prompts could not be loaded.");
        }
    }

    private static async Task<QuickPrompt> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            // Avoid reading through a shortcut into an unrelated private file.
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                return new QuickPrompt(name, string.Empty, "Symbolic links are not supported.");
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > MaximumFileBytes)
            {
                return new QuickPrompt(name, string.Empty, "File exceeds the 64 KiB limit.");
            }

            var bytes = new byte[MaximumFileBytes + 1];
            var length = 0;
            while (length < bytes.Length)
            {
                var read = await stream.ReadAsync(bytes.AsMemory(length), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                length += read;
            }

            if (length > MaximumFileBytes)
            {
                return new QuickPrompt(name, string.Empty, "File exceeds the 64 KiB limit.");
            }

            var text = StrictUtf8.GetString(bytes, 0, length);
            if (text.StartsWith('﻿'))
            {
                text = text[1..];
            }

            var error = ValidateText(text);
            return new QuickPrompt(name, error is null ? text : string.Empty, error);
        }
        catch (DecoderFallbackException)
        {
            return new QuickPrompt(name, string.Empty, "File must contain valid UTF-8 text.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new QuickPrompt(name, string.Empty, "File could not be read.");
        }
    }

    public static string? ValidateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Prompt is empty.";
        }

        if (text.TrimStart().StartsWith('/'))
        {
            return "Quick prompts cannot start with a slash command.";
        }

        if (StrictUtf8.GetByteCount(text) > MaximumFileBytes)
        {
            return "Prompt exceeds the 64 KiB limit.";
        }

        if (text.Any(ch => (char.IsControl(ch) && ch is not ('\r' or '\n' or '\t')) || ch == '\u2063'))
        {
            return "Prompt contains unsupported control or attachment marker characters.";
        }

        return null;
    }
}
