using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace Jellyfin.Plugin.JellyP.Infrastructure;

/// <summary>Bounded reads and durable atomic writes for plugin-owned data.</summary>
public static class DurableFiles
{
    /// <summary>Reads at most the configured encoded image limit, including on file growth.</summary>
    public static byte[] ReadImage(string path)
    {
        EnsureRegularPath(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        const int limit = Conversion.WebpImageConverter.MaximumInputBytes;
        if (file.Length is <= 0 or > limit)
        {
            throw new InvalidDataException("Image is empty or exceeds 32 MiB.");
        }

        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        if (file.ReadByte() != -1)
        {
            throw new IOException("Image changed during read.");
        }

        return bytes;
    }

    /// <summary>Rejects symlinks in every existing component of a path.</summary>
    public static void EnsureRegularPath(string path)
    {
        var absolute = System.IO.Path.GetFullPath(path);
        for (var current = absolute; !string.IsNullOrEmpty(current); current = System.IO.Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Symbolic links are excluded from artwork migration.");
            }
        }
    }

    /// <summary>Returns a hex SHA256.</summary>
    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>Writes and flushes bytes before atomically publishing the plugin-owned file.</summary>
    public static void Write(string path, ReadOnlySpan<byte> bytes)
    {
        EnsureDirectory(System.IO.Path.GetDirectoryName(path)!);
        EnsureRegularPath(System.IO.Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, path, overwrite: true);
        FlushDirectory(System.IO.Path.GetDirectoryName(path)!);
    }

    /// <summary>Creates recovery directories with durable parent entries on Linux.</summary>
    public static void CreateDirectory(string path)
    {
        EnsureDirectory(path);
        EnsureRegularPath(path);
    }

    /// <summary>Persists artwork directory entries after Jellyfin saves a verified file on Linux.</summary>
    public static void FlushParentDirectory(string path)
    {
        // Jellyfin may have created a new canonical directory for legacy artwork. Persist its ancestors too.
        for (var parent = System.IO.Path.GetDirectoryName(path); !string.IsNullOrEmpty(parent); parent = System.IO.Path.GetDirectoryName(parent))
        {
            if (Directory.Exists(parent)) FlushDirectory(parent);
        }
    }

    /// <summary>Checks existing destination ancestors without creating missing directories before journaling.</summary>
    public static void EnsureSafeParent(string path)
    {
        for (var parent = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)); !string.IsNullOrEmpty(parent); parent = System.IO.Path.GetDirectoryName(parent))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(parent); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0 || (attributes & FileAttributes.Directory) == 0)
                throw new IOException("Artwork destination parent is not a regular directory.");
            EnsureRegularPath(parent);
            return;
        }

        throw new IOException("Artwork destination has no existing filesystem parent.");
    }

    /// <summary>Deletes plugin-owned data and persists its directory entry on Linux.</summary>
    public static void Delete(string path)
    {
        File.Delete(path);
        if (Directory.Exists(System.IO.Path.GetDirectoryName(path))) FlushDirectory(System.IO.Path.GetDirectoryName(path)!);
    }

    private static void EnsureDirectory(string path)
    {
        if (Directory.Exists(path)) return;
        var parent = System.IO.Path.GetDirectoryName(path);
        if (parent is not null) EnsureDirectory(parent);
        Directory.CreateDirectory(path);
        if (parent is not null) FlushDirectory(parent);
    }

    private static void FlushDirectory(string path)
    {
        if (!OperatingSystem.IsLinux()) return; // Process-crash recovery only on other platforms; see runtime support.
        EnsureRegularPath(path);
        if ((File.GetAttributes(path) & FileAttributes.Directory) == 0)
            throw new IOException("Recovery durability target is not a directory.");
        // O_RDONLY is zero on Linux architectures; O_DIRECTORY has different values on x64 and ARM64.
        // Validate the plugin-owned directory above instead of passing an architecture-specific flag.
        var descriptor = OpenDirectory(path, 0);
        if (descriptor < 0)
        {
            var error = Marshal.GetLastPInvokeError();
            throw new IOException($"Cannot open recovery directory for durability (errno {error}).");
        }
        try
        {
            if (Fsync(descriptor) != 0)
            {
                var error = Marshal.GetLastPInvokeError();
                throw new IOException($"Cannot synchronize recovery directory (errno {error}).");
            }
        }
        finally { _ = Close(descriptor); }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenDirectory(string path, int flags);
    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(int fd);
    [DllImport("libc", EntryPoint = "close")]
    private static extern int Close(int fd);

    /// <summary>Persists a JSON recovery record.</summary>
    public static void WriteJson<T>(string path, T value) => Write(path, JsonSerializer.SerializeToUtf8Bytes(value));

    /// <summary>Reads a JSON recovery record; invalid records fail closed.</summary>
    public static T ReadJson<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllBytes(path))
        ?? throw new InvalidDataException($"Invalid JellyP journal: {System.IO.Path.GetFileName(path)}");

    /// <summary>Checks both the backup and target volumes before writing.</summary>
    public static void EnsureSpace(string path, long requiredBytes)
    {
        var absolute = System.IO.Path.GetFullPath(path);
        // Use the longest matching mount, rather than '/' on Unix with nested mounts.
        var volume = DriveInfo.GetDrives()
            .Where(d => absolute.StartsWith(d.Name.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .OrderByDescending(d => d.Name.Length).FirstOrDefault()
            ?? new DriveInfo(System.IO.Path.GetPathRoot(absolute)!);
        if (!volume.IsReady || volume.AvailableFreeSpace < requiredBytes)
        {
            throw new IOException("Free-space reserve would be exceeded.");
        }
    }
}
