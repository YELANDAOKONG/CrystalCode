using System.Runtime.InteropServices;

namespace CrystalCode.Engine.Tools;

/// <summary>
/// Distinguishes a regular file from a pipe, device, socket, or directory
/// before the host opens it. Opening a FIFO blocks, and that wait cannot
/// observe cancellation.
/// </summary>
internal static class RegularFile
{
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixRegularFile = 0x8000;
    private const uint InvalidFileAttributes = 0xFFFFFFFF;
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeDevice = 0x40;
    private const uint FileAttributeReparsePoint = 0x400;

    public static bool IsRegular(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (OperatingSystem.IsWindows())
        {
            return IsWindowsRegular(path);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return IsUnixRegular(path);
        }

        return false;
    }

    private static bool IsWindowsRegular(string path)
    {
        if (path.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(@"\\?\pipe\", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var attributes = GetFileAttributes(path);
        if (attributes == InvalidFileAttributes)
        {
            return false;
        }

        return (attributes & (FileAttributeDirectory | FileAttributeDevice | FileAttributeReparsePoint)) == 0;
    }

    private static bool IsUnixRegular(string path)
    {
        if (Stat(path, out var status) != 0)
        {
            return false;
        }

        return (status.Mode & UnixFileTypeMask) == UnixRegularFile;
    }

    // Field order matches SystemNative_Stat's FileStatus in .NET 10.
    [StructLayout(LayoutKind.Sequential)]
    private struct UnixFileStatus
    {
        public int Flags;
        public int Mode;
        public uint UserId;
        public uint GroupId;
        public long Size;
        public long AccessTimeSeconds;
        public long AccessTimeNanoseconds;
        public long ModificationTimeSeconds;
        public long ModificationTimeNanoseconds;
        public long StatusChangeTimeSeconds;
        public long StatusChangeTimeNanoseconds;
        public long BirthTimeSeconds;
        public long BirthTimeNanoseconds;
        public long Device;
        public long DeviceType;
        public long Inode;
        public uint UserFlags;
    }

    [DllImport("libSystem.Native", EntryPoint = "SystemNative_Stat", SetLastError = true)]
    private static extern int Stat(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        out UnixFileStatus status);

    [DllImport("kernel32.dll", EntryPoint = "GetFileAttributesW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFileAttributes(string path);
}
